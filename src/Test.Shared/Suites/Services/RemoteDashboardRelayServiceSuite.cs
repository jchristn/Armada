namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Net.WebSockets;
    using System.Reflection;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the <see cref="RemoteDashboardRelayService"/>: loopback host resolution for
    /// self-relay traffic, HTTP relay of GET/POST/PUT/DELETE plus error and oversize-body rejection,
    /// and WebSocket relay of messages plus remote-close propagation and session cleanup. HTTP/WS
    /// cases run against an in-process loopback host.
    /// </summary>
    public sealed class RemoteDashboardRelayServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.RemoteDashboardRelayService";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Remote Dashboard Relay Service suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("resolve_loopback_host_maps_localhost_to_ipv4_loopback", "ResolveLoopbackHost MapsLocalhostToIpv4Loopback", TestTags.Positive, async () =>
            {
                await using RemoteDashboardRelayService service = new RemoteDashboardRelayService(
                    CreateLogging(),
                    CreateSettings(7890, "localhost"),
                    (_, _, _) => Task.CompletedTask);

                MethodInfo? resolveLoopbackHost = typeof(RemoteDashboardRelayService).GetMethod(
                    "ResolveLoopbackHost",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                AssertNotNull(resolveLoopbackHost);
                AssertEqual(IPAddress.Loopback.ToString(), resolveLoopbackHost!.Invoke(service, null)?.ToString(), "localhost should normalize to IPv4 loopback for self-relay traffic");
            }));

            cases.Add(CaseAsync("handle_async_relays_http_methods_bodies_and_errors", "HandleAsync RelaysHttpMethodsBodiesAndErrors", TestTags.Positive, async () =>
            {
                await using LoopbackRelayHost host = await LoopbackRelayHost.StartAsync().ConfigureAwait(false);
                await using RemoteDashboardRelayService service = new RemoteDashboardRelayService(
                    CreateLogging(),
                    CreateSettings(host.Port),
                    (_, _, _) => Task.CompletedTask);

                RemoteTunnelRequestResult getResult = await service.HandleAsync(
                    RemoteTunnelProtocol.CreateRequest(
                        "armada.http.request",
                        new RemoteTunnelHttpRelayRequest
                        {
                            Method = "GET",
                            Path = "/api/v1/echo",
                            QueryString = "mode=get",
                            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["X-Test-Header"] = "demo"
                            }
                        }),
                    CancellationToken.None).ConfigureAwait(false);

                RemoteTunnelHttpRelayResponse getPayload = RequireRelayResponse(getResult, "GET relay");
                JsonElement getJson = JsonDocument.Parse(DecodeBody(getPayload.BodyBase64)).RootElement;
                AssertEqual(200, getPayload.StatusCode, "GET status should relay");
                AssertEqual("GET", getJson.GetProperty("method").GetString(), "GET method should relay");
                AssertEqual("/api/v1/echo", getJson.GetProperty("path").GetString(), "GET path should relay");
                AssertEqual("mode=get", getJson.GetProperty("query").GetString(), "GET query should relay");
                AssertEqual("demo", getJson.GetProperty("header").GetString(), "GET headers should relay");

                const string postJson = "{\"mode\":\"post\",\"count\":2}";
                RemoteTunnelRequestResult postResult = await service.HandleAsync(
                    RemoteTunnelProtocol.CreateRequest(
                        "armada.http.request",
                        new RemoteTunnelHttpRelayRequest
                        {
                            Method = "POST",
                            Path = "/api/v1/echo",
                            ContentType = "application/json",
                            BodyBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(postJson))
                        }),
                    CancellationToken.None).ConfigureAwait(false);

                RemoteTunnelHttpRelayResponse postPayload = RequireRelayResponse(postResult, "POST relay");
                JsonElement postBody = JsonDocument.Parse(DecodeBody(postPayload.BodyBase64)).RootElement;
                AssertEqual(201, postPayload.StatusCode, "POST status should relay");
                AssertEqual("application/json", postBody.GetProperty("contentType").GetString(), "POST content type should relay");
                AssertEqual(postJson, postBody.GetProperty("bodyText").GetString(), "POST body should relay");

                byte[] uploadBytes = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
                RemoteTunnelRequestResult putResult = await service.HandleAsync(
                    RemoteTunnelProtocol.CreateRequest(
                        "armada.http.request",
                        new RemoteTunnelHttpRelayRequest
                        {
                            Method = "PUT",
                            Path = "/api/v1/binary",
                            ContentType = "application/octet-stream",
                            BodyBase64 = Convert.ToBase64String(uploadBytes)
                        }),
                    CancellationToken.None).ConfigureAwait(false);

                RemoteTunnelHttpRelayResponse putPayload = RequireRelayResponse(putResult, "PUT relay");
                AssertEqual(200, putPayload.StatusCode, "PUT status should relay");
                AssertEqual("application/octet-stream", putPayload.ContentType, "PUT binary content type should relay");
                AssertEqual(Convert.ToBase64String(uploadBytes), Convert.ToBase64String(DecodeBody(putPayload.BodyBase64)), "PUT binary body should relay");

                RemoteTunnelRequestResult deleteResult = await service.HandleAsync(
                    RemoteTunnelProtocol.CreateRequest(
                        "armada.http.request",
                        new RemoteTunnelHttpRelayRequest
                        {
                            Method = "DELETE",
                            Path = "/api/v1/delete-target"
                        }),
                    CancellationToken.None).ConfigureAwait(false);

                RemoteTunnelHttpRelayResponse deletePayload = RequireRelayResponse(deleteResult, "DELETE relay");
                AssertEqual(204, deletePayload.StatusCode, "DELETE status should relay");
                AssertNull(deletePayload.BodyBase64, "DELETE should not return a body");

                RemoteTunnelRequestResult errorResult = await service.HandleAsync(
                    RemoteTunnelProtocol.CreateRequest(
                        "armada.http.request",
                        new RemoteTunnelHttpRelayRequest
                        {
                            Method = "GET",
                            Path = "/api/v1/error"
                        }),
                    CancellationToken.None).ConfigureAwait(false);

                RemoteTunnelHttpRelayResponse errorPayload = RequireRelayResponse(errorResult, "Error relay");
                AssertEqual(502, errorPayload.StatusCode, "Upstream error status should relay");
                AssertContains("upstream failed", Encoding.UTF8.GetString(DecodeBody(errorPayload.BodyBase64)), "Error body should relay");

                byte[] oversizeRequest = new byte[Constants.DefaultRemoteRelayMaxBodyBytes + 1];
                RemoteTunnelRequestResult requestTooLarge = await service.HandleAsync(
                    RemoteTunnelProtocol.CreateRequest(
                        "armada.http.request",
                        new RemoteTunnelHttpRelayRequest
                        {
                            Method = "POST",
                            Path = "/api/v1/echo",
                            ContentType = "application/octet-stream",
                            BodyBase64 = Convert.ToBase64String(oversizeRequest)
                        }),
                    CancellationToken.None).ConfigureAwait(false);

                AssertEqual(413, requestTooLarge.StatusCode, "Oversize request should be rejected");
                AssertEqual("relay_body_too_large", requestTooLarge.ErrorCode, "Oversize request should return a clear error code");

                RemoteTunnelRequestResult responseTooLarge = await service.HandleAsync(
                    RemoteTunnelProtocol.CreateRequest(
                        "armada.http.request",
                        new RemoteTunnelHttpRelayRequest
                        {
                            Method = "GET",
                            Path = "/api/v1/large-response"
                        }),
                    CancellationToken.None).ConfigureAwait(false);

                AssertEqual(413, responseTooLarge.StatusCode, "Oversize response should be rejected");
                AssertEqual("relay_body_too_large", responseTooLarge.ErrorCode, "Oversize response should return a clear error code");
            }));

            cases.Add(CaseAsync("handle_async_relays_canonical_path_and_rejects_ambiguous_paths", "HandleAsync RelaysCanonicalPathAndRejectsAmbiguousPaths", TestTags.Negative, async () =>
            {
                await using LoopbackRelayHost host = await LoopbackRelayHost.StartAsync().ConfigureAwait(false);
                await using RemoteDashboardRelayService service = new RemoteDashboardRelayService(
                    CreateLogging(),
                    CreateSettings(host.Port),
                    (_, _, _) => Task.CompletedTask);

                RemoteTunnelRequestResult collapsed = await service.HandleAsync(
                    RemoteTunnelProtocol.CreateRequest(
                        "armada.http.request",
                        new RemoteTunnelHttpRelayRequest { Method = "GET", Path = "/api/v1//%65cho/" }),
                    CancellationToken.None).ConfigureAwait(false);
                RemoteTunnelHttpRelayResponse collapsedPayload = RequireRelayResponse(collapsed, "collapsed relay");
                EchoBody? echo = JsonSerializer.Deserialize<EchoBody>(DecodeBody(collapsedPayload.BodyBase64), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                AssertEqual(200, collapsedPayload.StatusCode, "canonicalized path should relay");
                AssertEqual("/api/v1/echo", echo?.Path, "the local request must use the canonical path");

                foreach (string ambiguous in new string[] { "/api/v1/./echo", "/api/v1/%2e%2e/echo", "/api/v1/echo%2fx", "/api/v1/../v1/echo", "/api/v1/echo\\x", "/api/v1" })
                {
                    RemoteTunnelRequestResult rejected = await service.HandleAsync(
                        RemoteTunnelProtocol.CreateRequest(
                            "armada.http.request",
                            new RemoteTunnelHttpRelayRequest { Method = "GET", Path = ambiguous }),
                        CancellationToken.None).ConfigureAwait(false);
                    AssertEqual(400, rejected.StatusCode, "'" + ambiguous + "' should be rejected");
                    AssertEqual("invalid_path", rejected.ErrorCode, "'" + ambiguous + "' error code");
                }
            }));

            cases.Add(CaseAsync("handle_async_relays_web_socket_messages_and_remote_close", "HandleAsync RelaysWebSocketMessagesAndRemoteClose", TestTags.Positive, async () =>
            {
                await using LoopbackRelayHost host = await LoopbackRelayHost.StartAsync().ConfigureAwait(false);
                RelayEventCollector collector = new RelayEventCollector();
                await using RemoteDashboardRelayService service = new RemoteDashboardRelayService(
                    CreateLogging(),
                    CreateSettings(host.Port),
                    collector.RecordAsync);

                RemoteTunnelRequestResult openResult = await service.HandleAsync(
                    RemoteTunnelProtocol.CreateRequest(
                        "armada.ws.open",
                        new RemoteTunnelWebSocketOpenRequest
                        {
                            ProxySocketId = "sock-1",
                            Path = "/ws"
                        }),
                    CancellationToken.None).ConfigureAwait(false);

                AssertEqual(200, openResult.StatusCode, "WebSocket open should succeed");

                RemoteTunnelRequestResult messageResult = await service.HandleAsync(
                    RemoteTunnelProtocol.CreateRequest(
                        "armada.ws.message",
                        new RemoteTunnelWebSocketMessage
                        {
                            ProxySocketId = "sock-1",
                            Data = "hello relay"
                        }),
                    CancellationToken.None).ConfigureAwait(false);

                AssertEqual(202, messageResult.StatusCode, "WebSocket message should be accepted");

                RemoteTunnelWebSocketMessage echoed = await collector.WaitForAsync<RemoteTunnelWebSocketMessage>(
                    "armada.ws.message",
                    payload => payload.ProxySocketId == "sock-1" && payload.Data == "echo:hello relay").ConfigureAwait(false);
                AssertEqual("echo:hello relay", echoed.Data, "Echoed websocket message should publish back through the relay");

                await service.HandleAsync(
                    RemoteTunnelProtocol.CreateRequest(
                        "armada.ws.message",
                        new RemoteTunnelWebSocketMessage
                        {
                            ProxySocketId = "sock-1",
                            Data = "close-me"
                        }),
                    CancellationToken.None).ConfigureAwait(false);

                RemoteTunnelWebSocketCloseRequest closed = await collector.WaitForAsync<RemoteTunnelWebSocketCloseRequest>(
                    "armada.ws.closed",
                    payload => payload.ProxySocketId == "sock-1").ConfigureAwait(false);
                AssertEqual("remote close", closed.Reason, "Remote close reason should relay back");

                RemoteTunnelRequestResult missingSession = await service.HandleAsync(
                    RemoteTunnelProtocol.CreateRequest(
                        "armada.ws.message",
                        new RemoteTunnelWebSocketMessage
                        {
                            ProxySocketId = "sock-1",
                            Data = "after close"
                        }),
                    CancellationToken.None).ConfigureAwait(false);

                AssertEqual(404, missingSession.StatusCode, "Closed websocket sessions should be removed from relay state");
            }));

            cases.Add(CaseAsync("handle_async_web_socket_open_drops_proxy_session_subprotocol", "HandleAsync WebSocketOpenDropsProxySessionSubprotocol", TestTags.Negative, async () =>
            {
                await using LoopbackRelayHost host = await LoopbackRelayHost.StartAsync().ConfigureAwait(false);
                RelayEventCollector collector = new RelayEventCollector();
                await using RemoteDashboardRelayService service = new RemoteDashboardRelayService(
                    CreateLogging(),
                    CreateSettings(host.Port),
                    collector.RecordAsync);

                RemoteTunnelRequestResult openResult = await service.HandleAsync(
                    RemoteTunnelProtocol.CreateRequest(
                        "armada.ws.open",
                        new RemoteTunnelWebSocketOpenRequest
                        {
                            ProxySocketId = "sock-proto",
                            Path = "/ws",
                            Subprotocols = "armada, armada-token.YWRtaXJhbA, " + Constants.ProxySessionProtocolPrefix + "cHJveHk"
                        }),
                    CancellationToken.None).ConfigureAwait(false);
                AssertEqual(200, openResult.StatusCode, "WebSocket open should succeed");

                string? offered = await host.WaitForWebSocketProtocolsAsync().ConfigureAwait(false);
                AssertNotNull(offered, "the local /ws upgrade should be observed");
                AssertContains("armada-token.YWRtaXJhbA", offered!, "the Admiral's own token entry is relayed");
                AssertFalse(offered!.Contains(Constants.ProxySessionProtocolPrefix, StringComparison.OrdinalIgnoreCase), "a proxy session entry must never reach the local /ws upgrade: " + offered);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Remote Dashboard Relay Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static ArmadaSettings CreateSettings(int port, string restHostname = "127.0.0.1")
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.Rest.Hostname = restHostname;
            settings.Rest.Ssl = false;
            settings.AdmiralPort = port;
            return settings;
        }

        private static RemoteTunnelHttpRelayResponse RequireRelayResponse(RemoteTunnelRequestResult result, string label)
        {
            RemoteTunnelHttpRelayResponse? payload = result.Payload as RemoteTunnelHttpRelayResponse;
            if (payload == null)
            {
                throw new Exception("Assertion failed: " + label + " payload was not a relay response");
            }

            return payload;
        }

        private static byte[] DecodeBody(string? bodyBase64)
        {
            return String.IsNullOrWhiteSpace(bodyBase64)
                ? Array.Empty<byte>()
                : Convert.FromBase64String(bodyBase64);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private sealed class RelayEventCollector
        {
            private readonly ConcurrentQueue<(string Method, object? Payload)> _Events = new ConcurrentQueue<(string Method, object? Payload)>();

            public Task RecordAsync(string method, object? payload, CancellationToken token)
            {
                _Events.Enqueue((method, payload));
                return Task.CompletedTask;
            }

            public async Task<T> WaitForAsync<T>(string method, Func<T, bool> predicate, int timeoutMs = 5000) where T : class
            {
                MonotonicDeadline deadlineUtc = MonotonicDeadline.After(TimeSpan.FromMilliseconds(timeoutMs));
                while (!deadlineUtc.Passed)
                {
                    foreach ((string Method, object? Payload) entry in _Events)
                    {
                        if (!String.Equals(entry.Method, method, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (entry.Payload is T typed && predicate(typed))
                        {
                            return typed;
                        }
                    }

                    await Task.Delay(25).ConfigureAwait(false);
                }

                throw new TimeoutException("Timed out waiting for relay event " + method + ".");
            }
        }

        private sealed class EchoBody
        {
            public string? Method { get; set; } = null;

            public string? Path { get; set; } = null;
        }

        private sealed class LoopbackRelayHost : IAsyncDisposable
        {
            private readonly HttpListener _Listener;
            private readonly CancellationTokenSource _Cancellation = new CancellationTokenSource();
            private readonly Task _LoopTask;

            private LoopbackRelayHost()
            {
                _Listener = TestPorts.StartHttpListener(p => new[] { "http://127.0.0.1:" + p + "/" }, out int port);
                Port = port;
                _LoopTask = Task.Run(() => ListenAsync(_Cancellation.Token));
            }

            public int Port { get; }

            private readonly ConcurrentQueue<string> _WebSocketProtocols = new ConcurrentQueue<string>();

            public async Task<string?> WaitForWebSocketProtocolsAsync(int timeoutMs = 5000)
            {
                MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromMilliseconds(timeoutMs));
                while (!deadline.Passed)
                {
                    if (_WebSocketProtocols.TryPeek(out string? value)) return value;
                    await Task.Delay(20).ConfigureAwait(false);
                }

                return null;
            }

            public static Task<LoopbackRelayHost> StartAsync()
            {
                return Task.FromResult(new LoopbackRelayHost());
            }

            public async ValueTask DisposeAsync()
            {
                _Cancellation.Cancel();

                try
                {
                    _Listener.Stop();
                }
                catch
                {
                }

                try
                {
                    await _LoopTask.ConfigureAwait(false);
                }
                catch
                {
                }

                _Listener.Close();
                _Cancellation.Dispose();
            }

            private async Task ListenAsync(CancellationToken token)
            {
                while (!token.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _Listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch (HttpListenerException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    _ = Task.Run(() => HandleContextAsync(context, token), CancellationToken.None);
                }
            }

            private async Task HandleContextAsync(HttpListenerContext context, CancellationToken token)
            {
                if (context.Request.IsWebSocketRequest &&
                    String.Equals(context.Request.Url?.AbsolutePath, "/ws", StringComparison.OrdinalIgnoreCase))
                {
                    _WebSocketProtocols.Enqueue(context.Request.Headers["Sec-WebSocket-Protocol"] ?? String.Empty);
                    HttpListenerWebSocketContext socketContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                    await HandleWebSocketAsync(socketContext.WebSocket, token).ConfigureAwait(false);
                    return;
                }

                string path = context.Request.Url?.AbsolutePath ?? "/";
                switch (path)
                {
                    case "/api/v1/echo":
                        await HandleEchoAsync(context, token).ConfigureAwait(false);
                        return;
                    case "/api/v1/binary":
                        await HandleBinaryAsync(context, token).ConfigureAwait(false);
                        return;
                    case "/api/v1/delete-target":
                        context.Response.StatusCode = 204;
                        context.Response.Close();
                        return;
                    case "/api/v1/error":
                        await WriteResponseAsync(context, 502, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("upstream failed"), token).ConfigureAwait(false);
                        return;
                    case "/api/v1/large-response":
                        await WriteResponseAsync(
                            context,
                            200,
                            "application/octet-stream",
                            new byte[Constants.DefaultRemoteRelayMaxBodyBytes + 1],
                            token).ConfigureAwait(false);
                        return;
                    default:
                        await WriteResponseAsync(context, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("not found"), token).ConfigureAwait(false);
                        return;
                }
            }

            private static async Task HandleEchoAsync(HttpListenerContext context, CancellationToken token)
            {
                byte[] bodyBytes = await ReadBodyAsync(context.Request.InputStream, token).ConfigureAwait(false);
                object payload = new
                {
                    method = context.Request.HttpMethod,
                    path = context.Request.Url?.AbsolutePath,
                    query = context.Request.Url?.Query?.TrimStart('?') ?? String.Empty,
                    header = context.Request.Headers["X-Test-Header"],
                    contentType = context.Request.ContentType,
                    bodyText = bodyBytes.Length > 0 ? Encoding.UTF8.GetString(bodyBytes) : null
                };

                byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload, RemoteTunnelProtocol.JsonOptions);
                int statusCode = String.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) ? 201 : 200;
                await WriteResponseAsync(context, statusCode, "application/json; charset=utf-8", json, token).ConfigureAwait(false);
            }

            private static async Task HandleBinaryAsync(HttpListenerContext context, CancellationToken token)
            {
                byte[] bodyBytes = await ReadBodyAsync(context.Request.InputStream, token).ConfigureAwait(false);
                await WriteResponseAsync(context, 200, "application/octet-stream", bodyBytes, token).ConfigureAwait(false);
            }

            private static async Task HandleWebSocketAsync(WebSocket socket, CancellationToken token)
            {
                byte[] buffer = new byte[4096];
                try
                {
                    while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                    {
                        using MemoryStream stream = new MemoryStream();
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "remote close", token).ConfigureAwait(false);
                                return;
                            }

                            if (result.Count > 0)
                            {
                                stream.Write(buffer, 0, result.Count);
                            }
                        }
                        while (!result.EndOfMessage);

                        string message = Encoding.UTF8.GetString(stream.ToArray());
                        if (String.Equals(message, "close-me", StringComparison.Ordinal))
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "remote close", token).ConfigureAwait(false);
                            return;
                        }

                        byte[] responseBytes = Encoding.UTF8.GetBytes("echo:" + message);
                        await socket.SendAsync(
                            new ArraySegment<byte>(responseBytes),
                            WebSocketMessageType.Text,
                            true,
                            token).ConfigureAwait(false);
                    }
                }
                catch
                {
                    try
                    {
                        socket.Abort();
                    }
                    catch
                    {
                    }
                }
                finally
                {
                    socket.Dispose();
                }
            }

            private static async Task<byte[]> ReadBodyAsync(Stream stream, CancellationToken token)
            {
                using MemoryStream buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, token).ConfigureAwait(false);
                return buffer.ToArray();
            }

            private static async Task WriteResponseAsync(HttpListenerContext context, int statusCode, string contentType, byte[] body, CancellationToken token)
            {
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = contentType;
                context.Response.ContentLength64 = body.LongLength;
                if (body.Length > 0)
                {
                    await context.Response.OutputStream.WriteAsync(body, 0, body.Length, token).ConfigureAwait(false);
                }

                context.Response.Close();
            }
        }

        #endregion
    }
}
