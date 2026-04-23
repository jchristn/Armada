namespace Armada.Server
{
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Net.WebSockets;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Maintains Armada's outbound remote-control tunnel connection.
    /// v0.7.0 ships the Armada-side tunnel foundation only: URL normalization,
    /// capability handshake, heartbeat/ping, reconnect, and status telemetry.
    /// </summary>
    public class RemoteTunnelManager : IDisposable
    {
        private readonly string _Header = "[RemoteTunnel] ";
        private readonly LoggingModule _Logging;
        private readonly ArmadaSettings _Settings;
        private readonly object _SyncRoot = new object();
        private readonly ConcurrentDictionary<string, DateTime> _OutstandingPings = new ConcurrentDictionary<string, DateTime>();
        private readonly SemaphoreSlim _SendLock = new SemaphoreSlim(1, 1);

        private RemoteTunnelStatus _Status = new RemoteTunnelStatus();
        private ClientWebSocket? _Socket;
        private CancellationTokenSource? _LoopTokenSource;
        private Task? _LoopTask;
        private CancellationToken _ServerToken = CancellationToken.None;

        /// <summary>
        /// Optional request handler for proxy-initiated tunnel requests.
        /// </summary>
        public Func<RemoteTunnelEnvelope, CancellationToken, Task<RemoteTunnelRequestResult>>? OnHandleRequest { get; set; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="settings">Application settings.</param>
        public RemoteTunnelManager(LoggingModule logging, ArmadaSettings settings)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            SetStatusBase(_Settings.RemoteControl.Enabled ? RemoteTunnelStateEnum.Disconnected : RemoteTunnelStateEnum.Disabled);
        }

        /// <summary>
        /// Start the background tunnel-management loop.
        /// </summary>
        /// <param name="serverToken">Server shutdown token.</param>
        public void Start(CancellationToken serverToken)
        {
            lock (_SyncRoot)
            {
                if (_LoopTask != null)
                {
                    return;
                }

                _ServerToken = serverToken;
                _LoopTokenSource = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
                _LoopTask = Task.Run(() => RunLoopAsync(_LoopTokenSource.Token), _LoopTokenSource.Token);
            }
        }

        /// <summary>
        /// Stop the background tunnel-management loop.
        /// </summary>
        public async Task StopAsync()
        {
            Task? loopTask;
            CancellationTokenSource? loopTokenSource;

            lock (_SyncRoot)
            {
                loopTask = _LoopTask;
                loopTokenSource = _LoopTokenSource;
                _LoopTask = null;
                _LoopTokenSource = null;
            }

            loopTokenSource?.Cancel();
            await CloseSocketAsync().ConfigureAwait(false);

            if (loopTask != null)
            {
                try
                {
                    await loopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            UpdateStatus(status =>
            {
                status.Enabled = _Settings.RemoteControl.Enabled;
                status.State = RemoteTunnelStateEnum.Stopping;
                status.LastDisconnectUtc = DateTime.UtcNow;
                return status;
            });
        }

        /// <summary>
        /// Reload the tunnel manager so updated settings apply immediately.
        /// </summary>
        public async Task ReloadAsync()
        {
            await StopAsync().ConfigureAwait(false);

            if (!_ServerToken.IsCancellationRequested)
            {
                Start(_ServerToken);
            }
        }

        /// <summary>
        /// Get a copy of the current tunnel status.
        /// </summary>
        /// <returns>Status snapshot.</returns>
        public RemoteTunnelStatus GetStatus()
        {
            lock (_SyncRoot)
            {
                return CloneStatus(_Status);
            }
        }

        /// <summary>
        /// Publish a server-side event to the connected proxy, if any.
        /// </summary>
        /// <param name="eventType">Event type.</param>
        /// <param name="payload">Event payload.</param>
        /// <param name="token">Optional cancellation token.</param>
        public async Task PublishEventAsync(string eventType, object? payload, CancellationToken token = default)
        {
            ClientWebSocket? socket = _Socket;
            if (socket == null || socket.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                await SendEnvelopeAsync(socket, RemoteTunnelProtocol.CreateEvent(eventType, payload), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "failed to publish tunnel event " + eventType + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Normalize a configured tunnel URL to a websocket URI.
        /// </summary>
        /// <param name="tunnelUrl">Configured tunnel URL.</param>
        /// <param name="normalizedUri">Normalized websocket URI when valid.</param>
        /// <param name="error">Validation error when invalid.</param>
        /// <returns>True if normalization succeeded.</returns>
        public static bool TryNormalizeTunnelUrl(string? tunnelUrl, out Uri? normalizedUri, out string? error)
        {
            normalizedUri = null;
            error = null;

            if (String.IsNullOrWhiteSpace(tunnelUrl))
            {
                error = "Remote control is enabled, but no tunnel URL is configured.";
                return false;
            }

            if (!Uri.TryCreate(tunnelUrl, UriKind.Absolute, out Uri? parsed))
            {
                error = "Tunnel URL must be an absolute URI.";
                return false;
            }

            string scheme = parsed.Scheme.ToLowerInvariant();
            if (scheme == Uri.UriSchemeHttp)
            {
                UriBuilder builder = EnsureTunnelPath(parsed);
                builder.Scheme = "ws";
                builder.Port = parsed.IsDefaultPort ? 80 : parsed.Port;
                normalizedUri = builder.Uri;
                return true;
            }

            if (scheme == Uri.UriSchemeHttps)
            {
                UriBuilder builder = EnsureTunnelPath(parsed);
                builder.Scheme = "wss";
                builder.Port = parsed.IsDefaultPort ? 443 : parsed.Port;
                normalizedUri = builder.Uri;
                return true;
            }

            if (scheme == "ws" || scheme == "wss")
            {
                normalizedUri = EnsureTunnelPath(parsed).Uri;
                return true;
            }

            error = "Tunnel URL must use ws, wss, http, or https.";
            return false;
        }

        private static UriBuilder EnsureTunnelPath(Uri parsed)
        {
            UriBuilder builder = new UriBuilder(parsed);
            string path = builder.Path ?? String.Empty;

            if (String.IsNullOrWhiteSpace(path) || path == "/")
            {
                builder.Path = "/tunnel";
            }

            return builder;
        }

        /// <summary>
        /// Build the capability manifest sent during handshake.
        /// </summary>
        /// <returns>Capability manifest.</returns>
        public RemoteTunnelCapabilityManifest BuildCapabilityManifest()
        {
            return new RemoteTunnelCapabilityManifest
            {
                ProtocolVersion = Constants.RemoteTunnelProtocolVersion,
                ArmadaVersion = Constants.ProductVersion,
                Features = new List<string>
                {
                    "remoteControl.handshake",
                    "remoteControl.heartbeat",
                    "remoteControl.events",
                    "remoteControl.requests",
                    "instance.summary",
                    "fleets.list",
                    "fleet.detail",
                    "fleet.create",
                    "fleet.update",
                    "vessels.list",
                    "vessel.detail",
                    "vessel.create",
                    "vessel.update",
                    "activity.recent",
                    "missions.recent",
                    "missions.list",
                    "mission.create",
                    "mission.update",
                    "mission.cancel",
                    "mission.restart",
                    "voyages.recent",
                    "voyages.list",
                    "voyage.dispatch",
                    "voyage.cancel",
                    "captains.recent",
                    "captain.stop",
                    "mission.detail",
                    "mission.log",
                    "mission.diff",
                    "voyage.detail",
                    "captain.detail",
                    "captain.log",
                    "status.health",
                    "status.snapshot",
                    "settings.remoteControl"
                }
            };
        }

        /// <summary>
        /// Compute the reconnect delay with capped exponential backoff and jitter.
        /// </summary>
        /// <param name="settings">Remote-control settings.</param>
        /// <param name="attempt">Reconnect attempt number.</param>
        /// <returns>Delay before the next reconnect attempt.</returns>
        public static TimeSpan ComputeReconnectDelay(RemoteControlSettings settings, int attempt)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (attempt < 1) attempt = 1;

            double exponent = Math.Pow(2, Math.Min(attempt - 1, 6));
            double baseDelay = settings.ReconnectBaseDelaySeconds * exponent;
            double cappedDelay = Math.Min(baseDelay, settings.ReconnectMaxDelaySeconds);
            double jitterFactor = 0.9 + (Random.Shared.NextDouble() * 0.2);
            return TimeSpan.FromSeconds(cappedDelay * jitterFactor);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                RemoteControlSettings remoteControl = _Settings.RemoteControl;
                string instanceId = ResolveInstanceId();
                RemoteTunnelCapabilityManifest capabilityManifest = BuildCapabilityManifest();

                if (!remoteControl.Enabled)
                {
                    UpdateStatus(status =>
                    {
                        status.Enabled = false;
                        status.State = RemoteTunnelStateEnum.Disabled;
                        status.TunnelUrl = remoteControl.TunnelUrl;
                        status.InstanceId = instanceId;
                        status.CapabilityManifest = capabilityManifest;
                        status.LastError = null;
                        return status;
                    });

                    await DelayAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                    continue;
                }

                if (!TryNormalizeTunnelUrl(remoteControl.TunnelUrl, out Uri? tunnelUri, out string? normalizationError))
                {
                    UpdateStatus(status =>
                    {
                        status.Enabled = true;
                        status.State = RemoteTunnelStateEnum.Error;
                        status.TunnelUrl = remoteControl.TunnelUrl;
                        status.InstanceId = instanceId;
                        status.CapabilityManifest = capabilityManifest;
                        status.LastError = normalizationError;
                        return status;
                    });

                    await DelayAsync(ComputeReconnectDelay(remoteControl, 1), token).ConfigureAwait(false);
                    continue;
                }

                Uri tunnelEndpoint = tunnelUri!;
                ClientWebSocket socket = CreateSocket(remoteControl);
                _Socket = socket;

                DateTime attemptUtc = DateTime.UtcNow;
                UpdateStatus(status =>
                {
                    status.Enabled = true;
                    status.State = RemoteTunnelStateEnum.Connecting;
                    status.TunnelUrl = tunnelEndpoint.ToString();
                    status.InstanceId = instanceId;
                    status.CapabilityManifest = capabilityManifest;
                    status.LastConnectAttemptUtc = attemptUtc;
                    status.LastError = null;
                    return status;
                });

                try
                {
                    using CancellationTokenSource connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    connectTimeout.CancelAfter(TimeSpan.FromSeconds(remoteControl.ConnectTimeoutSeconds));
                    await socket.ConnectAsync(tunnelEndpoint, connectTimeout.Token).ConfigureAwait(false);

                    DateTime connectedUtc = DateTime.UtcNow;
                    UpdateStatus(status =>
                    {
                        status.State = RemoteTunnelStateEnum.Connected;
                        status.ConnectedUtc = connectedUtc;
                        status.LastHeartbeatUtc = connectedUtc;
                        status.LastError = null;
                        status.ReconnectAttempts = 0;
                        return status;
                    });

                    _Logging.Info(_Header + "connected to " + tunnelEndpoint);
                    await SendHandshakeAsync(socket, instanceId, remoteControl, capabilityManifest, token).ConfigureAwait(false);
                    await RunConnectedLoopAsync(socket, remoteControl, token).ConfigureAwait(false);

                    UpdateStatus(status =>
                    {
                        status.State = remoteControl.Enabled ? RemoteTunnelStateEnum.Disconnected : RemoteTunnelStateEnum.Disabled;
                        status.LastDisconnectUtc = DateTime.UtcNow;
                        return status;
                    });
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    MarkFailure("Remote tunnel connection timed out.");
                }
                catch (WebSocketException ex)
                {
                    MarkFailure("Remote tunnel websocket error: " + ex.Message);
                }
                catch (Exception ex)
                {
                    MarkFailure("Remote tunnel failure: " + ex.Message);
                }
                finally
                {
                    _Socket = null;
                    socket.Dispose();
                }

                RemoteTunnelStatus statusSnapshot = GetStatus();
                int attempt = Math.Max(1, statusSnapshot.ReconnectAttempts);
                await DelayAsync(ComputeReconnectDelay(remoteControl, attempt), token).ConfigureAwait(false);
            }
        }

        private async Task RunConnectedLoopAsync(ClientWebSocket socket, RemoteControlSettings settings, CancellationToken token)
        {
            using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task receiveTask = ReceiveLoopAsync(socket, linkedSource.Token);
            Task heartbeatTask = HeartbeatLoopAsync(socket, settings, linkedSource.Token);

            Task completed = await Task.WhenAny(receiveTask, heartbeatTask).ConfigureAwait(false);
            linkedSource.Cancel();

            try
            {
                await completed.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            try
            {
                await Task.WhenAll(receiveTask, heartbeatTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            byte[] buffer = new byte[8192];

            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using MemoryStream messageStream = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    if (result.Count > 0)
                    {
                        messageStream.Write(buffer, 0, result.Count);
                    }
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                string message = Encoding.UTF8.GetString(messageStream.ToArray());
                await HandleInboundMessageAsync(socket, message, token).ConfigureAwait(false);
            }
        }

        private async Task HeartbeatLoopAsync(ClientWebSocket socket, RemoteControlSettings settings, CancellationToken token)
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await DelayAsync(TimeSpan.FromSeconds(settings.HeartbeatIntervalSeconds), token).ConfigureAwait(false);

                if (token.IsCancellationRequested || socket.State != WebSocketState.Open)
                {
                    return;
                }

                string correlationId = Guid.NewGuid().ToString("N");
                DateTime nowUtc = DateTime.UtcNow;
                _OutstandingPings[correlationId] = nowUtc;

                await SendEnvelopeAsync(socket, RemoteTunnelProtocol.CreatePing(correlationId), token).ConfigureAwait(false);

                UpdateStatus(status =>
                {
                    status.LastHeartbeatUtc = nowUtc;
                    return status;
                });
            }
        }

        private async Task HandleInboundMessageAsync(ClientWebSocket socket, string message, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(message))
            {
                return;
            }

            RemoteTunnelEnvelope envelope = JsonSerializer.Deserialize<RemoteTunnelEnvelope>(message, RemoteTunnelProtocol.JsonOptions)
                ?? new RemoteTunnelEnvelope();
            string type = envelope.Type ?? String.Empty;
            string? correlationId = envelope.CorrelationId;

            DateTime nowUtc = DateTime.UtcNow;
            UpdateStatus(status =>
            {
                status.LastHeartbeatUtc = nowUtc;
                return status;
            });

            if (String.Equals(type, "ping", StringComparison.OrdinalIgnoreCase))
            {
                await SendEnvelopeAsync(socket, RemoteTunnelProtocol.CreatePong(correlationId), token).ConfigureAwait(false);
                return;
            }

            if (String.Equals(type, "pong", StringComparison.OrdinalIgnoreCase) &&
                !String.IsNullOrEmpty(correlationId) &&
                _OutstandingPings.TryRemove(correlationId, out DateTime sentUtc))
            {
                UpdateStatus(status =>
                {
                    status.LatencyMs = (int)Math.Max(0, (DateTime.UtcNow - sentUtc).TotalMilliseconds);
                    return status;
                });
                return;
            }

            if (String.Equals(type, "request", StringComparison.OrdinalIgnoreCase))
            {
                await HandleRequestEnvelopeAsync(socket, envelope, token).ConfigureAwait(false);
                return;
            }

            if (String.Equals(type, "response", StringComparison.OrdinalIgnoreCase))
            {
                if (envelope.Success.HasValue && !envelope.Success.Value && !String.IsNullOrWhiteSpace(envelope.Message))
                {
                    UpdateStatus(status =>
                    {
                        status.LastError = envelope.Message;
                        return status;
                    });
                }

                return;
            }

            if ((String.Equals(type, "error", StringComparison.OrdinalIgnoreCase) ||
                 (String.Equals(type, "response", StringComparison.OrdinalIgnoreCase) && envelope.Success == false)) &&
                !String.IsNullOrWhiteSpace(envelope.Message))
            {
                UpdateStatus(status =>
                {
                    status.LastError = envelope.Message;
                    return status;
                });
            }
        }

        private async Task SendHandshakeAsync(
            ClientWebSocket socket,
            string instanceId,
            RemoteControlSettings settings,
            RemoteTunnelCapabilityManifest capabilityManifest,
            CancellationToken token)
        {
            string proofTimestampUtc = DateTime.UtcNow.ToString("O");
            string proofNonce = RemoteTunnelAuth.CreateNonce();
            RemoteTunnelHandshakePayload payload = new RemoteTunnelHandshakePayload
            {
                ProtocolVersion = capabilityManifest.ProtocolVersion,
                ArmadaVersion = capabilityManifest.ArmadaVersion,
                InstanceId = instanceId,
                EnrollmentToken = settings.EnrollmentToken,
                PasswordNonce = proofNonce,
                PasswordTimestampUtc = proofTimestampUtc,
                PasswordProofSha256 = RemoteTunnelAuth.ComputeTunnelHandshakeProof(settings.Password, instanceId, proofTimestampUtc, proofNonce),
                Capabilities = new List<string>(capabilityManifest.Features)
            };
            await SendEnvelopeAsync(socket, RemoteTunnelProtocol.CreateRequest("armada.tunnel.handshake", payload), token).ConfigureAwait(false);
        }

        private async Task SendEnvelopeAsync(ClientWebSocket socket, RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            string json = JsonSerializer.Serialize(envelope, RemoteTunnelProtocol.JsonOptions);
            byte[] data = Encoding.UTF8.GetBytes(json);

            await _SendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            }
            finally
            {
                _SendLock.Release();
            }
        }

        private ClientWebSocket CreateSocket(RemoteControlSettings settings)
        {
            ClientWebSocket socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(settings.HeartbeatIntervalSeconds);

            if (settings.AllowInvalidCertificates)
            {
                socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            }

            return socket;
        }

        private async Task CloseSocketAsync()
        {
            ClientWebSocket? socket = _Socket;
            if (socket == null)
            {
                return;
            }

            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Armada remote tunnel stopping", timeout.Token).ConfigureAwait(false);
                }
            }
            catch
            {
            }
        }

        private void MarkFailure(string error)
        {
            _Logging.Warn(_Header + error);
            UpdateStatus(status =>
            {
                status.State = RemoteTunnelStateEnum.Error;
                status.LastDisconnectUtc = DateTime.UtcNow;
                status.LastError = error;
                status.ReconnectAttempts = status.ReconnectAttempts + 1;
                return status;
            });
        }

        private void SetStatusBase(RemoteTunnelStateEnum state)
        {
            UpdateStatus(status =>
            {
                status.Enabled = _Settings.RemoteControl.Enabled;
                status.State = state;
                status.InstanceId = ResolveInstanceId();
                status.TunnelUrl = _Settings.RemoteControl.TunnelUrl;
                status.CapabilityManifest = BuildCapabilityManifest();
                return status;
            });
        }

        private void UpdateStatus(Func<RemoteTunnelStatus, RemoteTunnelStatus> updater)
        {
            lock (_SyncRoot)
            {
                _Status = updater(CloneStatus(_Status));
            }
        }

        private static RemoteTunnelStatus CloneStatus(RemoteTunnelStatus source)
        {
            return new RemoteTunnelStatus
            {
                Enabled = source.Enabled,
                State = source.State,
                TunnelUrl = source.TunnelUrl,
                InstanceId = source.InstanceId,
                LastConnectAttemptUtc = source.LastConnectAttemptUtc,
                ConnectedUtc = source.ConnectedUtc,
                LastHeartbeatUtc = source.LastHeartbeatUtc,
                LastDisconnectUtc = source.LastDisconnectUtc,
                LastError = source.LastError,
                ReconnectAttempts = source.ReconnectAttempts,
                LatencyMs = source.LatencyMs,
                CapabilityManifest = new RemoteTunnelCapabilityManifest
                {
                    ProtocolVersion = source.CapabilityManifest.ProtocolVersion,
                    ArmadaVersion = source.CapabilityManifest.ArmadaVersion,
                    Features = new List<string>(source.CapabilityManifest.Features)
                }
            };
        }

        private string ResolveInstanceId()
        {
            if (!String.IsNullOrWhiteSpace(_Settings.RemoteControl.InstanceId))
            {
                return _Settings.RemoteControl.InstanceId.Trim();
            }

            string basis = Environment.MachineName + "|" + Path.GetFullPath(_Settings.DataDirectory).ToLowerInvariant();
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(basis));
            string suffix = Convert.ToHexString(hash).Substring(0, 12).ToLowerInvariant();
            return "armada-" + suffix;
        }

        private static async Task DelayAsync(TimeSpan delay, CancellationToken token)
        {
            if (delay <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(delay, token).ConfigureAwait(false);
        }

        private async Task HandleRequestEnvelopeAsync(ClientWebSocket socket, RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(envelope.Method))
            {
                await SendEnvelopeAsync(
                    socket,
                    RemoteTunnelProtocol.CreateResponse(
                        envelope.CorrelationId,
                        new RemoteTunnelRequestResult
                        {
                            StatusCode = 400,
                            ErrorCode = "missing_method",
                            Message = "Tunnel request is missing a method."
                        }),
                    token).ConfigureAwait(false);
                return;
            }

            string requesterIp = String.IsNullOrWhiteSpace(envelope.RequesterIp) ? "unknown" : envelope.RequesterIp;
            string correlationSuffix = !String.IsNullOrWhiteSpace(envelope.CorrelationId)
                ? " (correlation " + envelope.CorrelationId + ")"
                : String.Empty;
            Stopwatch stopwatch = Stopwatch.StartNew();

            _Logging.Info(
                _Header +
                requesterIp + " processing " +
                envelope.Method +
                correlationSuffix);

            RemoteTunnelRequestResult result;

            if (OnHandleRequest == null)
            {
                result = new RemoteTunnelRequestResult
                {
                    StatusCode = 404,
                    ErrorCode = "unsupported_method",
                    Message = "No tunnel request handler is configured for " + envelope.Method + "."
                };
            }
            else
            {
                try
                {
                    result = await OnHandleRequest(envelope, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    result = new RemoteTunnelRequestResult
                    {
                        StatusCode = 408,
                        ErrorCode = "request_cancelled",
                        Message = "Tunnel request was cancelled."
                    };
                }
                catch (Exception ex)
                {
                    result = new RemoteTunnelRequestResult
                    {
                        StatusCode = 500,
                        ErrorCode = "request_failed",
                        Message = ex.Message
                    };
                }
            }

            stopwatch.Stop();
            int statusCode = result.StatusCode;
            if (statusCode <= 0)
            {
                statusCode = result.Success ? 200 : 500;
            }

            _Logging.Info(
                _Header +
                "processed " +
                envelope.Method +
                correlationSuffix +
                " status " +
                statusCode +
                " (" +
                stopwatch.Elapsed.TotalMilliseconds.ToString("F2") +
                "ms)");

            await SendEnvelopeAsync(socket, RemoteTunnelProtocol.CreateResponse(envelope.CorrelationId, result), token).ConfigureAwait(false);
        }
    }
}
