namespace Armada.Server
{
    using System.Collections.Concurrent;
    using System.Net;
    using System.Net.Http;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Handles generic dashboard REST and websocket relay requests over the Armada remote tunnel.
    /// </summary>
    public sealed class RemoteDashboardRelayService : IAsyncDisposable
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RemoteDashboardRelayService(
            LoggingModule logging,
            ArmadaSettings settings,
            Func<string, object?, CancellationToken, Task> publishEventAsync)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _PublishEventAsync = publishEventAsync ?? throw new ArgumentNullException(nameof(publishEventAsync));

            HttpClientHandler handler = new HttpClientHandler();
            if (_Settings.Rest.Ssl)
            {
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            _HttpClient = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Handle a generic dashboard relay tunnel request.
        /// </summary>
        public async Task<RemoteTunnelRequestResult> HandleAsync(RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            string method = envelope.Method?.Trim().ToLowerInvariant() ?? String.Empty;

            switch (method)
            {
                case "armada.http.request":
                    return await HandleHttpRelayAsync(
                        envelope.Payload?.Deserialize<RemoteTunnelHttpRelayRequest>(RemoteTunnelProtocol.JsonOptions),
                        envelope.RequesterIp,
                        token).ConfigureAwait(false);
                case "armada.ws.open":
                    return await HandleWebSocketOpenAsync(
                        envelope.Payload?.Deserialize<RemoteTunnelWebSocketOpenRequest>(RemoteTunnelProtocol.JsonOptions),
                        token).ConfigureAwait(false);
                case "armada.ws.message":
                    return await HandleWebSocketMessageAsync(
                        envelope.Payload?.Deserialize<RemoteTunnelWebSocketMessage>(RemoteTunnelProtocol.JsonOptions),
                        token).ConfigureAwait(false);
                case "armada.ws.close":
                    return await HandleWebSocketCloseAsync(
                        envelope.Payload?.Deserialize<RemoteTunnelWebSocketCloseRequest>(RemoteTunnelProtocol.JsonOptions),
                        token).ConfigureAwait(false);
                default:
                    return new RemoteTunnelRequestResult
                    {
                        StatusCode = 404,
                        ErrorCode = "unsupported_method",
                        Message = "Relay method " + envelope.Method + " is not supported."
                    };
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            foreach (KeyValuePair<string, RelayWebSocketSession> entry in _WebSocketSessions.ToArray())
            {
                await CloseRelaySocketAsync(entry.Key, entry.Value, WebSocketCloseStatus.NormalClosure, "Relay shutting down").ConfigureAwait(false);
            }

            _HttpClient.Dispose();
        }

        #endregion

        #region Private-Members

        private readonly string _Header = "[RemoteDashboardRelay] ";
        private readonly LoggingModule _Logging;
        private readonly ArmadaSettings _Settings;
        private readonly Func<string, object?, CancellationToken, Task> _PublishEventAsync;
        private readonly HttpClient _HttpClient;
        private readonly ConcurrentDictionary<string, RelayWebSocketSession> _WebSocketSessions = new ConcurrentDictionary<string, RelayWebSocketSession>(StringComparer.Ordinal);

        private static readonly HashSet<string> _BlockedRequestHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Connection",
            "Content-Length",
            "Cookie",
            "Host",
            "Proxy-Connection",
            "Sec-WebSocket-Accept",
            "Sec-WebSocket-Extensions",
            "Sec-WebSocket-Key",
            "Sec-WebSocket-Protocol",
            "Sec-WebSocket-Version",
            "Transfer-Encoding",
            Constants.ProxySessionTokenHeader
        };

        private static readonly HashSet<string> _BlockedResponseHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Connection",
            "Content-Length",
            "Keep-Alive",
            "Proxy-Authenticate",
            "Proxy-Authorization",
            "TE",
            "Trailer",
            "Transfer-Encoding",
            "Upgrade"
        };

        #endregion

