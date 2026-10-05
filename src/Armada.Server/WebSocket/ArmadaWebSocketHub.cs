namespace Armada.Server.WebSocket
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net.WebSockets;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.WebSockets;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>
    /// WebSocket hub for real-time event delivery on the main Watson REST server at /ws. Every upgrade must be
    /// authenticated (see <see cref="AuthorizeUpgradeAsync"/>); each connected socket carries its identity and
    /// receives only the events it is entitled to: entity changes of its own tenant (global admins may opt in to every
    /// tenant with <c>{ "Route": "subscribe", "AllTenants": true }</c>) and user-scoped events (Ask Armada threads)
    /// addressed to its own user. Commands are authorized by <see cref="WebSocketCommandHandler.IsAuthorized"/>.
    /// </summary>
    /// <remarks>Thread safety: the session registry is a concurrent dictionary; broadcast methods may be called from any
    /// thread.</remarks>
    public class ArmadaWebSocketHub
    {
        #region Public-Members

        /// <summary>
        /// Raised after an entity change is broadcast, with the entity type (mission, voyage, captain, check-run,
        /// merge-entry) and entity id. Used by in-process observers such as the Ask Armada work tracker.
        /// </summary>
        public event Action<string, string>? EntityChanged;

        /// <summary>
        /// Number of connected, authenticated sockets.
        /// </summary>
        public int ConnectionCount => _Clients.Count;

        #endregion

        #region Private-Members

        private string _Header = "[WebSocketHub] ";
        private LoggingModule _Logging;
        private IAdmiralService _Admiral;
        private WebSocketCommandHandler _CommandHandler;
        private Func<string?, string?, string?, Task<AuthContext>>? _Authenticate;
        private ArmadaSettings? _Settings;
        private ConcurrentDictionary<Guid, WebSocketClientState> _Clients = new ConcurrentDictionary<Guid, WebSocketClientState>();
        private ConditionalWeakTable<HttpContextBase, AuthContext> _UpgradeIdentities = new ConditionalWeakTable<HttpContextBase, AuthContext>();

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the WebSocket hub.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="admiral">Admiral service for command handling.</param>
        /// <param name="database">Database driver for data access.</param>
        /// <param name="mergeQueue">Merge queue service.</param>
        /// <param name="settings">Optional Armada settings for log/diff paths.</param>
        /// <param name="git">Optional git service for diff generation.</param>
        /// <param name="onStop">Optional callback invoked when stop_server is requested.</param>
        /// <param name="authenticate">Credential resolver (Authorization header, X-Token, X-Api-Key) used to authenticate
        /// upgrades; the same resolver as the REST API. When null every upgrade is rejected.</param>
        public ArmadaWebSocketHub(LoggingModule logging, IAdmiralService admiral, DatabaseDriver database, IMergeQueueService mergeQueue, ArmadaSettings? settings = null, IGitService? git = null, Action? onStop = null, Func<string?, string?, string?, Task<AuthContext>>? authenticate = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Admiral = admiral ?? throw new ArgumentNullException(nameof(admiral));
            _Authenticate = authenticate;
            _Settings = settings;

            _CommandHandler = new WebSocketCommandHandler(
                _Admiral,
                database ?? throw new ArgumentNullException(nameof(database)),
                mergeQueue ?? throw new ArgumentNullException(nameof(mergeQueue)),
                settings,
                git,
                onStop,
                _JsonOptions,
                (mission, status) => BroadcastMissionChange(mission, status),
                (voyage, status) => BroadcastVoyageChange(voyage, status));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Authenticate a /ws upgrade request before the handshake completes. Credentials are read from the REST headers
        /// (Authorization: Bearer, X-Token, X-Api-Key), the <c>token</c> query parameter, and the Sec-WebSocket-Protocol
        /// header (see <see cref="WebSocketCredentials"/>). On success the identity is remembered for
        /// <see cref="HandleWebSocketAsync"/>; on failure the caller must reject the upgrade with 401.
        /// </summary>
        /// <param name="ctx">HTTP context of the upgrade request.</param>
        /// <returns>The resolved identity; <see cref="AuthContext.IsAuthenticated"/> is false when rejected.</returns>
        public async Task<AuthContext> AuthorizeUpgradeAsync(HttpContextBase ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            AuthContext result = new AuthContext();
            if (_Authenticate == null) return result;

            WebSocketCredentials credentials = ExtractCredentials(ctx);
            if (!credentials.HasAny) return result;

            try
            {
                if (!String.IsNullOrWhiteSpace(credentials.AuthorizationHeader)
                    || !String.IsNullOrWhiteSpace(credentials.TokenHeader)
                    || !String.IsNullOrWhiteSpace(credentials.ApiKeyHeader))
                {
                    result = await _Authenticate(credentials.AuthorizationHeader, credentials.TokenHeader, credentials.ApiKeyHeader).ConfigureAwait(false);
                }

                if (!IsUsable(result))
                {
                    foreach (string candidate in credentials.TokenCandidates())
                    {
                        result = await _Authenticate("Bearer " + candidate, candidate, candidate).ConfigureAwait(false);
                        if (IsUsable(result)) break;
                    }
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "upgrade authentication error: " + ex.Message);
                result = new AuthContext();
            }

            if (!IsUsable(result)) return new AuthContext();

            _UpgradeIdentities.Remove(ctx);
            _UpgradeIdentities.Add(ctx, result);
            return result;
        }

        /// <summary>
        /// Watson WebSocket route handler registered at /ws. The upgrade must already have been authorized by
        /// <see cref="AuthorizeUpgradeAsync"/>; an unauthorized session is closed immediately with a policy violation.
        /// </summary>
        /// <param name="ctx">HTTP context for the upgrade request.</param>
        /// <param name="session">Watson WebSocket session.</param>
        public async Task HandleWebSocketAsync(HttpContextBase ctx, WebSocketSession session)
        {
            if (!_UpgradeIdentities.TryGetValue(ctx, out AuthContext? auth) || !IsUsable(auth))
            {
                _Logging.Warn(_Header + "closing unauthenticated socket from " + session.RemoteIp + ":" + session.RemotePort);
                try { await session.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Authentication required", CancellationToken.None).ConfigureAwait(false); }
                catch { }
                return;
            }

            _UpgradeIdentities.Remove(ctx);
            WebSocketClientState client = new WebSocketClientState(session, auth!);
            _Clients.TryAdd(session.Id, client);
            _Logging.Debug(_Header + "client connected: " + session.RemoteIp + ":" + session.RemotePort + " tenant " + client.TenantId + " user " + client.UserId);

            try
            {
                await foreach (WebSocketMessage message in session.ReadMessagesAsync(ctx.Token))
                {
                    if (message.MessageType != WebSocketMessageType.Text) continue;
                    await HandleMessageAsync(client, message.Text).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Server shutting down or client disconnected normally.
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "session error: " + ex.ToString());
            }
            finally
            {
                _Clients.TryRemove(session.Id, out _);
                _Logging.Debug(_Header + "client disconnected: " + session.RemoteIp + ":" + session.RemotePort);
            }
        }

        /// <summary>
        /// Deliver an event to every socket of a tenant (plus global admins that opted in to all tenants). A null or
        /// empty tenant reaches only global admins.
        /// </summary>
        /// <param name="tenantId">Tenant that owns the changed entity.</param>
        /// <param name="eventType">Event type string.</param>
        /// <param name="data">Event payload.</param>
        public void BroadcastToTenant(string? tenantId, string eventType, object? data)
        {
            BroadcastToTenant(tenantId, eventType, null, data);
        }

        /// <summary>
        /// Deliver an event with a human-readable message to every socket of a tenant (plus global admins that opted in
        /// to all tenants). A null or empty tenant reaches only global admins.
        /// </summary>
        /// <param name="tenantId">Tenant that owns the changed entity.</param>
        /// <param name="eventType">Event type string.</param>
        /// <param name="message">Optional human-readable message.</param>
        /// <param name="data">Event payload.</param>
        public void BroadcastToTenant(string? tenantId, string eventType, string? message, object? data)
        {
            if (String.IsNullOrEmpty(eventType)) throw new ArgumentNullException(nameof(eventType));
            object payload = BuildPayload(eventType, message, data);
            Deliver(payload, client => IsTenantRecipient(client, tenantId));
        }

        /// <summary>
        /// Deliver an event only to the sockets of one user in one tenant (Ask Armada thread events). Admin all-tenant
        /// opt-in does not apply: user-scoped events are private.
        /// </summary>
        /// <param name="tenantId">Tenant of the user.</param>
        /// <param name="userId">User identifier.</param>
        /// <param name="eventType">Event type string.</param>
        /// <param name="data">Event payload.</param>
        public void SendToUser(string tenantId, string userId, string eventType, object data)
        {
            if (String.IsNullOrEmpty(eventType)) throw new ArgumentNullException(nameof(eventType));
            if (String.IsNullOrEmpty(tenantId) || String.IsNullOrEmpty(userId)) return;
            object payload = BuildPayload(eventType, null, data);
            Deliver(payload, client => IsUserRecipient(client, tenantId, userId));
        }

        /// <summary>
        /// Provide the CLI permission service for the CLI permission WebSocket commands.
        /// </summary>
        /// <param name="service">Service, or null.</param>
        public void SetCliPermissionService(CliPermissionService? service)
        {
            _CommandHandler.CliPermissions = service;
        }

        /// <summary>
        /// Deliver a CLI permission event (cli_permission.requested or cli_permission.resolved) to the request's
        /// approvers and owner: global admins (of this tenant, or opted in to all tenants), the tenant's tenant admins,
        /// and the owning user. Each recipient's copy carries its own canDecide and canRemember.
        /// </summary>
        /// <param name="eventType">Event type.</param>
        /// <param name="request">Request.</param>
        public void BroadcastCliPermission(string eventType, CliPermissionRequest request)
        {
            if (String.IsNullOrEmpty(eventType)) throw new ArgumentNullException(nameof(eventType));
            if (request == null) return;
            try
            {
                string requestJson = JsonSerializer.Serialize(request, _JsonOptions);
                foreach (KeyValuePair<Guid, WebSocketClientState> kvp in _Clients)
                {
                    WebSocketClientState client = kvp.Value;
                    if (!client.Session.IsConnected)
                    {
                        _Clients.TryRemove(kvp.Key, out _);
                        continue;
                    }

                    if (!IsCliPermissionRecipient(client, request)) continue;
                    CliPermissionRequest copy = JsonSerializer.Deserialize<CliPermissionRequest>(requestJson, _JsonOptions) ?? request;
                    CliPermissionSettings permissions = _Settings?.Permissions ?? new CliPermissionSettings();
                    copy.CanDecide = copy.Status == CliPermissionRequestStatusEnum.Pending && CliPermissionAccess.CanDecide(client.Auth, copy, permissions);
                    copy.CanRemember = copy.CanDecide && CliPermissionAccess.CanRemember(client.Auth, copy);
                    object payload = BuildPayload(eventType, null, new { requestId = copy.Id, status = copy.Status, request = copy });
                    try
                    {
                        client.Session.SendTextAsync(JsonSerializer.Serialize(payload, _JsonOptions)).Wait();
                    }
                    catch
                    {
                        _Clients.TryRemove(kvp.Key, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "CLI permission broadcast error: " + ex.Message);
            }
        }

        /// <summary>
        /// Whether a socket identity should receive a CLI permission event. Exposed for unit tests.
        /// </summary>
        /// <param name="client">Socket identity.</param>
        /// <param name="request">Request.</param>
        /// <returns>True when the event may be delivered.</returns>
        public static bool IsCliPermissionRecipient(WebSocketClientState client, CliPermissionRequest request)
        {
            if (client == null || request == null) return false;
            if (!CliPermissionAccess.CanView(client.Auth, request)) return false;
            if (client.IsAdmin && !client.AllTenants && !String.Equals(client.TenantId, request.TenantId, StringComparison.Ordinal))
                return String.Equals(client.UserId, request.UserId, StringComparison.Ordinal);
            return true;
        }

        /// <summary>
        /// Whether a socket identity should receive a tenant-scoped event. Exposed for unit tests.
        /// </summary>
        /// <param name="client">Socket identity.</param>
        /// <param name="tenantId">Tenant of the event, or null.</param>
        /// <returns>True when the event may be delivered.</returns>
        public static bool IsTenantRecipient(WebSocketClientState client, string? tenantId)
        {
            if (client == null) return false;
            if (client.IsAdmin && client.AllTenants) return true;
            if (String.IsNullOrEmpty(tenantId)) return client.IsAdmin;
            return String.Equals(client.TenantId, tenantId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether a socket identity should receive a user-scoped event. Exposed for unit tests.
        /// </summary>
        /// <param name="client">Socket identity.</param>
        /// <param name="tenantId">Tenant of the target user.</param>
        /// <param name="userId">Target user.</param>
        /// <returns>True when the event may be delivered.</returns>
        public static bool IsUserRecipient(WebSocketClientState client, string tenantId, string userId)
        {
            if (client == null) return false;
            return String.Equals(client.TenantId, tenantId, StringComparison.Ordinal)
                && String.Equals(client.UserId, userId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Broadcast a mission state change to the mission's tenant.
        /// </summary>
        /// <param name="mission">Changed mission.</param>
        /// <param name="statusOverride">Status to report instead of the mission's current status, or null.</param>
        public void BroadcastMissionChange(Mission mission, string? statusOverride = null)
        {
            if (mission == null) return;
            BroadcastToTenant(mission.TenantId, "mission.changed", new
            {
                id = mission.Id,
                title = mission.Title,
                status = statusOverride ?? mission.Status.ToString(),
                voyageId = mission.VoyageId
            });
            RaiseEntityChanged("mission", mission.Id);
        }

        /// <summary>
        /// Broadcast a voyage state change to the voyage's tenant.
        /// </summary>
        /// <param name="voyage">Changed voyage.</param>
        /// <param name="statusOverride">Status to report instead of the voyage's current status, or null.</param>
        public void BroadcastVoyageChange(Voyage voyage, string? statusOverride = null)
        {
            if (voyage == null) return;
            BroadcastToTenant(voyage.TenantId, "voyage.changed", new
            {
                id = voyage.Id,
                title = voyage.Title,
                status = statusOverride ?? voyage.Status.ToString()
            });
            RaiseEntityChanged("voyage", voyage.Id);
        }

        /// <summary>
        /// Broadcast a captain state change to the captain's tenant.
        /// </summary>
        /// <param name="captain">Changed captain.</param>
        public void BroadcastCaptainChange(Captain captain)
        {
            if (captain == null) return;
            BroadcastToTenant(captain.TenantId, "captain.changed", new
            {
                id = captain.Id,
                name = captain.Name,
                state = captain.State.ToString()
            });
            RaiseEntityChanged("captain", captain.Id);
        }

        /// <summary>
        /// Broadcast a structured check-run change to the run's tenant.
        /// </summary>
        /// <param name="run">Changed check run.</param>
        public void BroadcastCheckRunChange(CheckRun run)
        {
            if (run == null) return;
            BroadcastToTenant(run.TenantId, "check-run.changed", run);
            RaiseEntityChanged("check-run", run.Id);
        }

        /// <summary>
        /// Broadcast an objective change to the objective's tenant.
        /// </summary>
        /// <param name="objective">Changed objective.</param>
        public void BroadcastObjectiveChange(Objective objective)
        {
            if (objective == null) return;
            BroadcastToTenant(objective.TenantId, "objective.changed", objective);
        }

        /// <summary>
        /// Broadcast a deployment change to the deployment's tenant.
        /// </summary>
        /// <param name="deployment">Changed deployment.</param>
        public void BroadcastDeploymentChange(Deployment deployment)
        {
            if (deployment == null) return;
            BroadcastToTenant(deployment.TenantId, "deployment.changed", deployment);

            BroadcastToTenant(deployment.TenantId, "deployment.progress", new
            {
                deployment.Id,
                deployment.Title,
                deployment.Status,
                deployment.VerificationStatus,
                deployment.EnvironmentId,
                deployment.EnvironmentName,
                deployment.StartedUtc,
                deployment.CompletedUtc,
                deployment.LastUpdateUtc
            });

            if (!String.IsNullOrWhiteSpace(deployment.EnvironmentId) || !String.IsNullOrWhiteSpace(deployment.EnvironmentName))
            {
                BroadcastToTenant(deployment.TenantId, "environment.health", new
                {
                    deployment.EnvironmentId,
                    deployment.EnvironmentName,
                    deployment.Id,
                    deployment.Title,
                    deployment.Status,
                    deployment.VerificationStatus,
                    deployment.LastMonitoredUtc,
                    deployment.LastRegressionAlertUtc,
                    deployment.LatestMonitoringSummary,
                    deployment.MonitoringFailureCount
                });
            }
        }

        /// <summary>
        /// Broadcast an incident change to the incident's tenant.
        /// </summary>
        /// <param name="incident">Changed incident.</param>
        public void BroadcastIncidentChange(Incident incident)
        {
            if (incident == null) return;
            BroadcastToTenant(incident.TenantId, "incident.changed", incident);
        }

        /// <summary>
        /// Broadcast a runbook execution change to the execution's tenant.
        /// </summary>
        /// <param name="execution">Changed runbook execution.</param>
        public void BroadcastRunbookExecutionChange(RunbookExecution execution)
        {
            if (execution == null) return;
            BroadcastToTenant(execution.TenantId, "runbook-execution.changed", execution);
        }

        /// <summary>
        /// Broadcast an approval-needed notification to the mission's tenant when a mission enters review.
        /// </summary>
        /// <param name="mission">Mission awaiting approval.</param>
        public void BroadcastApprovalNeeded(Mission mission)
        {
            if (mission == null) return;
            BroadcastToTenant(mission.TenantId, "approval-needed", new
            {
                entityType = "mission",
                entityId = mission.Id,
                missionId = mission.Id,
                title = mission.Title,
                status = mission.Status.ToString(),
                vesselId = mission.VesselId,
                voyageId = mission.VoyageId,
                reviewRequestedUtc = mission.ReviewRequestedUtc
            });
        }

        /// <summary>
        /// Notify in-process observers that an entity changed without sending anything to sockets (for example a merge
        /// queue entry whose change has no dedicated socket event).
        /// </summary>
        /// <param name="entityType">Entity type.</param>
        /// <param name="entityId">Entity identifier.</param>
        public void NotifyEntityChanged(string entityType, string entityId)
        {
            RaiseEntityChanged(entityType, entityId);
        }

        #endregion

        #region Private-Methods

        private static bool IsUsable(AuthContext? auth)
        {
            return auth != null && auth.IsAuthenticated && !String.IsNullOrEmpty(auth.UserId) && String.IsNullOrEmpty(auth.AskThreadId) && String.IsNullOrEmpty(auth.MissionId);
        }

        private static WebSocketCredentials ExtractCredentials(HttpContextBase ctx)
        {
            WebSocketCredentials credentials = new WebSocketCredentials();
            credentials.AuthorizationHeader = ctx.Request.Headers.Get("Authorization");
            credentials.TokenHeader = ctx.Request.Headers.Get("X-Token");
            credentials.ApiKeyHeader = ctx.Request.Headers.Get("X-Api-Key");
            try { credentials.QueryToken = ctx.Request.Query?.Elements?.Get("token"); }
            catch { credentials.QueryToken = null; }
            credentials.ProtocolTokens = WebSocketCredentials.ParseProtocolTokens(ctx.Request.Headers.Get("Sec-WebSocket-Protocol"));
            return credentials;
        }

        private async Task HandleMessageAsync(WebSocketClientState client, string body)
        {
            WebSocketSession session = client.Session;
            WebSocketRouteMessage? routeMessage = null;
            try
            {
                routeMessage = JsonSerializer.Deserialize<WebSocketRouteMessage>(body, _JsonOptions);
            }
            catch (JsonException)
            {
                // Non-JSON falls through to the unknown-route reply below.
            }

            string? route = routeMessage?.Route;

            try
            {
                if (string.Equals(route, "subscribe", StringComparison.OrdinalIgnoreCase))
                {
                    client.AllTenants = routeMessage != null && routeMessage.AllTenants;
                    ArmadaStatus status = await _Admiral.GetStatusAsync(client.Auth).ConfigureAwait(false);
                    object initial = new
                    {
                        type = "status.snapshot",
                        data = status,
                        allTenants = client.AllTenants,
                        timestamp = DateTime.UtcNow
                    };
                    await session.SendTextAsync(JsonSerializer.Serialize(initial, _JsonOptions)).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(route, "command", StringComparison.OrdinalIgnoreCase))
                {
                    WebSocketCommand command = JsonSerializer.Deserialize<WebSocketCommand>(body, _JsonOptions) ?? new WebSocketCommand();
                    object result;
                    if (!WebSocketCommandHandler.IsAuthorized(client.Auth, command.Action))
                    {
                        result = WebSocketCommandError.Create(command.Action, WebSocketCommandErrorCodeEnum.Forbidden, "Forbidden: WebSocket commands require a global administrator. Use the REST API, which is tenant-scoped.");
                    }
                    else
                    {
                        result = await _CommandHandler.HandleCommandAsync(command.Action, command, body, client.Auth).ConfigureAwait(false);
                    }

                    await session.SendTextAsync(JsonSerializer.Serialize(result, _JsonOptions)).ConfigureAwait(false);
                    return;
                }

                string errorJson = JsonSerializer.Serialize(
                    new { type = "error", message = "Unknown route: " + (route ?? "null") + ". Send a message with route 'subscribe' or 'command'" },
                    _JsonOptions);
                await session.SendTextAsync(errorJson).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error handling message: " + ex.ToString());
                try
                {
                    string errorJson = JsonSerializer.Serialize(WebSocketCommandError.FromException(null, ex), _JsonOptions);
                    await session.SendTextAsync(errorJson).ConfigureAwait(false);
                }
                catch
                {
                    // Client may have disconnected before we could reply.
                }
            }
        }

        private static object BuildPayload(string eventType, string? message, object? data)
        {
            if (message == null)
            {
                return new
                {
                    type = eventType,
                    data = data,
                    timestamp = DateTime.UtcNow
                };
            }

            return new
            {
                type = eventType,
                message = message,
                data = data,
                timestamp = DateTime.UtcNow
            };
        }

        private void RaiseEntityChanged(string entityType, string entityId)
        {
            Action<string, string>? handler = EntityChanged;
            if (handler == null || String.IsNullOrEmpty(entityId)) return;
            try { handler(entityType, entityId); }
            catch (Exception ex) { _Logging.Warn(_Header + "entity change observer error: " + ex.Message); }
        }

        private void Deliver(object payload, Func<WebSocketClientState, bool> filter)
        {
            try
            {
                string json = JsonSerializer.Serialize(payload, _JsonOptions);

                foreach (KeyValuePair<Guid, WebSocketClientState> kvp in _Clients)
                {
                    WebSocketClientState client = kvp.Value;
                    WebSocketSession session = client.Session;
                    if (!session.IsConnected)
                    {
                        _Clients.TryRemove(kvp.Key, out _);
                        continue;
                    }

                    if (!filter(client)) continue;

                    try
                    {
                        session.SendTextAsync(json).Wait();
                    }
                    catch
                    {
                        _Clients.TryRemove(kvp.Key, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "broadcast error: " + ex.ToString());
            }
        }

        #endregion
    }
}
