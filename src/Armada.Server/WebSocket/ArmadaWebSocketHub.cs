namespace Armada.Server.WebSocket
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net.WebSockets;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.WebSockets;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>
    /// WebSocket hub for real-time event broadcasting.
    /// Runs on the main Watson7 REST server at the /ws path.
    /// Supports subscribe/command message routing and broadcasts mission/captain state changes.
    /// Provides full command parity with the REST API and MCP tools.
    /// </summary>
    public class ArmadaWebSocketHub
    {
        #region Private-Members

        private string _Header = "[WebSocketHub] ";
        private LoggingModule _Logging;
        private IAdmiralService _Admiral;
        private WebSocketCommandHandler _CommandHandler;
        private ConcurrentDictionary<Guid, WebSocketSession> _Sessions = new ConcurrentDictionary<Guid, WebSocketSession>();

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
        public ArmadaWebSocketHub(LoggingModule logging, IAdmiralService admiral, DatabaseDriver database, IMergeQueueService mergeQueue, ArmadaSettings? settings = null, IGitService? git = null, Action? onStop = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Admiral = admiral ?? throw new ArgumentNullException(nameof(admiral));

            _CommandHandler = new WebSocketCommandHandler(
                _Admiral,
                database ?? throw new ArgumentNullException(nameof(database)),
                mergeQueue ?? throw new ArgumentNullException(nameof(mergeQueue)),
                settings,
                git,
                onStop,
                _JsonOptions,
                BroadcastMissionChange,
                BroadcastVoyageChange);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Watson7 WebSocket route handler. Registered on the main server at /ws.
        /// Manages the full session lifecycle: connect, read loop, disconnect.
        /// </summary>
        /// <param name="ctx">HTTP context for the upgrade request.</param>
        /// <param name="session">Watson7 WebSocket session.</param>
        public async Task HandleWebSocketAsync(HttpContextBase ctx, WebSocketSession session)
        {
            _Sessions.TryAdd(session.Id, session);
            _Logging.Info(_Header + "client connected: " + session.RemoteIp + ":" + session.RemotePort);

            try
            {
                await foreach (WebSocketMessage message in session.ReadMessagesAsync(ctx.Token))
                {
                    if (message.MessageType != WebSocketMessageType.Text) continue;
                    await HandleMessageAsync(session, message.Text).ConfigureAwait(false);
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
                _Sessions.TryRemove(session.Id, out _);
                _Logging.Info(_Header + "client disconnected: " + session.RemoteIp + ":" + session.RemotePort);
            }
        }

        /// <summary>
        /// Broadcast a mission state change to all connected clients.
        /// </summary>
        /// <param name="missionId">Mission ID.</param>
        /// <param name="status">New status.</param>
        /// <param name="title">Mission title.</param>
        public void BroadcastMissionChange(string missionId, string status, string? title = null)
        {
            object payload = new
            {
                type = "mission.changed",
                data = new
                {
                    id = missionId,
                    title = title,
                    status = status
                },
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        /// <summary>
        /// Broadcast a voyage state change to all connected clients.
        /// </summary>
        /// <param name="voyageId">Voyage ID.</param>
        /// <param name="status">New status.</param>
        /// <param name="title">Voyage title.</param>
        public void BroadcastVoyageChange(string voyageId, string status, string? title = null)
        {
            object payload = new
            {
                type = "voyage.changed",
                data = new
                {
                    id = voyageId,
                    title = title,
                    status = status
                },
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        /// <summary>
        /// Broadcast a captain state change to all connected clients.
        /// </summary>
        /// <param name="captainId">Captain ID.</param>
        /// <param name="state">New state.</param>
        /// <param name="name">Captain name.</param>
        public void BroadcastCaptainChange(string captainId, string state, string? name = null)
        {
            object payload = new
            {
                type = "captain.changed",
                data = new
                {
                    id = captainId,
                    name = name,
                    state = state
                },
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        /// <summary>
        /// Broadcast a structured check-run change to all connected clients.
        /// </summary>
        /// <param name="run">Changed check run.</param>
        public void BroadcastCheckRunChange(CheckRun run)
        {
            object payload = new
            {
                type = "check-run.changed",
                data = run,
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        /// <summary>
        /// Broadcast an objective change to all connected clients.
        /// </summary>
        /// <param name="objective">Changed objective.</param>
        public void BroadcastObjectiveChange(Objective objective)
        {
            object payload = new
            {
                type = "objective.changed",
                data = objective,
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        /// <summary>
        /// Broadcast a deployment change to all connected clients.
        /// </summary>
        /// <param name="deployment">Changed deployment.</param>
        public void BroadcastDeploymentChange(Deployment deployment)
        {
            object changedPayload = new
            {
                type = "deployment.changed",
                data = deployment,
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(changedPayload);

            object progressPayload = new
            {
                type = "deployment.progress",
                data = new
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
                },
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(progressPayload);

            if (!String.IsNullOrWhiteSpace(deployment.EnvironmentId) || !String.IsNullOrWhiteSpace(deployment.EnvironmentName))
            {
                object environmentPayload = new
                {
                    type = "environment.health",
                    data = new
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
                    },
                    timestamp = DateTime.UtcNow
                };

                BroadcastEvent(environmentPayload);
            }
        }

        /// <summary>
        /// Broadcast an incident change to all connected clients.
        /// </summary>
        /// <param name="incident">Changed incident.</param>
        public void BroadcastIncidentChange(Incident incident)
        {
            object payload = new
            {
                type = "incident.changed",
                data = incident,
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        /// <summary>
        /// Broadcast a runbook execution change to all connected clients.
        /// </summary>
        /// <param name="execution">Changed runbook execution.</param>
        public void BroadcastRunbookExecutionChange(RunbookExecution execution)
        {
            object payload = new
            {
                type = "runbook-execution.changed",
                data = execution,
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        /// <summary>
        /// Broadcast an approval-needed notification when a mission enters review.
        /// </summary>
        /// <param name="mission">Mission awaiting approval.</param>
        public void BroadcastApprovalNeeded(Mission mission)
        {
            object payload = new
            {
                type = "approval-needed",
                data = new
                {
                    entityType = "mission",
                    entityId = mission.Id,
                    missionId = mission.Id,
                    title = mission.Title,
                    status = mission.Status.ToString(),
                    vesselId = mission.VesselId,
                    voyageId = mission.VoyageId,
                    reviewRequestedUtc = mission.ReviewRequestedUtc
                },
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        /// <summary>
        /// Broadcast a generic event to all connected clients.
        /// </summary>
        /// <param name="eventType">Event type string.</param>
        /// <param name="message">Event message.</param>
        /// <param name="data">Optional additional data.</param>
        public void BroadcastEvent(string eventType, string message, object? data = null)
        {
            object payload = new
            {
                type = eventType,
                message = message,
                data = data,
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        #endregion

        #region Private-Methods

        private async Task HandleMessageAsync(WebSocketSession session, string body)
        {
            string? route = null;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("route", out JsonElement routeEl))
                        route = routeEl.GetString();
                    if (route == null && doc.RootElement.TryGetProperty("Route", out JsonElement routeElPascal))
                        route = routeElPascal.GetString();
                }
            }
            catch
            {
                // Non-JSON or missing route falls through to default branch below.
            }

            try
            {
                if (string.Equals(route, "subscribe", StringComparison.OrdinalIgnoreCase))
                {
                    ArmadaStatus status = await _Admiral.GetStatusAsync().ConfigureAwait(false);
                    object initial = new
                    {
                        type = "status.snapshot",
                        data = status,
                        timestamp = DateTime.UtcNow
                    };
                    await session.SendTextAsync(JsonSerializer.Serialize(initial, _JsonOptions)).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(route, "command", StringComparison.OrdinalIgnoreCase))
                {
                    WebSocketCommand command = JsonSerializer.Deserialize<WebSocketCommand>(body, _JsonOptions) ?? new WebSocketCommand();
                    object result = await _CommandHandler.HandleCommandAsync(command.Action, command, body).ConfigureAwait(false);
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
                    string errorJson = JsonSerializer.Serialize(new { type = "command.error", error = ex.Message }, _JsonOptions);
                    await session.SendTextAsync(errorJson).ConfigureAwait(false);
                }
                catch
                {
                    // Client may have disconnected before we could reply.
                }
            }
        }

        private void BroadcastEvent(object payload)
        {
            try
            {
                string json = JsonSerializer.Serialize(payload, _JsonOptions);

                foreach (KeyValuePair<Guid, WebSocketSession> kvp in _Sessions)
                {
                    WebSocketSession session = kvp.Value;
                    if (!session.IsConnected)
                    {
                        _Sessions.TryRemove(kvp.Key, out _);
                        continue;
                    }
                    try
                    {
                        session.SendTextAsync(json).Wait();
                    }
                    catch
                    {
                        _Sessions.TryRemove(kvp.Key, out _);
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