        #region Private-Methods

        private async Task<RemoteTunnelRequestResult> HandleHttpRelayAsync(RemoteTunnelHttpRelayRequest? request, string? requesterIp, CancellationToken token)
        {
            if (request == null)
            {
                return new RemoteTunnelRequestResult
                {
                    StatusCode = 400,
                    ErrorCode = "invalid_request",
                    Message = "HTTP relay payload is required."
                };
            }

            string path = NormalizePath(request.Path);
            if (!path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase))
            {
                return new RemoteTunnelRequestResult
                {
                    StatusCode = 400,
                    ErrorCode = "invalid_path",
                    Message = "HTTP relay path must target /api/v1/*."
                };
            }

            Uri requestUri = BuildLocalHttpUri(path, request.QueryString);
            using HttpRequestMessage relayRequest = new HttpRequestMessage(new HttpMethod((request.Method ?? "GET").Trim().ToUpperInvariant()), requestUri);

            foreach (KeyValuePair<string, string> header in request.Headers ?? new Dictionary<string, string>())
            {
                if (String.IsNullOrWhiteSpace(header.Key) || _BlockedRequestHeaders.Contains(header.Key))
                {
                    continue;
                }

                if (!relayRequest.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    relayRequest.Content ??= new ByteArrayContent(Array.Empty<byte>());
                    relayRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            if (!String.IsNullOrWhiteSpace(requesterIp))
            {
                relayRequest.Headers.TryAddWithoutValidation("X-Forwarded-For", requesterIp);
            }

            byte[] requestBody = DecodeBody(request.BodyBase64);
            if (requestBody.Length > Constants.DefaultRemoteRelayMaxBodyBytes)
            {
                return new RemoteTunnelRequestResult
                {
                    StatusCode = 413,
                    ErrorCode = "relay_body_too_large",
                    Message = "Dashboard relay request bodies are limited to " + Constants.DefaultRemoteRelayMaxBodyBytes + " bytes."
                };
            }

            if (requestBody.Length > 0)
            {
                relayRequest.Content = new ByteArrayContent(requestBody);
            }
            else if (RequiresRequestBody(relayRequest.Method.Method) && !String.IsNullOrWhiteSpace(request.ContentType))
            {
                relayRequest.Content = new ByteArrayContent(Array.Empty<byte>());
            }

            if (relayRequest.Content != null && !String.IsNullOrWhiteSpace(request.ContentType))
            {
                relayRequest.Content.Headers.TryAddWithoutValidation("Content-Type", request.ContentType);
            }

            try
            {
                using HttpResponseMessage response = await _HttpClient.SendAsync(
                    relayRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    token).ConfigureAwait(false);

                byte[] responseBody = response.Content == null
                    ? Array.Empty<byte>()
                    : await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                if (responseBody.Length > Constants.DefaultRemoteRelayMaxBodyBytes)
                {
                    return new RemoteTunnelRequestResult
                    {
                        StatusCode = 413,
                        ErrorCode = "relay_body_too_large",
                        Message = "Dashboard relay response bodies are limited to " + Constants.DefaultRemoteRelayMaxBodyBytes + " bytes."
                    };
                }

                RemoteTunnelHttpRelayResponse payload = new RemoteTunnelHttpRelayResponse
                {
                    StatusCode = (int)response.StatusCode,
                    ReasonPhrase = response.ReasonPhrase,
                    ContentType = response.Content?.Headers?.ContentType?.ToString(),
                    BodyBase64 = responseBody.Length > 0 ? Convert.ToBase64String(responseBody) : null
                };

                foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
                {
                    if (_BlockedResponseHeaders.Contains(header.Key))
                    {
                        continue;
                    }

                    payload.Headers[header.Key] = String.Join(", ", header.Value);
                }

                if (response.Content != null)
                {
                    foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
                    {
                        if (_BlockedResponseHeaders.Contains(header.Key))
                        {
                            continue;
                        }

                        payload.Headers[header.Key] = String.Join(", ", header.Value);
                    }
                }

                return new RemoteTunnelRequestResult
                {
                    StatusCode = payload.StatusCode,
                    Payload = payload
                };
            }
            catch (OperationCanceledException)
            {
                return new RemoteTunnelRequestResult
                {
                    StatusCode = 504,
                    ErrorCode = "relay_timeout",
                    Message = "The relayed Armada request timed out."
                };
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "HTTP relay failed for " + requestUri + ": " + ex.ToString());
                return new RemoteTunnelRequestResult
                {
                    StatusCode = 502,
                    ErrorCode = "relay_failed",
                    Message = ex.Message
                };
            }
        }

