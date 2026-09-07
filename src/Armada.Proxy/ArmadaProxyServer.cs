namespace Armada.Proxy
{
    using System.Collections.Concurrent;
    using System.Collections.Specialized;
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
    /// Watson-based remote proxy host for Armada dashboard relay.
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
        private readonly ProxyAuthService _Auth;
        private readonly ProxyRoutePolicyService _RoutePolicy;
        private readonly string _WwwrootDirectory;
        private readonly string _DashboardDirectory;
        private readonly DateTime _StartUtc = DateTime.UtcNow;
        private readonly ConcurrentDictionary<string, BrowserWebSocketRelay> _BrowserSockets = new ConcurrentDictionary<string, BrowserWebSocketRelay>(StringComparer.Ordinal);
        private const string DashboardApiRelayCapability = "dashboard.http.relay";
        private const string DashboardWebSocketRelayCapability = "dashboard.websocket.relay";

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
            _Auth = new ProxyAuthService(_Settings);
            _RoutePolicy = new ProxyRoutePolicyService();
            _WwwrootDirectory = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            _DashboardDirectory = Path.Combine(AppContext.BaseDirectory, "dashboard");
            _Registry.EventReceived += HandleInstanceEvent;
        }

        /// <summary>
        /// Start the proxy host.
        /// </summary>
        public Task StartAsync(CancellationToken token = default)
        {
            if (_Started)
            {
                return Task.CompletedTask;
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
            _Server.Start(token);
            _Started = true;

            if (!_Quiet)
            {
                _Logging.Info(_Header + "proxy started on " + _Server.Settings.Prefix);
            }

            return Task.CompletedTask;
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
            CloseAllBrowserSocketsAsync("Proxy stopping").GetAwaiter().GetResult();
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
                api.Info.Description = "Remote access portal and relay for the Armada dashboard.";
                api.Tags.Add(new OpenApiTag { Name = "ProxyAuth", Description = "Proxy-local browser authentication routes" });
                api.Tags.Add(new OpenApiTag { Name = "ProxySession", Description = "Proxy-local deployment selection routes" });
                api.Tags.Add(new OpenApiTag { Name = "ProxyStatus", Description = "Proxy health and tunnel visibility routes" });
            });

            RegisterApiRoutes(server);
            RegisterWebSocketRoutes(server);
            RegisterStaticContent(server);
        }

        private void RegisterStaticContent(Webserver server)
        {
            server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/", ServePortalIndexAsync);

            if (Directory.Exists(_WwwrootDirectory))
            {
                server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/app.css", ctx => ServePortalAssetAsync(ctx, "app.css", "text/css; charset=utf-8"));
                server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/app.js", ctx => ServePortalAssetAsync(ctx, "app.js", "application/javascript; charset=utf-8"));
                server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/img/logo-dark-grey.png", ctx => ServePortalAssetAsync(ctx, Path.Combine("img", "logo-dark-grey.png"), "image/png"));
                server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/img/logo-light-grey.png", ctx => ServePortalAssetAsync(ctx, Path.Combine("img", "logo-light-grey.png"), "image/png"));
                server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/img/logo.ico", ctx => ServePortalAssetAsync(ctx, Path.Combine("img", "logo.ico"), "image/x-icon"));
            }
            else
            {
                _Logging.Warn(_Header + "wwwroot directory not found at " + _WwwrootDirectory);
            }
        }

        private void RegisterApiRoutes(Webserver server)
        {
            MapJsonGet(server, "/proxy-api/v1/auth/challenge", (req) =>
            {
                ProxyAuthService.ProxyAuthChallenge challenge = _Auth.CreateChallenge();
                return new
                {
                    nonce = challenge.Nonce,
                    expiresUtc = challenge.ExpiresUtc
                };
            });

            MapJsonPost(server, "/proxy-api/v1/auth/login", (req) =>
            {
                JsonElement payload = ReadJsonBody(req);
                string? nonce = GetOptionalProperty(payload, "nonce");
                string? proofSha256 = GetOptionalProperty(payload, "proofSha256");

                if (!_Auth.TryLogin(nonce, proofSha256, out ProxyAuthService.ProxyBrowserSession? session, out string? error))
                {
                    req.Http.Response.StatusCode = 401;
                    return new { error = error ?? "Proxy authentication failed." };
                }

                req.Http.Response.Headers.Add("Set-Cookie", BuildSessionCookie(session!.Token, session.ExpiresUtc));
                _Logging.Debug(_Header + "browser login accepted");
                return new
                {
                    token = session.Token,
                    expiresUtc = session.ExpiresUtc,
                    selectedInstanceId = session.SelectedInstanceId
                };
            });

            MapJsonPost(server, "/proxy-api/v1/auth/logout", (req) =>
            {
                string? sessionToken = GetProxySessionToken(req.Http.Request.Headers);
                _Auth.Logout(sessionToken);
                req.Http.Response.Headers.Add("Set-Cookie", BuildClearedSessionCookie());
                return new { success = true };
            });

            MapJsonGet(server, "/proxy-api/v1/status/health", (req) => BuildHealthPayload());

            MapJsonGet(server, "/proxy-api/v1/instances", (req) =>
            {
                List<RemoteInstanceSummary> instances = _Registry.ListSummaries();
                return new
                {
                    count = instances.Count,
                    instances = instances.Select(BuildInstanceSummaryPayload).ToList()
                };
            });

            MapJsonGet(server, "/proxy-api/v1/session/context", (req) =>
            {
                string? sessionToken = GetProxySessionToken(req.Http.Request.Headers);
                if (!_Auth.TryGetSession(sessionToken, out ProxyAuthService.ProxyBrowserSession? session))
                {
                    req.Http.Response.StatusCode = 401;
                    return new { error = "Proxy authentication required. Sign in again." };
                }

                return BuildSessionContextPayload(session!);
            });

            MapJsonPost(server, "/proxy-api/v1/session/instance", (req) =>
            {
                string? sessionToken = GetProxySessionToken(req.Http.Request.Headers);
                JsonElement payload = ReadJsonBody(req);
                string? instanceId = GetOptionalProperty(payload, "instanceId");
                if (String.IsNullOrWhiteSpace(instanceId))
                {
                    req.Http.Response.StatusCode = 400;
                    return new { error = "instanceId is required." };
                }

                RemoteInstanceSummary? summary = _Registry.GetRecord(instanceId.Trim())?.ToSummary(DateTime.UtcNow, _Settings.StaleAfterSeconds);
                if (summary == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new { error = "Instance not found." };
                }

                if (!IsDashboardInstanceSelectable(summary, out string selectionError))
                {
                    req.Http.Response.StatusCode = 409;
                    return new { error = selectionError };
                }

                if (!_Auth.TrySetSelectedInstance(sessionToken, instanceId, out ProxyAuthService.ProxyBrowserSession? session, out string? error))
                {
                    req.Http.Response.StatusCode = 401;
                    return new { error = error ?? "Proxy authentication required. Sign in again." };
                }

                return BuildSessionContextPayload(session!);
            });

            MapJsonPost(server, "/proxy-api/v1/session/logout-instance", (req) =>
            {
                string? sessionToken = GetProxySessionToken(req.Http.Request.Headers);
                if (!_Auth.TrySetSelectedInstance(sessionToken, null, out ProxyAuthService.ProxyBrowserSession? session, out string? error))
                {
                    req.Http.Response.StatusCode = 401;
                    return new { error = error ?? "Proxy authentication required. Sign in again." };
                }

                return BuildSessionContextPayload(session!);
            });
        }

        private void RegisterWebSocketRoutes(Webserver server)
        {
            server.WebSocket("/tunnel", HandleTunnelAsync);
            server.WebSocket("/ws", HandleDashboardWebSocketAsync);
        }

        private void MapJsonGet(Webserver server, string path, Func<ApiRequest, object> handler)
        {
            server.Get(path, (req) => Task.FromResult(handler(req)));
        }

        private void MapJsonPost(Webserver server, string path, Func<ApiRequest, object> handler)
        {
            server.Post(path, (req) => Task.FromResult(handler(req)));
        }

        private async Task RelayArmadaHttpRequestAsync(HttpContextBase ctx, ProxyAuthService.ProxyBrowserSession browserSession, CancellationToken token)
        {
            string instanceId = browserSession.SelectedInstanceId!.Trim();
            byte[] requestBytes = ctx.Request.DataAsBytes ?? Array.Empty<byte>();
            if (requestBytes.Length > Constants.DefaultRemoteRelayMaxBodyBytes)
            {
                await SendJsonErrorAsync(
                    ctx,
                    413,
                    "Proxy relay currently supports request bodies up to " + Constants.DefaultRemoteRelayMaxBodyBytes + " bytes.").ConfigureAwait(false);
                return;
            }

            RemoteTunnelHttpRelayRequest relayRequest = new RemoteTunnelHttpRelayRequest
            {
                Method = ctx.Request.Method.ToString().ToUpperInvariant(),
                Path = NormalizePath(ctx.Request.Url.RawWithoutQuery),
                QueryString = GetRawQueryString(ctx.Request.Url.RawWithQuery),
                Headers = ExtractHeaders(ctx.Request.Headers),
                ContentType = ctx.Request.ContentType,
                BodyBase64 = requestBytes.Length > 0
                    ? Convert.ToBase64String(requestBytes)
                    : null
            };

            if (!_RoutePolicy.TryAuthorize(relayRequest, out int statusCode, out string? policyError))
            {
                await SendJsonErrorAsync(ctx, statusCode, policyError ?? "Proxy route policy denied this request.").ConfigureAwait(false);
                return;
            }

            string requesterIp = ResolveRequesterIp(ctx);

            RemoteTunnelEnvelope relayResponseEnvelope;
            try
            {
                relayResponseEnvelope = await _Registry.SendRequestAsync(
                    instanceId,
                    "armada.http.request",
                    relayRequest,
                    token,
                    requesterIp).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                await SendJsonErrorAsync(ctx, 504, ex.Message).ConfigureAwait(false);
                return;
            }
            catch (InvalidOperationException ex)
            {
                await SendJsonErrorAsync(ctx, 503, ex.Message).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "HTTP relay failed for " + instanceId + ": " + ex.Message);
                await SendJsonErrorAsync(ctx, 502, ex.Message).ConfigureAwait(false);
                return;
            }

            RemoteTunnelHttpRelayResponse? relayResponse = relayResponseEnvelope.Payload?.Deserialize<RemoteTunnelHttpRelayResponse>(RemoteTunnelProtocol.JsonOptions);
            if (relayResponse == null)
            {
                await SendJsonErrorAsync(ctx, relayResponseEnvelope.StatusCode ?? 502, relayResponseEnvelope.Message ?? "Relay response payload is missing.").ConfigureAwait(false);
                return;
            }

            ctx.Response.StatusCode = relayResponse.StatusCode;
            if (!String.IsNullOrWhiteSpace(relayResponse.ContentType))
            {
                ctx.Response.ContentType = relayResponse.ContentType;
            }

            foreach (KeyValuePair<string, string> header in relayResponse.Headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            {
                if (String.IsNullOrWhiteSpace(header.Key) ||
                    String.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ctx.Response.Headers.Add(header.Key, header.Value);
            }

            byte[] responseBytes = DecodeBase64(relayResponse.BodyBase64);
            await ctx.Response.Send(responseBytes, ctx.Token).ConfigureAwait(false);
        }

        private async Task HandleDashboardWebSocketAsync(HttpContextBase ctx, WebSocketSession session)
        {
            if (!TryGetBrowserSession(ctx, out ProxyAuthService.ProxyBrowserSession? browserSession))
            {
                await CloseBrowserSessionAsync(session, WebSocketCloseStatus.PolicyViolation, "Proxy authentication required.").ConfigureAwait(false);
                return;
            }

            if (!HasSelectedInstance(browserSession))
            {
                await CloseBrowserSessionAsync(session, WebSocketCloseStatus.PolicyViolation, "Select a deployment before opening the dashboard.").ConfigureAwait(false);
                return;
            }

            if (!TryGetSelectedInstanceSummary(browserSession!, out RemoteInstanceSummary? summary, out string? selectionError))
            {
                await CloseBrowserSessionAsync(session, WebSocketCloseStatus.PolicyViolation, selectionError ?? "Choose a connected deployment again.").ConfigureAwait(false);
                return;
            }

            if (!SupportsDashboardWebSocketRelay(summary!))
            {
                await CloseBrowserSessionAsync(session, WebSocketCloseStatus.PolicyViolation, BuildUnsupportedDashboardWebSocketMessage(summary!.InstanceId)).ConfigureAwait(false);
                return;
            }

            string instanceId = browserSession!.SelectedInstanceId!.Trim();
            string proxySocketId = Guid.NewGuid().ToString("N");
            BrowserWebSocketRelay relay = new BrowserWebSocketRelay(proxySocketId, instanceId, session);
            _BrowserSockets[proxySocketId] = relay;

            try
            {
                RemoteTunnelEnvelope response = await _Registry.SendRequestAsync(
                    instanceId,
                    "armada.ws.open",
                    new RemoteTunnelWebSocketOpenRequest
                    {
                        ProxySocketId = proxySocketId,
                        Path = "/ws"
                    },
                    ctx.Token,
                    ResolveRequesterIp(ctx)).ConfigureAwait(false);

                if ((response.StatusCode ?? 500) < 200 || (response.StatusCode ?? 500) >= 300)
                {
                    await CloseBrowserRelayAsync(relay, WebSocketCloseStatus.InternalServerError, response.Message ?? "Unable to open remote dashboard websocket.").ConfigureAwait(false);
                    return;
                }

                await foreach (WebSocketMessage message in session.ReadMessagesAsync(ctx.Token).ConfigureAwait(false))
                {
                    if (message.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    response = await _Registry.SendRequestAsync(
                        instanceId,
                        "armada.ws.message",
                        new RemoteTunnelWebSocketMessage
                        {
                            ProxySocketId = proxySocketId,
                            Data = message.Text ?? String.Empty
                        },
                        ctx.Token,
                        ResolveRequesterIp(ctx)).ConfigureAwait(false);

                    if ((response.StatusCode ?? 500) < 200 || (response.StatusCode ?? 500) >= 300)
                    {
                        await CloseBrowserRelayAsync(relay, WebSocketCloseStatus.InternalServerError, response.Message ?? "Remote websocket relay send failed.").ConfigureAwait(false);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
            catch (TimeoutException ex)
            {
                await CloseBrowserRelayAsync(relay, WebSocketCloseStatus.InternalServerError, ex.Message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "dashboard websocket relay error for " + proxySocketId + ": " + ex.Message);
                await CloseBrowserRelayAsync(relay, WebSocketCloseStatus.InternalServerError, ex.Message).ConfigureAwait(false);
            }
            finally
            {
                _BrowserSockets.TryRemove(proxySocketId, out BrowserWebSocketRelay? _);

                try
                {
                    await _Registry.SendRequestAsync(
                        instanceId,
                        "armada.ws.close",
                        new RemoteTunnelWebSocketCloseRequest
                        {
                            ProxySocketId = proxySocketId,
                            Code = (int)WebSocketCloseStatus.NormalClosure,
                            Reason = "Browser websocket closed"
                        },
                        CancellationToken.None,
                        ResolveRequesterIp(ctx)).ConfigureAwait(false);
                }
                catch
                {
                }

                await CloseBrowserSessionAsync(session, WebSocketCloseStatus.NormalClosure, "Dashboard websocket closed.").ConfigureAwait(false);
            }
        }

        private void HandleInstanceEvent(string instanceId, RemoteTunnelEnvelope envelope)
        {
            _ = HandleInstanceEventAsync(instanceId, envelope);
        }

        private async Task HandleInstanceEventAsync(string instanceId, RemoteTunnelEnvelope envelope)
        {
            string method = envelope.Method?.Trim().ToLowerInvariant() ?? String.Empty;
            switch (method)
            {
                case "armada.ws.message":
                    RemoteTunnelWebSocketMessage? message = envelope.Payload?.Deserialize<RemoteTunnelWebSocketMessage>(RemoteTunnelProtocol.JsonOptions);
                    if (message == null || String.IsNullOrWhiteSpace(message.ProxySocketId))
                    {
                        return;
                    }

                    if (!_BrowserSockets.TryGetValue(message.ProxySocketId, out BrowserWebSocketRelay? relay) ||
                        !String.Equals(relay.InstanceId, instanceId, StringComparison.Ordinal))
                    {
                        return;
                    }

                    await relay.SendLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (relay.Session.IsConnected)
                        {
                            await relay.Session.SendTextAsync(message.Data ?? String.Empty, CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        relay.SendLock.Release();
                    }
                    return;
                case "armada.ws.closed":
                    RemoteTunnelWebSocketCloseRequest? closePayload = envelope.Payload?.Deserialize<RemoteTunnelWebSocketCloseRequest>(RemoteTunnelProtocol.JsonOptions);
                    if (closePayload != null &&
                        !String.IsNullOrWhiteSpace(closePayload.ProxySocketId) &&
                        _BrowserSockets.TryGetValue(closePayload.ProxySocketId, out BrowserWebSocketRelay? closingRelay))
                    {
                        await CloseBrowserRelayAsync(
                            closingRelay,
                            MapCloseStatus(closePayload.Code),
                            closePayload.Reason ?? "Remote dashboard websocket closed.").ConfigureAwait(false);
                    }
                    return;
                case "armada.ws.error":
                    RemoteTunnelWebSocketCloseRequest? errorPayload = envelope.Payload?.Deserialize<RemoteTunnelWebSocketCloseRequest>(RemoteTunnelProtocol.JsonOptions);
                    if (errorPayload != null &&
                        !String.IsNullOrWhiteSpace(errorPayload.ProxySocketId) &&
                        _BrowserSockets.TryGetValue(errorPayload.ProxySocketId, out BrowserWebSocketRelay? errorRelay))
                    {
                        await CloseBrowserRelayAsync(
                            errorRelay,
                            WebSocketCloseStatus.InternalServerError,
                            errorPayload.Reason ?? "Remote dashboard websocket relay failed.").ConfigureAwait(false);
                    }
                    return;
            }
        }

        private Task HandleTunnelAsync(HttpContextBase ctx, WebSocketSession session)
        {
            return HandleTunnelInternalAsync(ctx, session);
        }

        private async Task HandleTunnelInternalAsync(HttpContextBase ctx, WebSocketSession session)
        {
            string? instanceId = null;
            string remoteAddress = String.IsNullOrWhiteSpace(session.RemoteIp) ? "unknown" : session.RemoteIp + ":" + session.RemotePort;

            try
            {
                using CancellationTokenSource handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(ctx.Token);
                handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(_Settings.HandshakeTimeoutSeconds));
                RemoteTunnelEnvelope firstEnvelope = await ReceiveEnvelopeAsync(session, handshakeTimeout.Token).ConfigureAwait(false);

                if (!String.Equals(firstEnvelope.Type, "request", StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(firstEnvelope.Method, "armada.tunnel.handshake", StringComparison.OrdinalIgnoreCase))
                {
                    _Logging.Warn(_Header + "invalid handshake from " + remoteAddress + ": first message was " + (firstEnvelope.Method ?? firstEnvelope.Type ?? "unknown"));
                    await SendEnvelopeAsync(session, RemoteTunnelProtocol.CreateError(firstEnvelope.CorrelationId, "invalid_handshake", "First tunnel message must be armada.tunnel.handshake.", 400), ctx.Token).ConfigureAwait(false);
                    await session.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Handshake required", ctx.Token).ConfigureAwait(false);
                    return;
                }

                RemoteTunnelHandshakePayload? handshake = firstEnvelope.Payload?.Deserialize<RemoteTunnelHandshakePayload>(RemoteTunnelProtocol.JsonOptions);
                if (!_Registry.TryValidateHandshake(handshake, out string? handshakeError))
                {
                    _Logging.Warn(_Header + "handshake rejected from " + remoteAddress + ": " + (handshakeError ?? "unknown reason"));
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
                _Registry.RegisterHandshake(handshake, remoteAddress, proxySession);
                _Logging.Info(_Header + "handshake accepted for " + instanceId + " from " + remoteAddress);

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
                    await CloseBrowserSocketsForInstanceAsync(instanceId, "Remote deployment disconnected from the proxy.").ConfigureAwait(false);
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

        private async Task CloseBrowserRelayAsync(BrowserWebSocketRelay relay, WebSocketCloseStatus status, string reason)
        {
            _BrowserSockets.TryRemove(relay.ProxySocketId, out BrowserWebSocketRelay? _);
            await CloseBrowserSessionAsync(relay.Session, status, reason).ConfigureAwait(false);
        }

        private static async Task CloseBrowserSessionAsync(WebSocketSession session, WebSocketCloseStatus status, string reason)
        {
            if (!session.IsConnected)
            {
                return;
            }

            try
            {
                await session.CloseAsync(status, reason, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private async Task CloseBrowserSocketsForInstanceAsync(string instanceId, string reason)
        {
            BrowserWebSocketRelay[] relays = _BrowserSockets.Values
                .Where(relay => String.Equals(relay.InstanceId, instanceId, StringComparison.Ordinal))
                .ToArray();

            foreach (BrowserWebSocketRelay relay in relays)
            {
                await CloseBrowserRelayAsync(relay, WebSocketCloseStatus.EndpointUnavailable, reason).ConfigureAwait(false);
            }
        }

        private async Task CloseAllBrowserSocketsAsync(string reason)
        {
            foreach (BrowserWebSocketRelay relay in _BrowserSockets.Values.ToArray())
            {
                await CloseBrowserRelayAsync(relay, WebSocketCloseStatus.NormalClosure, reason).ConfigureAwait(false);
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

        private async Task ServePortalIndexAsync(HttpContextBase ctx)
        {
            await ServePortalAssetAsync(ctx, "index.html", "text/html; charset=utf-8").ConfigureAwait(false);
        }

        private async Task ServePortalAssetAsync(HttpContextBase ctx, string relativePath, string contentType)
        {
            string fullPath = Path.Combine(_WwwrootDirectory, relativePath);
            if (!File.Exists(fullPath))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                await ctx.Response.Send("Armada.Proxy portal asset is missing.", ctx.Token).ConfigureAwait(false);
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = contentType;
            byte[] bytes = await File.ReadAllBytesAsync(fullPath, ctx.Token).ConfigureAwait(false);
            await ctx.Response.Send(bytes, ctx.Token).ConfigureAwait(false);
        }

        private async Task DefaultRouteAsync(HttpContextBase ctx)
        {
            string path = NormalizePath(ctx.Request.Url.RawWithoutQuery);
            if (IsRelayedApiPath(path))
            {
                if (!TryGetBrowserSession(ctx, out ProxyAuthService.ProxyBrowserSession? browserSession))
                {
                    await HandleMissingProxySessionAsync(ctx, path).ConfigureAwait(false);
                    return;
                }

                if (!HasSelectedInstance(browserSession))
                {
                    await HandleMissingSelectedInstanceAsync(ctx, path).ConfigureAwait(false);
                    return;
                }

                if (!TryGetSelectedInstanceSummary(browserSession!, out RemoteInstanceSummary? summary, out string? selectionError))
                {
                    await SendJsonErrorAsync(ctx, 409, selectionError ?? "Choose a connected deployment again.").ConfigureAwait(false);
                    return;
                }

                if (!SupportsDashboardApiRelay(summary!))
                {
                    await SendJsonErrorAsync(ctx, 409, BuildUnsupportedDashboardAccessMessage(summary!.InstanceId)).ConfigureAwait(false);
                    return;
                }

                await RelayArmadaHttpRequestAsync(ctx, browserSession!, ctx.Token).ConfigureAwait(false);
                return;
            }

            if (String.Equals(path, "/dashboard", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/dashboard/", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetBrowserSession(ctx, out ProxyAuthService.ProxyBrowserSession? browserSession))
                {
                    await HandleMissingProxySessionAsync(ctx, path).ConfigureAwait(false);
                    return;
                }

                if (!HasSelectedInstance(browserSession))
                {
                    await HandleMissingSelectedInstanceAsync(ctx, path).ConfigureAwait(false);
                    return;
                }

                if (!TryGetSelectedInstanceSummary(browserSession!, out RemoteInstanceSummary? summary, out string? selectionError))
                {
                    await RedirectToPortalWithErrorAsync(ctx, selectionError ?? "Choose a connected deployment again.").ConfigureAwait(false);
                    return;
                }

                if (!SupportsDashboardApiRelay(summary!))
                {
                    await RedirectToPortalWithErrorAsync(ctx, BuildUnsupportedDashboardAccessMessage(summary!.InstanceId)).ConfigureAwait(false);
                    return;
                }

                await ServeDashboardAssetAsync(ctx, path).ConfigureAwait(false);
                return;
            }

            ctx.Response.StatusCode = 404;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send("{\"error\":\"Not found\"}", ctx.Token).ConfigureAwait(false);
        }

        private async Task ServeDashboardAssetAsync(HttpContextBase ctx, string requestPath)
        {
            if (!Directory.Exists(_DashboardDirectory))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                await ctx.Response.Send("Armada dashboard assets are missing from the proxy build.", ctx.Token).ConfigureAwait(false);
                return;
            }

            string relativePath = requestPath.Equals("/dashboard", StringComparison.OrdinalIgnoreCase)
                ? "index.html"
                : requestPath.Substring("/dashboard/".Length);
            relativePath = Uri.UnescapeDataString(relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (String.IsNullOrWhiteSpace(relativePath))
            {
                relativePath = "index.html";
            }

            string? fullPath = TryResolveStaticPath(_DashboardDirectory, relativePath);
            if (fullPath != null && File.Exists(fullPath))
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = GetContentType(fullPath);
                byte[] bytes = await File.ReadAllBytesAsync(fullPath, ctx.Token).ConfigureAwait(false);
                await ctx.Response.Send(bytes, ctx.Token).ConfigureAwait(false);
                return;
            }

            string spaIndexPath = Path.Combine(_DashboardDirectory, "index.html");
            if (!File.Exists(spaIndexPath))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                await ctx.Response.Send("Armada dashboard index is missing from the proxy build.", ctx.Token).ConfigureAwait(false);
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            byte[] indexBytes = await File.ReadAllBytesAsync(spaIndexPath, ctx.Token).ConfigureAwait(false);
            await ctx.Response.Send(indexBytes, ctx.Token).ConfigureAwait(false);
        }

        private static string? TryResolveStaticPath(string rootDirectory, string relativePath)
        {
            string fullPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
            string normalizedRoot = Path.GetFullPath(rootDirectory);
            if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return fullPath;
        }

        private static string GetContentType(string fullPath)
        {
            string extension = Path.GetExtension(fullPath).ToLowerInvariant();
            return extension switch
            {
                ".css" => "text/css; charset=utf-8",
                ".html" => "text/html; charset=utf-8",
                ".ico" => "image/x-icon",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".js" => "application/javascript; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                ".map" => "application/json; charset=utf-8",
                ".png" => "image/png",
                ".svg" => "image/svg+xml",
                ".txt" => "text/plain; charset=utf-8",
                ".webp" => "image/webp",
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                _ => "application/octet-stream"
            };
        }

        private object BuildHealthPayload()
        {
            List<RemoteInstanceSummary> instances = _Registry.ListSummaries();
            int connectedCount = instances.Count(summary => String.Equals(summary.State, "connected", StringComparison.OrdinalIgnoreCase));
            int staleCount = instances.Count(summary => String.Equals(summary.State, "stale", StringComparison.OrdinalIgnoreCase));

            return new
            {
                product = "Armada.Proxy",
                version = Constants.ProductVersion,
                status = "ok",
                startUtc = _StartUtc,
                uptimeSeconds = (long)Math.Max(0, (DateTime.UtcNow - _StartUtc).TotalSeconds),
                connectedInstances = connectedCount,
                staleInstances = staleCount,
                instanceCount = instances.Count
            };
        }

        private object BuildSessionContextPayload(ProxyAuthService.ProxyBrowserSession session)
        {
            object? selectedInstance = null;
            bool apiRelay = false;
            bool websocketRelay = false;
            if (!String.IsNullOrWhiteSpace(session.SelectedInstanceId))
            {
                RemoteInstanceSummary? instanceSummary = _Registry.GetRecord(session.SelectedInstanceId.Trim())?.ToSummary(DateTime.UtcNow, _Settings.StaleAfterSeconds);
                if (instanceSummary != null)
                {
                    selectedInstance = BuildInstanceSummaryPayload(instanceSummary);
                    apiRelay = SupportsDashboardApiRelay(instanceSummary);
                    websocketRelay = SupportsDashboardWebSocketRelay(instanceSummary);
                }
            }

            return new
            {
                isAuthenticated = true,
                expiresUtc = session.ExpiresUtc,
                selectedInstanceId = session.SelectedInstanceId,
                selectedInstance = selectedInstance,
                relay = new
                {
                    dashboard = apiRelay,
                    api = apiRelay,
                    websocket = websocketRelay
                }
            };
        }

        private static object BuildInstanceSummaryPayload(RemoteInstanceSummary summary)
        {
            return new
            {
                instanceId = summary.InstanceId,
                state = summary.State,
                armadaVersion = summary.ArmadaVersion,
                protocolVersion = summary.ProtocolVersion,
                capabilities = summary.Capabilities,
                remoteAddress = summary.RemoteAddress,
                firstSeenUtc = summary.FirstSeenUtc,
                connectedUtc = summary.ConnectedUtc,
                lastSeenUtc = summary.LastSeenUtc,
                lastEventUtc = summary.LastEventUtc,
                lastDisconnectUtc = summary.LastDisconnectUtc,
                lastError = summary.LastError,
                recentEventCount = summary.RecentEventCount,
                pendingRequestCount = summary.PendingRequestCount
            };
        }

        private static Dictionary<string, string> ExtractHeaders(NameValueCollection headers)
        {
            Dictionary<string, string> results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? key in headers.AllKeys)
            {
                if (String.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                string? value = headers.Get(key);
                if (!String.IsNullOrWhiteSpace(value))
                {
                    results[key] = value;
                }
            }

            return results;
        }

        private async Task RedirectToPortalWithErrorAsync(HttpContextBase ctx, string message)
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.Headers.Add("Location", "/?error=" + Uri.EscapeDataString(message));
            await ctx.Response.Send().ConfigureAwait(false);
        }

        private bool TryGetSelectedInstanceSummary(ProxyAuthService.ProxyBrowserSession session, out RemoteInstanceSummary? summary, out string? error)
        {
            summary = null;
            error = null;

            string? selectedInstanceId = session.SelectedInstanceId?.Trim();
            if (String.IsNullOrWhiteSpace(selectedInstanceId))
            {
                error = "Select a connected deployment before opening the dashboard.";
                return false;
            }

            summary = _Registry.GetRecord(selectedInstanceId)?.ToSummary(DateTime.UtcNow, _Settings.StaleAfterSeconds);
            if (summary == null)
            {
                error = "The selected deployment is no longer available. Choose a connected deployment again.";
                return false;
            }

            if (!IsInstanceConnectable(summary))
            {
                error = "The selected deployment is no longer connected. Choose a connected deployment again.";
                return false;
            }

            return true;
        }

        private static bool IsDashboardInstanceSelectable(RemoteInstanceSummary summary, out string error)
        {
            if (!IsInstanceConnectable(summary))
            {
                error = "Select a connected deployment before opening the dashboard.";
                return false;
            }

            if (!SupportsDashboardApiRelay(summary))
            {
                error = BuildUnsupportedDashboardAccessMessage(summary.InstanceId);
                return false;
            }

            error = String.Empty;
            return true;
        }

        private static bool IsInstanceConnectable(RemoteInstanceSummary summary)
        {
            string state = summary.State?.Trim() ?? String.Empty;
            return String.Equals(state, "connected", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(state, "stale", StringComparison.OrdinalIgnoreCase);
        }

        private static bool SupportsDashboardApiRelay(RemoteInstanceSummary summary)
        {
            return HasCapability(summary, DashboardApiRelayCapability);
        }

        private static bool SupportsDashboardWebSocketRelay(RemoteInstanceSummary summary)
        {
            return HasCapability(summary, DashboardWebSocketRelayCapability);
        }

        private static bool HasCapability(RemoteInstanceSummary summary, string capability)
        {
            return summary.Capabilities.Any(candidate => String.Equals(candidate, capability, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildUnsupportedDashboardAccessMessage(string instanceId)
        {
            return instanceId + " needs an Armada update before it can open through this proxy dashboard.";
        }

        private static string BuildUnsupportedDashboardWebSocketMessage(string instanceId)
        {
            return instanceId + " does not support live dashboard websocket relay yet. Update Armada on that deployment and reconnect it.";
        }

        private static string BuildSessionCookie(string token, DateTime expiresUtc)
        {
            int maxAgeSeconds = (int)Math.Max(1, Math.Ceiling((expiresUtc - DateTime.UtcNow).TotalSeconds));
            return Constants.ProxySessionCookieName + "=" + token + "; Path=/; HttpOnly; SameSite=Lax; Max-Age=" + maxAgeSeconds;
        }

        private static string BuildClearedSessionCookie()
        {
            return Constants.ProxySessionCookieName + "=; Path=/; HttpOnly; SameSite=Lax; Max-Age=0";
        }

        private bool TryGetBrowserSession(HttpContextBase ctx, out ProxyAuthService.ProxyBrowserSession? session)
        {
            string? sessionToken = GetProxySessionToken(ctx.Request.Headers);
            return _Auth.TryGetSession(sessionToken, out session);
        }

        private static string? GetProxySessionToken(NameValueCollection headers)
        {
            string? headerToken = headers.Get(Constants.ProxySessionTokenHeader);
            if (!String.IsNullOrWhiteSpace(headerToken))
            {
                return headerToken.Trim();
            }

            string? cookieHeader = headers.Get("Cookie");
            if (String.IsNullOrWhiteSpace(cookieHeader))
            {
                return null;
            }

            foreach (string part in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!part.StartsWith(Constants.ProxySessionCookieName + "=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string candidate = part.Substring(Constants.ProxySessionCookieName.Length + 1).Trim();
                return String.IsNullOrWhiteSpace(candidate) ? null : candidate;
            }

            return null;
        }

        private static bool HasSelectedInstance(ProxyAuthService.ProxyBrowserSession? session)
        {
            return session != null && !String.IsNullOrWhiteSpace(session.SelectedInstanceId);
        }

        private static bool IsRelayedApiPath(string? path)
        {
            return !String.IsNullOrWhiteSpace(path) &&
                path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase);
        }

        private async Task HandleMissingProxySessionAsync(HttpContextBase ctx, string path)
        {
            if (path.StartsWith("/dashboard", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 302;
                ctx.Response.Headers.Add("Location", "/");
                await ctx.Response.Send().ConfigureAwait(false);
                return;
            }

            await SendJsonErrorAsync(ctx, 401, "Proxy authentication required. Sign in again.").ConfigureAwait(false);
        }

        private async Task HandleMissingSelectedInstanceAsync(HttpContextBase ctx, string path)
        {
            if (path.StartsWith("/dashboard", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 302;
                ctx.Response.Headers.Add("Location", "/");
                await ctx.Response.Send().ConfigureAwait(false);
                return;
            }

            await SendJsonErrorAsync(ctx, 409, "Select a connected deployment before opening the dashboard.").ConfigureAwait(false);
        }

        private static Task SendJsonErrorAsync(HttpContextBase ctx, int statusCode, string message)
        {
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "application/json";
            string json = JsonSerializer.Serialize(new { error = message }, RemoteTunnelProtocol.JsonOptions);
            return ctx.Response.Send(json, ctx.Token);
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

        private static string NormalizePath(string? path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                return "/";
            }

            string normalized = path.Trim();
            return normalized.StartsWith("/", StringComparison.Ordinal) ? normalized : "/" + normalized;
        }

        private static string? GetRawQueryString(string? rawWithQuery)
        {
            if (String.IsNullOrWhiteSpace(rawWithQuery))
            {
                return null;
            }

            int queryIndex = rawWithQuery.IndexOf('?');
            if (queryIndex < 0 || queryIndex >= rawWithQuery.Length - 1)
            {
                return null;
            }

            return rawWithQuery.Substring(queryIndex + 1);
        }

        private static byte[] DecodeBase64(string? value)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<byte>();
            }

            try
            {
                return Convert.FromBase64String(value);
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        private string ResolveRequesterIp(HttpContextBase ctx)
        {
            return NormalizeForwardedValue(
                ExtractForwardedIp(ctx.Request.Headers.Get("X-Forwarded-For")) ??
                ExtractForwardedHeaderIp(ctx.Request.Headers.Get("Forwarded")) ??
                ctx.Request.Source?.IpAddress?.ToString()) ?? "unknown";
        }

        private static string? ExtractForwardedIp(string? rawValue)
        {
            if (String.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            string firstValue = rawValue.Split(',')[0].Trim();
            return NormalizeForwardedValue(firstValue);
        }

        private static string? ExtractForwardedHeaderIp(string? rawValue)
        {
            if (String.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            string firstForwarded = rawValue.Split(',')[0];
            foreach (string part in firstForwarded.Split(';'))
            {
                string candidate = part.Trim();
                if (!candidate.StartsWith("for=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return NormalizeForwardedValue(candidate.Substring(4));
            }

            return null;
        }

        private static string? NormalizeForwardedValue(string? value)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string candidate = value.Trim().Trim('"');
            if (String.IsNullOrWhiteSpace(candidate) || String.Equals(candidate, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (candidate.StartsWith("[", StringComparison.Ordinal) && candidate.Contains(']'))
            {
                int endBracket = candidate.IndexOf(']');
                if (endBracket > 1)
                {
                    return candidate.Substring(1, endBracket - 1);
                }
            }

            if (candidate.Count(ch => ch == ':') == 1 && candidate.Contains('.'))
            {
                int lastColon = candidate.LastIndexOf(':');
                if (lastColon > 0)
                {
                    return candidate.Substring(0, lastColon);
                }
            }

            return candidate;
        }

        private static WebSocketCloseStatus MapCloseStatus(int? code)
        {
            if (code.HasValue && Enum.IsDefined(typeof(WebSocketCloseStatus), code.Value))
            {
                return (WebSocketCloseStatus)code.Value;
            }

            return WebSocketCloseStatus.NormalClosure;
        }

        private static List<string> GetCapabilities()
        {
            return new List<string>
            {
                "proxy.portal",
                "dashboard.static",
                "dashboard.http.relay",
                "dashboard.websocket.relay",
                "instances.summary",
                "instances.selection",
                "tunnel.handshake",
                "tunnel.ping"
            };
        }

        private sealed class BrowserWebSocketRelay
        {
            public BrowserWebSocketRelay(string proxySocketId, string instanceId, WebSocketSession session)
            {
                ProxySocketId = proxySocketId;
                InstanceId = instanceId;
                Session = session;
            }

            public string ProxySocketId { get; }

            public string InstanceId { get; }

            public WebSocketSession Session { get; }

            public SemaphoreSlim SendLock { get; } = new SemaphoreSlim(1, 1);
        }
    }
}
