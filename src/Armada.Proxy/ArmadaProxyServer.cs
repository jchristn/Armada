namespace Armada.Proxy
{
    using System.Net.WebSockets;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Proxy.Models;
    using Armada.Proxy.Services;
    using Armada.Proxy.Settings;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using WatsonWebserver.Core.WebSockets;

    /// <summary>
    /// Watson-based remote proxy host for Armada remote management.
    /// </summary>
    public class ArmadaProxyServer : IDisposable
    {
        /// <summary>
        /// Callback invoked when the proxy host is stopping.
        /// </summary>
        public Action? OnStopping { get; set; }

        private readonly string _Header = "[ArmadaProxyServer] ";
        private readonly LoggingModule _Logging;
        private readonly ProxySettings _Settings;
        private readonly bool _Quiet;
        private readonly InstanceRegistry _Registry;
        private readonly string _WwwrootDirectory;
        private readonly DateTime _StartUtc = DateTime.UtcNow;

        private Webserver _Server = null!;
        private bool _Started = false;
        private bool _Disposed = false;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ArmadaProxyServer(LoggingModule logging, ProxySettings settings, bool quiet = false)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Quiet = quiet;
            _Registry = new InstanceRegistry(_Settings);
            _WwwrootDirectory = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        }

        /// <summary>
        /// Start the proxy host.
        /// </summary>
        public async Task StartAsync(CancellationToken token = default)
        {
            if (_Started)
            {
                return;
            }

            WebserverSettings webserverSettings = new WebserverSettings(_Settings.Hostname, _Settings.Port, false);
            webserverSettings.IO.EnableKeepAlive = true;
            webserverSettings.IO.ReadTimeoutMs = 30000;
            webserverSettings.Protocols.IdleTimeoutMs = 30000;
            webserverSettings.Timeout.DefaultTimeout = TimeSpan.FromSeconds(Math.Max(10, _Settings.RequestTimeoutSeconds));
            webserverSettings.WebSockets.Enable = true;
            webserverSettings.WebSockets.AllowClientSuppliedGuid = true;

            _Server = new Webserver(webserverSettings, DefaultRouteAsync);
            ConfigureServer(_Server);
            await _Server.StartAsync(token).ConfigureAwait(false);
            _Started = true;

            if (!_Quiet)
            {
                _Logging.Info(_Header + "proxy started on " + _Server.Settings.Prefix);
            }
        }

        /// <summary>
        /// Stop the proxy host.
        /// </summary>
        public void Stop()
        {
            if (!_Started)
            {
                return;
            }

            _Logging.Info(_Header + "stopping");
            _Server.Stop();
            _Started = false;
            OnStopping?.Invoke();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_Disposed)
            {
                return;
            }

            _Disposed = true;
            Stop();
            _Server?.Dispose();
        }

        private void ConfigureServer(Webserver server)
        {
            server.Events.Logger = message => _Logging.Debug(_Header + message);
            server.Events.ExceptionEncountered += (sender, args) =>
            {
                if (args?.Exception != null)
                {
                    _Logging.Warn(_Header + "watson exception: " + args.Exception);
                }
            };
            server.Events.ServerStarted += (sender, args) => _Logging.Info(_Header + "server listening");
            server.Events.ServerStopped += (sender, args) => _Logging.Info(_Header + "server stopped");
            server.Events.WebSocketSessionStarted += (sender, args) =>
            {
                if (args?.Session != null)
                {
                    _Logging.Info(_Header + "websocket connected " + args.Session.RemoteIp + ":" + args.Session.RemotePort + " " + args.Session.Request.Path);
                }
            };
            server.Events.WebSocketSessionEnded += (sender, args) =>
            {
                if (args?.Session != null)
                {
                    _Logging.Info(_Header + "websocket disconnected " + args.Session.RemoteIp + ":" + args.Session.RemotePort + " " + args.Session.Request.Path);
                }
            };

            server.Middleware.Add(async (ctx, next, token) =>
            {
                DateTime startUtc = DateTime.UtcNow;
                await next().ConfigureAwait(false);
                double elapsedMs = (DateTime.UtcNow - startUtc).TotalMilliseconds;
                _Logging.Debug(_Header + ctx.Request.Method + " " + ctx.Request.Url.RawWithQuery + " " + ctx.Response.StatusCode + " (" + elapsedMs.ToString("F2") + "ms)");
            });

            server.UseOpenApi(api =>
            {
                api.Info.Title = Constants.ProductName + " Proxy API";
                api.Info.Version = Constants.ProductVersion;
                api.Info.Description = "Remote proxy service for Armada instance discovery, tunnel routing, and bounded management actions.";
                api.Tags.Add(new OpenApiTag { Name = "Status", Description = "Proxy health and status routes" });
                api.Tags.Add(new OpenApiTag { Name = "Instances", Description = "Remote instance inspection and tunnel forwarding" });
            });

            RegisterStaticContent(server);
            RegisterApiRoutes(server);
            RegisterWebSocketRoutes(server);
        }

        private void RegisterStaticContent(Webserver server)
        {
            server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/", ServeIndexAsync);

            if (Directory.Exists(_WwwrootDirectory))
            {
                server.Routes.PreAuthentication.Content.BaseDirectory = _WwwrootDirectory;
                server.Routes.PreAuthentication.Content.Add("/", true);
            }
            else
            {
                _Logging.Warn(_Header + "wwwroot directory not found at " + _WwwrootDirectory);
            }
        }

        private void RegisterApiRoutes(Webserver server)
        {
            server.Get("/api/v1/status/health", async (req) => BuildHealthPayload());

            server.Get("/api/v1/instances", async (req) =>
            {
                List<RemoteInstanceSummary> instances = _Registry.ListSummaries();
                return new
                {
                    count = instances.Count,
                    instances = instances
                };
            });

            server.Get("/api/v1/instances/{instanceId}", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                RemoteInstanceRecord? record = _Registry.GetRecord(instanceId);
                if (record == null)
                {
                    throw new WebserverException(ApiResultEnum.NotFound, "Instance not found.");
                }

                return new
                {
                    summary = record.ToSummary(DateTime.UtcNow, _Settings.StaleAfterSeconds),
                    recentEvents = record.GetRecentEvents()
                };
            });

            server.Get("/api/v1/instances/{instanceId}/summary", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                return await ForwardPayloadAsync(req, instanceId, "armada.instance.summary", null).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/status/snapshot", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                return await ForwardTunnelResponseAsync(req, instanceId, "armada.status.snapshot", null).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/health", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                return await ForwardTunnelResponseAsync(req, instanceId, "armada.status.health", null).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/activity", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                int limit = ParsePositiveInt(req.Query["limit"], 20, 1, 100);
                return await ForwardPayloadAsync(req, instanceId, "armada.activity.recent", new RemoteTunnelQueryRequest { Limit = limit }).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/missions/recent", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                int limit = ParsePositiveInt(req.Query["limit"], 10, 1, 100);
                return await ForwardPayloadAsync(req, instanceId, "armada.missions.recent", new RemoteTunnelQueryRequest { Limit = limit }).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/voyages/recent", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                int limit = ParsePositiveInt(req.Query["limit"], 10, 1, 100);
                return await ForwardPayloadAsync(req, instanceId, "armada.voyages.recent", new RemoteTunnelQueryRequest { Limit = limit }).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/captains/recent", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                int limit = ParsePositiveInt(req.Query["limit"], 10, 1, 100);
                return await ForwardPayloadAsync(req, instanceId, "armada.captains.recent", new RemoteTunnelQueryRequest { Limit = limit }).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/missions/{missionId}", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                string missionId = RequireParameter(req, "missionId");
                return await ForwardPayloadAsync(req, instanceId, "armada.mission.detail", new RemoteTunnelQueryRequest { MissionId = missionId }).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/missions/{missionId}/log", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                string missionId = RequireParameter(req, "missionId");
                int offset = ParsePositiveInt(req.Query["offset"], 0, 0, Int32.MaxValue);
                int lines = ParsePositiveInt(req.Query["lines"], 200, 1, 2000);
                return await ForwardPayloadAsync(req, instanceId, "armada.mission.log", new RemoteTunnelQueryRequest
                {
                    MissionId = missionId,
                    Offset = offset,
                    Lines = lines
                }).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/missions/{missionId}/diff", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                string missionId = RequireParameter(req, "missionId");
                return await ForwardPayloadAsync(req, instanceId, "armada.mission.diff", new RemoteTunnelQueryRequest { MissionId = missionId }).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/voyages/{voyageId}", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                string voyageId = RequireParameter(req, "voyageId");
                return await ForwardPayloadAsync(req, instanceId, "armada.voyage.detail", new RemoteTunnelQueryRequest { VoyageId = voyageId }).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/captains/{captainId}", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                string captainId = RequireParameter(req, "captainId");
                return await ForwardPayloadAsync(req, instanceId, "armada.captain.detail", new RemoteTunnelQueryRequest { CaptainId = captainId }).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/captains/{captainId}/log", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                string captainId = RequireParameter(req, "captainId");
                int offset = ParsePositiveInt(req.Query["offset"], 0, 0, Int32.MaxValue);
                int lines = ParsePositiveInt(req.Query["lines"], 50, 1, 1000);
                return await ForwardPayloadAsync(req, instanceId, "armada.captain.log", new RemoteTunnelQueryRequest
                {
                    CaptainId = captainId,
                    Offset = offset,
                    Lines = lines
                }).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/fleets", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                int limit = ParsePositiveInt(req.Query["limit"], 12, 1, 200);
                return await ForwardPayloadAsync(req, instanceId, "armada.fleets.list", new RemoteTunnelQueryRequest { Limit = limit }).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/fleets/{fleetId}", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                string fleetId = RequireParameter(req, "fleetId");
                return await ForwardPayloadAsync(req, instanceId, "armada.fleet.detail", new RemoteTunnelQueryRequest { FleetId = fleetId }).ConfigureAwait(false);
            });

            server.Post("/api/v1/instances/{instanceId}/fleets", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                JsonElement payload = ReadJsonBody(req);
                req.Http.Response.StatusCode = 201;
                return await ForwardPayloadAsync(req, instanceId, "armada.fleet.create", payload).ConfigureAwait(false);
            });

            server.Put("/api/v1/instances/{instanceId}/fleets/{fleetId}", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                string fleetId = RequireParameter(req, "fleetId");
                JsonElement fleet = ReadJsonBody(req);
                return await ForwardPayloadAsync(req, instanceId, "armada.fleet.update", new { fleetId, fleet }).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/vessels", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                int limit = ParsePositiveInt(req.Query["limit"], 12, 1, 200);
                string? fleetId = GetOptionalValue(req.Query["fleetId"]);
                return await ForwardPayloadAsync(req, instanceId, "armada.vessels.list", new RemoteTunnelQueryRequest { Limit = limit, FleetId = fleetId }).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/vessels/{vesselId}", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                string vesselId = RequireParameter(req, "vesselId");
                return await ForwardPayloadAsync(req, instanceId, "armada.vessel.detail", new RemoteTunnelQueryRequest { VesselId = vesselId }).ConfigureAwait(false);
            });

            server.Post("/api/v1/instances/{instanceId}/vessels", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                JsonElement payload = ReadJsonBody(req);
                req.Http.Response.StatusCode = 201;
                return await ForwardPayloadAsync(req, instanceId, "armada.vessel.create", payload).ConfigureAwait(false);
            });

            server.Put("/api/v1/instances/{instanceId}/vessels/{vesselId}", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                string vesselId = RequireParameter(req, "vesselId");
                JsonElement vessel = ReadJsonBody(req);
                return await ForwardPayloadAsync(req, instanceId, "armada.vessel.update", new { vesselId, vessel }).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/voyages", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                int limit = ParsePositiveInt(req.Query["limit"], 12, 1, 200);
                string? status = GetOptionalValue(req.Query["status"]);
                return await ForwardPayloadAsync(req, instanceId, "armada.voyages.list", new RemoteTunnelQueryRequest { Limit = limit, Status = status }).ConfigureAwait(false);
            });

            server.Post("/api/v1/instances/{instanceId}/voyages/dispatch", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                JsonElement payload = ReadJsonBody(req);
                req.Http.Response.StatusCode = 201;
                return await ForwardPayloadAsync(req, instanceId, "armada.voyage.dispatch", payload).ConfigureAwait(false);
            });

            server.Delete("/api/v1/instances/{instanceId}/voyages/{voyageId}", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                string voyageId = RequireParameter(req, "voyageId");
                return await ForwardPayloadAsync(req, instanceId, "armada.voyage.cancel", new RemoteTunnelQueryRequest { VoyageId = voyageId }).ConfigureAwait(false);
            });

            server.Get("/api/v1/instances/{instanceId}/missions", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                int limit = ParsePositiveInt(req.Query["limit"], 16, 1, 200);
                return await ForwardPayloadAsync(req, instanceId, "armada.missions.list", new RemoteTunnelQueryRequest
                {
                    Limit = limit,
                    Status = GetOptionalValue(req.Query["status"]),
                    VoyageId = GetOptionalValue(req.Query["voyageId"]),
                    VesselId = GetOptionalValue(req.Query["vesselId"])
                }).ConfigureAwait(false);
            });

            server.Post("/api/v1/instances/{instanceId}/missions", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                JsonElement payload = ReadJsonBody(req);
                req.Http.Response.StatusCode = 201;
                return await ForwardPayloadAsync(req, instanceId, "armada.mission.create", payload).ConfigureAwait(false);
            });

            server.Put("/api/v1/instances/{instanceId}/missions/{missionId}", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                string missionId = RequireParameter(req, "missionId");
                JsonElement mission = ReadJsonBody(req);
                return await ForwardPayloadAsync(req, instanceId, "armada.mission.update", new { missionId, mission }).ConfigureAwait(false);
            });

            server.Delete("/api/v1/instances/{instanceId}/missions/{missionId}", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                string missionId = RequireParameter(req, "missionId");
                return await ForwardPayloadAsync(req, instanceId, "armada.mission.cancel", new RemoteTunnelQueryRequest { MissionId = missionId }).ConfigureAwait(false);
            });

            server.Post("/api/v1/instances/{instanceId}/missions/{missionId}/restart", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                string missionId = RequireParameter(req, "missionId");
                JsonElement payload = ReadJsonBody(req);
                return await ForwardPayloadAsync(req, instanceId, "armada.mission.restart", new
                {
                    missionId,
                    title = GetOptionalProperty(payload, "title"),
                    description = GetOptionalProperty(payload, "description")
                }).ConfigureAwait(false);
            });

            server.Post("/api/v1/instances/{instanceId}/captains/{captainId}/stop", async (req) =>
            {
                string instanceId = RequireParameter(req, "instanceId");
                string captainId = RequireParameter(req, "captainId");
                return await ForwardPayloadAsync(req, instanceId, "armada.captain.stop", new RemoteTunnelQueryRequest { CaptainId = captainId }).ConfigureAwait(false);
            });
        }

        private void RegisterWebSocketRoutes(Webserver server)
        {
            server.WebSocket("/tunnel", HandleTunnelAsync);
        }

        private object BuildHealthPayload()
        {
            List<RemoteInstanceSummary> instances = _Registry.ListSummaries();
            return new
            {
                healthy = true,
                product = "Armada.Proxy",
                version = Constants.ProductVersion,
                protocolVersion = Constants.RemoteTunnelProtocolVersion,
                port = _Settings.Port,
                startedUtc = _StartUtc,
                instances = new
                {
                    total = instances.Count,
                    connected = instances.Count(instance => String.Equals(instance.State, "connected", StringComparison.OrdinalIgnoreCase)),
                    stale = instances.Count(instance => String.Equals(instance.State, "stale", StringComparison.OrdinalIgnoreCase)),
                    offline = instances.Count(instance => String.Equals(instance.State, "offline", StringComparison.OrdinalIgnoreCase))
                }
            };
        }

        private async Task<object> ForwardPayloadAsync(ApiRequest req, string instanceId, string method, object? payload)
        {
            try
            {
                RemoteTunnelEnvelope response = await _Registry.SendRequestAsync(instanceId, method, payload, req.CancellationToken).ConfigureAwait(false);
                return BuildForwardedPayloadResult(req, response);
            }
            catch (Exception ex)
            {
                req.Http.Response.StatusCode = 400;
                return new { error = ex.Message };
            }
        }

        private async Task<object> ForwardTunnelResponseAsync(ApiRequest req, string instanceId, string method, object? payload)
        {
            try
            {
                RemoteTunnelEnvelope response = await _Registry.SendRequestAsync(instanceId, method, payload, req.CancellationToken).ConfigureAwait(false);
                return BuildTunnelProxyResponse(response);
            }
            catch (Exception ex)
            {
                req.Http.Response.StatusCode = 400;
                return new { error = ex.Message };
            }
        }

        private object BuildForwardedPayloadResult(ApiRequest req, RemoteTunnelEnvelope response)
        {
            object? payload = DeserializePayload(response.Payload);
            int statusCode = response.StatusCode ?? (response.Success == false ? 502 : 200);

            if (statusCode >= 200 && statusCode < 300 && String.IsNullOrWhiteSpace(response.ErrorCode))
            {
                req.Http.Response.StatusCode = statusCode;
                return payload ?? new { };
            }

            req.Http.Response.StatusCode = statusCode;
            return new
            {
                error = response.Message ?? "Tunnel request failed.",
                errorCode = response.ErrorCode,
                correlationId = response.CorrelationId,
                payload = payload
            };
        }

        private object BuildTunnelProxyResponse(RemoteTunnelEnvelope response)
        {
            return new
            {
                correlationId = response.CorrelationId,
                success = response.Success,
                statusCode = response.StatusCode,
                errorCode = response.ErrorCode,
                message = response.Message,
                payload = DeserializePayload(response.Payload)
            };
        }

        private Task HandleTunnelAsync(HttpContextBase ctx, WebSocketSession session)
        {
            return HandleTunnelInternalAsync(ctx, session);
        }

        private async Task HandleTunnelInternalAsync(HttpContextBase ctx, WebSocketSession session)
        {
            string? instanceId = null;

            try
            {
                using CancellationTokenSource handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(ctx.Token);
                handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(_Settings.HandshakeTimeoutSeconds));
                RemoteTunnelEnvelope firstEnvelope = await ReceiveEnvelopeAsync(session, handshakeTimeout.Token).ConfigureAwait(false);

                if (!String.Equals(firstEnvelope.Type, "request", StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(firstEnvelope.Method, "armada.tunnel.handshake", StringComparison.OrdinalIgnoreCase))
                {
                    await SendEnvelopeAsync(session, RemoteTunnelProtocol.CreateError(firstEnvelope.CorrelationId, "invalid_handshake", "First tunnel message must be armada.tunnel.handshake.", 400), ctx.Token).ConfigureAwait(false);
                    await session.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Handshake required", ctx.Token).ConfigureAwait(false);
                    return;
                }

                RemoteTunnelHandshakePayload? handshake = firstEnvelope.Payload?.Deserialize<RemoteTunnelHandshakePayload>(RemoteTunnelProtocol.JsonOptions);
                if (!_Registry.TryValidateHandshake(handshake, out string? handshakeError))
                {
                    await SendEnvelopeAsync(
                        session,
                        RemoteTunnelProtocol.CreateResponse(
                            firstEnvelope.CorrelationId,
                            new RemoteTunnelRequestResult
                            {
                                StatusCode = 401,
                                ErrorCode = "handshake_rejected",
                                Message = handshakeError
                            }),
                        ctx.Token).ConfigureAwait(false);
                    await session.CloseAsync(WebSocketCloseStatus.PolicyViolation, handshakeError ?? "Handshake rejected", ctx.Token).ConfigureAwait(false);
                    return;
                }

                RemoteInstanceSession proxySession = new RemoteInstanceSession((envelope, token) => SendEnvelopeAsync(session, envelope, token));
                instanceId = handshake!.InstanceId!.Trim();
                string remoteAddress = String.IsNullOrWhiteSpace(session.RemoteIp) ? "unknown" : session.RemoteIp + ":" + session.RemotePort;
                _Registry.RegisterHandshake(handshake, remoteAddress, proxySession);

                await SendEnvelopeAsync(
                    session,
                    RemoteTunnelProtocol.CreateResponse(
                        firstEnvelope.CorrelationId,
                        new RemoteTunnelRequestResult
                        {
                            StatusCode = 200,
                            Payload = new RemoteTunnelHandshakeResponse
                            {
                                Accepted = true,
                                ProxyVersion = Constants.ProductVersion,
                                ProtocolVersion = Constants.RemoteTunnelProtocolVersion,
                                InstanceId = instanceId,
                                Message = "Handshake accepted.",
                                Capabilities = GetCapabilities()
                            },
                            Message = "Handshake accepted."
                        }),
                    ctx.Token).ConfigureAwait(false);

                await foreach (WebSocketMessage message in session.ReadMessagesAsync(ctx.Token).ConfigureAwait(false))
                {
                    if (message.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    RemoteTunnelEnvelope envelope = DeserializeEnvelope(message.Text);
                    _Registry.MarkSeen(instanceId);

                    if (String.Equals(envelope.Type, "ping", StringComparison.OrdinalIgnoreCase))
                    {
                        await SendEnvelopeAsync(session, RemoteTunnelProtocol.CreatePong(envelope.CorrelationId), ctx.Token).ConfigureAwait(false);
                        continue;
                    }

                    if (String.Equals(envelope.Type, "response", StringComparison.OrdinalIgnoreCase))
                    {
                        _Registry.TryCompleteResponse(instanceId, envelope);
                        continue;
                    }

                    if (String.Equals(envelope.Type, "event", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(envelope.Type, "error", StringComparison.OrdinalIgnoreCase))
                    {
                        _Registry.RecordEvent(instanceId, envelope);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
            catch (JsonException ex)
            {
                if (session.IsConnected)
                {
                    try
                    {
                        await SendEnvelopeAsync(session, RemoteTunnelProtocol.CreateError(null, "invalid_json", ex.Message, 400), CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                if (!String.IsNullOrWhiteSpace(instanceId))
                {
                    _Registry.MarkDisconnected(instanceId);
                }

                if (session.IsConnected)
                {
                    try
                    {
                        await session.CloseAsync(WebSocketCloseStatus.NormalClosure, "Tunnel closed", CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private async Task<RemoteTunnelEnvelope> ReceiveEnvelopeAsync(WebSocketSession session, CancellationToken token)
        {
            WebSocketMessage message = await session.ReceiveAsync(token).ConfigureAwait(false);
            if (message.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidOperationException("Only text websocket messages are supported.");
            }

            return DeserializeEnvelope(message.Text);
        }

        private RemoteTunnelEnvelope DeserializeEnvelope(string? json)
        {
            return JsonSerializer.Deserialize<RemoteTunnelEnvelope>(json ?? String.Empty, RemoteTunnelProtocol.JsonOptions)
                ?? throw new JsonException("Tunnel envelope could not be deserialized.");
        }

        private async Task SendEnvelopeAsync(WebSocketSession session, RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            string json = JsonSerializer.Serialize(envelope, RemoteTunnelProtocol.JsonOptions);
            await session.SendTextAsync(json, token).ConfigureAwait(false);
        }

        private async Task ServeIndexAsync(HttpContextBase ctx)
        {
            string indexPath = Path.Combine(_WwwrootDirectory, "index.html");
            if (!File.Exists(indexPath))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.ContentType = "text/plain";
                await ctx.Response.Send("Armada.Proxy dashboard assets are missing.", ctx.Token).ConfigureAwait(false);
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/html";
            await ctx.Response.Send(await File.ReadAllTextAsync(indexPath, ctx.Token).ConfigureAwait(false), ctx.Token).ConfigureAwait(false);
        }

        private Task DefaultRouteAsync(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.ContentType = "application/json";
            return ctx.Response.Send("{\"error\":\"Not found\"}", ctx.Token);
        }

        private static object? DeserializePayload(JsonElement? payload)
        {
            if (!payload.HasValue)
            {
                return null;
            }

            return JsonSerializer.Deserialize<object>(payload.Value.GetRawText(), RemoteTunnelProtocol.JsonOptions);
        }

        private static string RequireParameter(ApiRequest req, string name)
        {
            string value = req.Parameters[name];
            if (String.IsNullOrWhiteSpace(value))
            {
                throw new WebserverException(ApiResultEnum.BadRequest, "Missing parameter: " + name);
            }

            return value.Trim();
        }

        private static string? GetOptionalValue(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static int ParsePositiveInt(string? rawValue, int defaultValue, int minimum, int maximum)
        {
            if (!Int32.TryParse(rawValue, out int parsed))
            {
                parsed = defaultValue;
            }

            if (parsed < minimum) parsed = minimum;
            if (parsed > maximum) parsed = maximum;
            return parsed;
        }

        private static JsonElement ReadJsonBody(ApiRequest req)
        {
            string body = req.Http.Request.DataAsString ?? String.Empty;
            if (String.IsNullOrWhiteSpace(body))
            {
                return JsonDocument.Parse("{}").RootElement.Clone();
            }

            return JsonDocument.Parse(body).RootElement.Clone();
        }

        private static string? GetOptionalProperty(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (String.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.ToString();
                }
            }

            return null;
        }

        private static List<string> GetCapabilities()
        {
            return new List<string>
            {
                "instances.summary",
                "instances.detail",
                "instances.shell.summary",
                "instances.fleets.list",
                "instances.fleet.detail",
                "instances.fleet.create",
                "instances.fleet.update",
                "instances.vessels.list",
                "instances.vessel.detail",
                "instances.vessel.create",
                "instances.vessel.update",
                "instances.activity",
                "instances.missions.list",
                "instances.missions.recent",
                "instances.voyages.list",
                "instances.voyages.recent",
                "instances.captains.recent",
                "instances.mission.detail",
                "instances.mission.log",
                "instances.mission.diff",
                "instances.mission.create",
                "instances.mission.update",
                "instances.mission.cancel",
                "instances.mission.restart",
                "instances.voyage.detail",
                "instances.voyage.dispatch",
                "instances.voyage.cancel",
                "instances.captain.detail",
                "instances.captain.log",
                "instances.captain.stop",
                "armada.status.snapshot",
                "armada.status.health"
            };
        }
    }
}