        private async Task<RemoteTunnelRequestResult> HandleWebSocketOpenAsync(RemoteTunnelWebSocketOpenRequest? request, CancellationToken token)
        {
            if (request == null || String.IsNullOrWhiteSpace(request.ProxySocketId))
            {
                return new RemoteTunnelRequestResult
                {
                    StatusCode = 400,
                    ErrorCode = "invalid_request",
                    Message = "Websocket relay open requires a proxySocketId."
                };
            }

            string proxySocketId = request.ProxySocketId.Trim();
            string path = NormalizePath(request.Path);
            if (!String.Equals(path, "/ws", StringComparison.OrdinalIgnoreCase))
            {
                return new RemoteTunnelRequestResult
                {
                    StatusCode = 400,
                    ErrorCode = "invalid_path",
                    Message = "Only /ws can be opened through websocket relay."
                };
            }

            RelayWebSocketSession relaySession = new RelayWebSocketSession(proxySocketId, CreateLocalWebSocket());
            if (!_WebSocketSessions.TryAdd(proxySocketId, relaySession))
            {
                return new RemoteTunnelRequestResult
                {
                    StatusCode = 409,
                    ErrorCode = "already_open",
                    Message = "A websocket relay session already exists for " + proxySocketId + "."
                };
            }

            try
            {
                await relaySession.Socket.ConnectAsync(BuildLocalWebSocketUri(path), token).ConfigureAwait(false);
                relaySession.ReceiveTask = Task.Run(() => ReceiveLoopAsync(relaySession));

                return new RemoteTunnelRequestResult
                {
                    StatusCode = 200,
                    Payload = new
                    {
                        proxySocketId = proxySocketId,
                        connected = true
                    }
                };
            }
            catch (Exception ex)
            {
                _WebSocketSessions.TryRemove(proxySocketId, out RelayWebSocketSession? _);
                relaySession.Dispose();
                _Logging.Warn(_Header + "websocket relay open failed for " + proxySocketId + ": " + ex.ToString());
                return new RemoteTunnelRequestResult
                {
                    StatusCode = 502,
                    ErrorCode = "relay_open_failed",
                    Message = ex.Message
                };
            }
        }

        private async Task<RemoteTunnelRequestResult> HandleWebSocketMessageAsync(RemoteTunnelWebSocketMessage? message, CancellationToken token)
        {
            if (message == null || String.IsNullOrWhiteSpace(message.ProxySocketId))
            {
                return new RemoteTunnelRequestResult
                {
                    StatusCode = 400,
                    ErrorCode = "invalid_request",
                    Message = "Websocket relay message requires a proxySocketId."
                };
            }

            if (!_WebSocketSessions.TryGetValue(message.ProxySocketId.Trim(), out RelayWebSocketSession? relaySession))
            {
                return new RemoteTunnelRequestResult
                {
                    StatusCode = 404,
                    ErrorCode = "not_found",
                    Message = "Websocket relay session was not found."
                };
            }

            byte[] bytes = Encoding.UTF8.GetBytes(message.Data ?? String.Empty);
            await relaySession.SendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await relaySession.Socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "websocket relay send failed for " + relaySession.ProxySocketId + ": " + ex.ToString());
                return new RemoteTunnelRequestResult
                {
                    StatusCode = 502,
                    ErrorCode = "relay_send_failed",
                    Message = ex.Message
                };
            }
            finally
            {
                relaySession.SendLock.Release();
            }

            return new RemoteTunnelRequestResult
            {
                StatusCode = 202,
                Payload = new { proxySocketId = relaySession.ProxySocketId }
            };
        }

        private async Task<RemoteTunnelRequestResult> HandleWebSocketCloseAsync(RemoteTunnelWebSocketCloseRequest? request, CancellationToken token)
        {
            if (request == null || String.IsNullOrWhiteSpace(request.ProxySocketId))
            {
                return new RemoteTunnelRequestResult
                {
                    StatusCode = 400,
                    ErrorCode = "invalid_request",
                    Message = "Websocket relay close requires a proxySocketId."
                };
            }

            if (!_WebSocketSessions.TryRemove(request.ProxySocketId.Trim(), out RelayWebSocketSession? relaySession))
            {
                return new RemoteTunnelRequestResult
                {
                    StatusCode = 404,
                    ErrorCode = "not_found",
                    Message = "Websocket relay session was not found."
                };
            }

            WebSocketCloseStatus closeStatus = request.Code.HasValue && Enum.IsDefined(typeof(WebSocketCloseStatus), request.Code.Value)
                ? (WebSocketCloseStatus)request.Code.Value
                : WebSocketCloseStatus.NormalClosure;

            await CloseRelaySocketAsync(request.ProxySocketId.Trim(), relaySession, closeStatus, request.Reason ?? "Proxy socket closed").ConfigureAwait(false);

            return new RemoteTunnelRequestResult
            {
                StatusCode = 200,
                Payload = new { proxySocketId = request.ProxySocketId.Trim(), closed = true }
            };
        }

        private async Task ReceiveLoopAsync(RelayWebSocketSession relaySession)
        {
            byte[] buffer = new byte[8192];

            try
            {
                while (!relaySession.Token.IsCancellationRequested && relaySession.Socket.State == WebSocketState.Open)
                {
                    using MemoryStream stream = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await relaySession.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), relaySession.Token).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await PublishCloseAsync(relaySession, result.CloseStatus, result.CloseStatusDescription).ConfigureAwait(false);
                            await CloseRelaySocketAsync(relaySession.ProxySocketId, relaySession, result.CloseStatus ?? WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription ?? "Remote websocket closed").ConfigureAwait(false);
                            return;
                        }

                        if (result.Count > 0)
                        {
                            stream.Write(buffer, 0, result.Count);
                        }
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    string message = Encoding.UTF8.GetString(stream.ToArray());
                    await _PublishEventAsync(
                        "armada.ws.message",
                        new RemoteTunnelWebSocketMessage
                        {
                            ProxySocketId = relaySession.ProxySocketId,
                            Data = message
                        },
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException ex)
            {
                _Logging.Warn(_Header + "websocket relay receive loop failed for " + relaySession.ProxySocketId + ": " + ex.ToString());
                await _PublishEventAsync(
                    "armada.ws.error",
                    new RemoteTunnelWebSocketCloseRequest
                    {
                        ProxySocketId = relaySession.ProxySocketId,
                        Reason = ex.Message
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "unexpected websocket relay failure for " + relaySession.ProxySocketId + ": " + ex.ToString());
                await _PublishEventAsync(
                    "armada.ws.error",
                    new RemoteTunnelWebSocketCloseRequest
                    {
                        ProxySocketId = relaySession.ProxySocketId,
                        Reason = ex.Message
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _WebSocketSessions.TryRemove(relaySession.ProxySocketId, out RelayWebSocketSession? _);
                await CloseRelaySocketAsync(relaySession.ProxySocketId, relaySession, WebSocketCloseStatus.NormalClosure, "Relay closed").ConfigureAwait(false);
            }
        }

        private async Task PublishCloseAsync(RelayWebSocketSession relaySession, WebSocketCloseStatus? closeStatus, string? reason)
        {
            await _PublishEventAsync(
                "armada.ws.closed",
                new RemoteTunnelWebSocketCloseRequest
                {
                    ProxySocketId = relaySession.ProxySocketId,
                    Code = closeStatus.HasValue ? (int)closeStatus.Value : null,
                    Reason = reason
                },
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task CloseRelaySocketAsync(string proxySocketId, RelayWebSocketSession relaySession, WebSocketCloseStatus status, string reason)
        {
            relaySession.Cancel();

            try
            {
                if (relaySession.Socket.State == WebSocketState.Open || relaySession.Socket.State == WebSocketState.CloseReceived)
                {
                    using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await relaySession.Socket.CloseAsync(status, reason, timeout.Token).ConfigureAwait(false);
                }
            }
            catch
            {
            }
            finally
            {
                relaySession.Dispose();
            }

            _Logging.Debug(_Header + "websocket relay closed for " + proxySocketId);
        }

        private ClientWebSocket CreateLocalWebSocket()
        {
            ClientWebSocket socket = new ClientWebSocket();
            if (_Settings.Rest.Ssl)
            {
                socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            }

            return socket;
        }

        private Uri BuildLocalHttpUri(string path, string? queryString)
        {
            UriBuilder builder = new UriBuilder
            {
                Scheme = _Settings.Rest.Ssl ? Uri.UriSchemeHttps : Uri.UriSchemeHttp,
                Host = ResolveLoopbackHost(),
                Port = _Settings.AdmiralPort,
                Path = path,
                Query = String.IsNullOrWhiteSpace(queryString) ? String.Empty : queryString
            };

            return builder.Uri;
        }

        private Uri BuildLocalWebSocketUri(string path)
        {
            UriBuilder builder = new UriBuilder
            {
                Scheme = _Settings.Rest.Ssl ? "wss" : "ws",
                Host = ResolveLoopbackHost(),
                Port = _Settings.AdmiralPort,
                Path = path
            };

            return builder.Uri;
        }

        private string ResolveLoopbackHost()
        {
            string configuredHost = (_Settings.Rest.Hostname ?? String.Empty).Trim();
            if (String.IsNullOrWhiteSpace(configuredHost) ||
                String.Equals(configuredHost, "localhost", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(configuredHost, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(configuredHost, "::", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(configuredHost, "*", StringComparison.OrdinalIgnoreCase))
            {
                return IPAddress.Loopback.ToString();
            }

            return configuredHost;
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

        private static bool RequiresRequestBody(string method)
        {
            return String.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(method, "PATCH", StringComparison.OrdinalIgnoreCase);
        }

        private static byte[] DecodeBody(string? bodyBase64)
        {
            if (String.IsNullOrWhiteSpace(bodyBase64))
            {
                return Array.Empty<byte>();
            }

            try
            {
                return Convert.FromBase64String(bodyBase64);
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        private sealed class RelayWebSocketSession : IDisposable
        {
            public RelayWebSocketSession(string proxySocketId, ClientWebSocket socket)
            {
                ProxySocketId = proxySocketId;
                Socket = socket;
                Cancellation = new CancellationTokenSource();
            }

            public string ProxySocketId { get; }

            public ClientWebSocket Socket { get; }

            public SemaphoreSlim SendLock { get; } = new SemaphoreSlim(1, 1);

            public CancellationTokenSource Cancellation { get; }

            public CancellationToken Token => Cancellation.Token;

            public Task? ReceiveTask { get; set; }

            private int _Disposed = 0;

            public void Cancel()
            {
                try
                {
                    Cancellation.Cancel();
                }
                catch
                {
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _Disposed, 1) != 0)
                {
                    return;
                }

                SendLock.Dispose();
                Cancellation.Dispose();
                Socket.Dispose();
            }
        }

        #endregion
    }
}
