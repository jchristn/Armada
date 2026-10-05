namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Sockets;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Proxy;
    using Armada.Proxy.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end descriptors for proxy dashboard relay integration: portal auth-and-selection gating
    /// before dashboard access, relay of representative HTTP methods/binary/error flows plus WebSocket
    /// traffic and reconnect recovery, WebSocket auth/selection enforcement, and relay-capability
    /// negotiation. Each case starts an in-process proxy server with a fake tunneled instance.
    /// </summary>
    public sealed class ProxyDashboardRelayIntegrationSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ProxyDashboardRelayIntegration";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Proxy Dashboard Relay Integration suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("portal_requires_auth_and_selection_before_dashboard", "PortalRequiresAuthAndSelectionBeforeDashboard", TestTags.Positive, async () =>
            {
                ProxyTestHarness harness = await ProxyTestHarness.StartAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                try
                {
                    HttpResponseMessage rootResponse = await harness.Browser.GetAsync("/").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, rootResponse.StatusCode, "Portal root should load");
                    AssertContains("Proxy Portal", await rootResponse.Content.ReadAsStringAsync().ConfigureAwait(false), "Portal root should serve the minimal portal");

                    HttpResponseMessage dashboardWithoutAuth = await harness.Browser.GetAsync("/dashboard").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Found, dashboardWithoutAuth.StatusCode, "Dashboard should redirect before proxy login");
                    AssertEqual("/", dashboardWithoutAuth.Headers.Location?.OriginalString, "Dashboard redirect should return to the portal");

                    await harness.LoginAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                    HttpResponseMessage dashboardWithoutSelection = await harness.Browser.GetAsync("/dashboard").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Found, dashboardWithoutSelection.StatusCode, "Dashboard should redirect before instance selection");

                    using JsonDocument instances = await harness.GetJsonAsync("/proxy-api/v1/instances").WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    JsonElement instanceList = instances.RootElement.GetProperty("instances");
                    AssertEqual(1, instanceList.GetArrayLength(), "One tunneled instance should be visible");
                    AssertEqual("smoke-instance", instanceList[0].GetProperty("instanceId").GetString(), "Instance ID should match the fake tunnel");

                    await harness.SelectInstanceAsync("smoke-instance").WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                    using JsonDocument sessionContext = await harness.GetJsonAsync("/proxy-api/v1/session/context").WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    AssertEqual("smoke-instance", sessionContext.RootElement.GetProperty("selectedInstanceId").GetString(), "Selected instance should persist in session context");
                    AssertTrue(sessionContext.RootElement.GetProperty("relay").GetProperty("websocket").GetBoolean(), "Session context should advertise websocket relay");

                    HttpResponseMessage dashboardResponse = await harness.Browser.GetAsync("/dashboard").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, dashboardResponse.StatusCode, "Dashboard should load after selection");
                    AssertContains("Dashboard Smoke", await dashboardResponse.Content.ReadAsStringAsync().ConfigureAwait(false), "Dashboard index should serve shared dashboard content");

                    HttpResponseMessage dashboardRouteResponse = await harness.Browser.GetAsync("/dashboard/planning").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, dashboardRouteResponse.StatusCode, "Dashboard SPA routes should fall back to index");
                    AssertContains("Dashboard Smoke", await dashboardRouteResponse.Content.ReadAsStringAsync().ConfigureAwait(false), "Dashboard SPA fallback should serve the index");

                    HttpResponseMessage assetResponse = await harness.Browser.GetAsync("/dashboard/assets/proxy-smoke.js").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, assetResponse.StatusCode, "Dashboard asset should load");
                    AssertContains("proxy smoke asset", await assetResponse.Content.ReadAsStringAsync().ConfigureAwait(false), "Dashboard asset should come from the proxy build");
                }
                finally
                {
                    await harness.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("proxy_relays_api_web_socket_and_reconnect_behavior", "ProxyRelaysApiWebSocketAndReconnectBehavior", TestTags.Positive, async () =>
            {
                ProxyTestHarness harness = await ProxyTestHarness.StartAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                try
                {
                    await harness.LoginAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    await harness.SelectInstanceAsync("smoke-instance").WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                    HttpResponseMessage healthResponse = await harness.Browser.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, healthResponse.StatusCode, "Relayed health request should succeed");
                    using (JsonDocument healthJson = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync().ConfigureAwait(false)))
                    {
                        AssertEqual("healthy", healthJson.RootElement.GetProperty("status").GetString(), "Relayed health payload should come from the tunneled instance");
                    }

                    HttpResponseMessage loginResponse = await harness.PostJsonAsync("/api/v1/authenticate", new
                    {
                        email = "system@armada",
                        password = "system",
                        tenantId = Constants.SystemTenantId
                    }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, loginResponse.StatusCode, "Relayed Armada login should succeed");

                    HttpResponseMessage planningCreate = await harness.PostJsonAsync("/api/v1/planning-sessions", new
                    {
                        title = "Proxy smoke plan"
                    }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, planningCreate.StatusCode, "Representative POST flow should relay");

                    HttpResponseMessage workspaceSave = await harness.PutJsonAsync("/api/v1/workspace/file", new
                    {
                        vesselId = "vsl_demo",
                        path = "README.md",
                        content = "updated remotely"
                    }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, workspaceSave.StatusCode, "Representative PUT flow should relay");

                    byte[] uploadBytes = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
                    using ByteArrayContent uploadContent = new ByteArrayContent(uploadBytes);
                    uploadContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    HttpResponseMessage binaryUpload = await harness.Browser.PostAsync("/api/v1/binary-upload", uploadContent).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, binaryUpload.StatusCode, "Binary upload should relay");
                    using (JsonDocument uploadJson = JsonDocument.Parse(await binaryUpload.Content.ReadAsStringAsync().ConfigureAwait(false)))
                    {
                        AssertEqual(uploadBytes.Length, uploadJson.RootElement.GetProperty("bytes").GetInt32(), "Binary upload byte count should relay");
                        AssertEqual("application/octet-stream", uploadJson.RootElement.GetProperty("contentType").GetString(), "Binary upload content type should relay");
                    }

                    HttpRequestMessage deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/objectives/obj_1");
                    HttpResponseMessage objectiveDelete = await harness.Browser.SendAsync(deleteRequest).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.NoContent, objectiveDelete.StatusCode, "Representative DELETE flow should relay");

                    HttpResponseMessage binaryDownload = await harness.Browser.GetAsync("/api/v1/binary-download").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, binaryDownload.StatusCode, "Binary download should relay");
                    AssertEqual("application/octet-stream", binaryDownload.Content.Headers.ContentType?.MediaType, "Binary download content type should relay");
                    AssertEqual("binary-download", Encoding.UTF8.GetString(await binaryDownload.Content.ReadAsByteArrayAsync().ConfigureAwait(false)), "Binary download body should relay");

                    HttpResponseMessage upstreamError = await harness.Browser.GetAsync("/api/v1/upstream-error").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.BadGateway, upstreamError.StatusCode, "Upstream relay errors should preserve status codes");
                    AssertContains("upstream failed", await upstreamError.Content.ReadAsStringAsync().ConfigureAwait(false), "Upstream relay errors should preserve body text");

                    HttpResponseMessage blockedRestore = await harness.PostJsonAsync("/api/v1/restore", new { path = "backup.zip" }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Forbidden, blockedRestore.StatusCode, "Blocked admin routes should fail at the proxy");
                    AssertEqual(0, harness.Tunnel.GetRequestCount("/api/v1/restore"), "Blocked routes should not be forwarded to the tunneled instance");

                    using ClientWebSocket browserSocket = await harness.ConnectBrowserWebSocketAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    await SendWebSocketTextAsync(browserSocket, "hello proxy").ConfigureAwait(false);
                    AssertEqual("echo:hello proxy", await ReceiveWebSocketTextAsync(browserSocket).ConfigureAwait(false), "Proxy websocket should relay live traffic");

                    await harness.Tunnel.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    AssertTrue(await WaitForWebSocketCloseAsync(browserSocket).ConfigureAwait(false), "Browser websocket should close when the remote tunnel drops");

                    await harness.Tunnel.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    await harness.WaitForConnectedInstanceAsync("smoke-instance").WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                    using ClientWebSocket reconnectedSocket = await harness.ConnectBrowserWebSocketAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    await SendWebSocketTextAsync(reconnectedSocket, "after reconnect").ConfigureAwait(false);
                    AssertEqual("echo:after reconnect", await ReceiveWebSocketTextAsync(reconnectedSocket).ConfigureAwait(false), "Browser websocket should recover after the tunnel reconnects");
                }
                finally
                {
                    await harness.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("proxy_web_socket_requires_auth_and_selection", "ProxyWebSocketRequiresAuthAndSelection", TestTags.Negative, async () =>
            {
                ProxyTestHarness harness = await ProxyTestHarness.StartAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                try
                {
                    using ClientWebSocket unauthenticatedSocket = await harness.ConnectBrowserWebSocketAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    AssertTrue(await WaitForWebSocketCloseAsync(unauthenticatedSocket).ConfigureAwait(false), "Proxy websocket should close when the browser has not authenticated");

                    await harness.LoginAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    using ClientWebSocket unselectedSocket = await harness.ConnectBrowserWebSocketAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    AssertTrue(await WaitForWebSocketCloseAsync(unselectedSocket).ConfigureAwait(false), "Proxy websocket should close when no deployment is selected");
                }
                finally
                {
                    await harness.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("proxy_rejects_deployments_without_dashboard_api_relay_capability", "ProxyRejectsDeploymentsWithoutDashboardApiRelayCapability", TestTags.Negative, async () =>
            {
                ProxyTestHarness harness = await ProxyTestHarness.StartAsync(new[]
                {
                    "dashboard.websocket.relay"
                }).WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                try
                {
                    await harness.LoginAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                    HttpResponseMessage selectionResponse = await harness.PostJsonAsync("/proxy-api/v1/session/instance", new
                    {
                        instanceId = "smoke-instance"
                    }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Conflict, selectionResponse.StatusCode, "Selection should fail when dashboard API relay is missing");
                    AssertContains("needs an Armada update", await selectionResponse.Content.ReadAsStringAsync().ConfigureAwait(false), "Selection failure should explain the compatibility requirement");

                    using JsonDocument sessionContext = await harness.GetJsonAsync("/proxy-api/v1/session/context").WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    bool hasSelection = sessionContext.RootElement.TryGetProperty("selectedInstanceId", out JsonElement selectedInstanceId)
                        && selectedInstanceId.ValueKind != JsonValueKind.Null
                        && !String.IsNullOrWhiteSpace(selectedInstanceId.GetString());
                    AssertFalse(hasSelection, "Failed selection should not persist a deployment");
                }
                finally
                {
                    await harness.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("proxy_session_context_reflects_selected_instance_relay_capabilities", "ProxySessionContextReflectsSelectedInstanceRelayCapabilities", TestTags.Positive, async () =>
            {
                ProxyTestHarness harness = await ProxyTestHarness.StartAsync(new[]
                {
                    "dashboard.http.relay"
                }).WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                try
                {
                    await harness.LoginAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    await harness.SelectInstanceAsync("smoke-instance").WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                    using JsonDocument sessionContext = await harness.GetJsonAsync("/proxy-api/v1/session/context").WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    JsonElement relay = sessionContext.RootElement.GetProperty("relay");
                    AssertTrue(relay.GetProperty("dashboard").GetBoolean(), "Dashboard flag should reflect HTTP relay availability");
                    AssertTrue(relay.GetProperty("api").GetBoolean(), "API relay should be true when HTTP relay is advertised");
                    AssertFalse(relay.GetProperty("websocket").GetBoolean(), "WebSocket relay should be false when websocket capability is absent");

                    using ClientWebSocket browserSocket = await harness.ConnectBrowserWebSocketAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    AssertTrue(await WaitForWebSocketCloseAsync(browserSocket).ConfigureAwait(false), "Proxy websocket should close when the selected deployment lacks websocket relay capability");
                }
                finally
                {
                    await harness.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Proxy Dashboard Relay Integration",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task SendWebSocketTextAsync(ClientWebSocket socket, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
        }

        private static async Task<string> ReceiveWebSocketTextAsync(ClientWebSocket socket)
        {
            byte[] buffer = new byte[4096];
            using MemoryStream stream = new MemoryStream();
            while (true)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new Exception("Expected a text websocket message but the socket closed.");
                }

                if (result.Count > 0)
                {
                    stream.Write(buffer, 0, result.Count);
                }

                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(stream.ToArray());
                }
            }
        }

        private static async Task<bool> WaitForWebSocketCloseAsync(ClientWebSocket socket, int timeoutMs = 5000)
        {
            using CancellationTokenSource timeout = new CancellationTokenSource(timeoutMs);
            byte[] buffer = new byte[256];
            try
            {
                while (!timeout.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
                return true;
            }

            return socket.State == WebSocketState.CloseReceived || socket.State == WebSocketState.Closed || socket.State == WebSocketState.Aborted;
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static int ReservePort()
        {
            return TestPorts.Reserve(1)[0];
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

        private sealed class ProxyTestHarness : IAsyncDisposable
        {
            private readonly Uri _BaseUri;
            private readonly HttpClientHandler _Handler;
            private readonly LoggingModule _Logging;
            private readonly string _DataDirectory;

            private ProxyTestHarness(
                ArmadaProxyServer proxy,
                FakeTunnelClient tunnel,
                HttpClient browser,
                HttpClientHandler handler,
                Uri baseUri,
                LoggingModule logging,
                string dataDirectory)
            {
                Proxy = proxy;
                Tunnel = tunnel;
                Browser = browser;
                _Handler = handler;
                _BaseUri = baseUri;
                _Logging = logging;
                _DataDirectory = dataDirectory;
            }

            public ArmadaProxyServer Proxy { get; }

            public FakeTunnelClient Tunnel { get; }

            public HttpClient Browser { get; }

            public static async Task<ProxyTestHarness> StartAsync(IEnumerable<string>? tunnelCapabilities = null)
            {
                EnsureStaticProxyAssets();

                int port = ReservePort();
                string password = "proxy-smoke-password";
                string dataDirectory = Path.Combine(Path.GetTempPath(), "armada-proxy-smoke-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dataDirectory);

                ProxySettings settings = new ProxySettings
                {
                    Hostname = "127.0.0.1",
                    Port = port,
                    Password = password,
                    DataDirectory = dataDirectory,
                    LogDirectory = Path.Combine(dataDirectory, "logs")
                };
                settings.InitializeDirectories();

                LoggingModule logging = CreateLogging();
                ArmadaProxyServer proxy = new ArmadaProxyServer(logging, settings, quiet: true);
                await proxy.StartAsync().ConfigureAwait(false);

                FakeTunnelClient tunnel = new FakeTunnelClient(port, password, "smoke-instance", tunnelCapabilities);
                await tunnel.ConnectAsync().ConfigureAwait(false);

                HttpClientHandler handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    CookieContainer = new CookieContainer(),
                    UseCookies = true
                };

                Uri baseUri = new Uri("http://127.0.0.1:" + port + "/");
                HttpClient browser = new HttpClient(handler)
                {
                    BaseAddress = baseUri,
                    Timeout = TimeSpan.FromSeconds(15)
                };

                return new ProxyTestHarness(proxy, tunnel, browser, handler, baseUri, logging, dataDirectory);
            }

            public async Task LoginAsync()
            {
                using JsonDocument challenge = await GetJsonAsync("/proxy-api/v1/auth/challenge").ConfigureAwait(false);
                string nonce = challenge.RootElement.GetProperty("nonce").GetString() ?? String.Empty;
                string proof = RemoteTunnelAuth.ComputeBrowserLoginProof("proxy-smoke-password", nonce);
                HttpResponseMessage response = await PostJsonAsync("/proxy-api/v1/auth/login", new
                {
                    nonce = nonce,
                    proofSha256 = proof
                }).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("Assertion failed: Proxy login should succeed but returned " + (int)response.StatusCode + ".");
                }
            }

            public async Task SelectInstanceAsync(string instanceId)
            {
                HttpResponseMessage response = await PostJsonAsync("/proxy-api/v1/session/instance", new
                {
                    instanceId = instanceId
                }).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("Assertion failed: Selecting an instance should succeed but returned " + (int)response.StatusCode + ".");
                }
            }

            public async Task<JsonDocument> GetJsonAsync(string path)
            {
                HttpResponseMessage response = await Browser.GetAsync(path).ConfigureAwait(false);
                string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                {
                    throw new Exception("Expected success for " + path + " but got " + (int)response.StatusCode + ": " + content);
                }

                return JsonDocument.Parse(content);
            }

            public Task<HttpResponseMessage> PostJsonAsync(string path, object payload)
            {
                return Browser.PostAsync(path, CreateJsonContent(payload));
            }

            public Task<HttpResponseMessage> PutJsonAsync(string path, object payload)
            {
                return Browser.PutAsync(path, CreateJsonContent(payload));
            }

            public async Task<ClientWebSocket> ConnectBrowserWebSocketAsync()
            {
                ClientWebSocket socket = new ClientWebSocket();
                string cookieHeader = _Handler.CookieContainer.GetCookieHeader(_BaseUri);
                if (!String.IsNullOrWhiteSpace(cookieHeader))
                {
                    socket.Options.SetRequestHeader("Cookie", cookieHeader);
                }

                using CancellationTokenSource connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await socket.ConnectAsync(new Uri("ws://127.0.0.1:" + _BaseUri.Port + "/ws"), connectTimeout.Token).ConfigureAwait(false);
                return socket;
            }

            public async Task WaitForConnectedInstanceAsync(string instanceId, int timeoutMs = 5000)
            {
                MonotonicDeadline deadlineUtc = MonotonicDeadline.After(TimeSpan.FromMilliseconds(timeoutMs));
                while (!deadlineUtc.Passed)
                {
                    using JsonDocument instances = await GetJsonAsync("/proxy-api/v1/instances").ConfigureAwait(false);
                    JsonElement array = instances.RootElement.GetProperty("instances");
                    foreach (JsonElement instance in array.EnumerateArray())
                    {
                        if (String.Equals(instance.GetProperty("instanceId").GetString(), instanceId, StringComparison.Ordinal) &&
                            String.Equals(instance.GetProperty("state").GetString(), "connected", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                    }

                    await Task.Delay(50).ConfigureAwait(false);
                }

                throw new TimeoutException("Timed out waiting for connected instance " + instanceId + ".");
            }

            public async ValueTask DisposeAsync()
            {
                Browser.Dispose();
                await Tunnel.DisposeAsync().ConfigureAwait(false);
                Proxy.Dispose();

                try
                {
                    Directory.Delete(_DataDirectory, true);
                }
                catch
                {
                }
            }

            private static StringContent CreateJsonContent(object payload)
            {
                string json = JsonSerializer.Serialize(payload, RemoteTunnelProtocol.JsonOptions);
                return new StringContent(json, Encoding.UTF8, "application/json");
            }

            private static void EnsureStaticProxyAssets()
            {
                string baseDirectory = AppContext.BaseDirectory;

                string wwwroot = Path.Combine(baseDirectory, "wwwroot");
                Directory.CreateDirectory(wwwroot);
                File.WriteAllText(Path.Combine(wwwroot, "index.html"), "<!doctype html><html><body>Proxy Portal</body></html>");
                File.WriteAllText(Path.Combine(wwwroot, "app.js"), "console.log('proxy portal smoke');");
                File.WriteAllText(Path.Combine(wwwroot, "app.css"), "body{font-family:sans-serif;}");

                string dashboard = Path.Combine(baseDirectory, "dashboard");
                string assets = Path.Combine(dashboard, "assets");
                Directory.CreateDirectory(assets);
                File.WriteAllText(Path.Combine(dashboard, "index.html"), "<!doctype html><html><body>Dashboard Smoke</body></html>");
                File.WriteAllText(Path.Combine(assets, "proxy-smoke.js"), "console.log('proxy smoke asset');");
            }
        }

        public sealed class FakeTunnelClient : IAsyncDisposable
        {
            private readonly int _ProxyPort;
            private readonly string _Password;
            private readonly string _InstanceId;
            private readonly List<string> _Capabilities;
            private readonly ConcurrentDictionary<string, int> _HttpRequestCounts = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private readonly ConcurrentDictionary<string, bool> _OpenSockets = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
            private readonly SemaphoreSlim _SendLock = new SemaphoreSlim(1, 1);

            private ClientWebSocket? _Socket;
            private Task? _ReceiveLoop;

            public FakeTunnelClient(int proxyPort, string password, string instanceId, IEnumerable<string>? capabilities = null)
            {
                _ProxyPort = proxyPort;
                _Password = password;
                _InstanceId = instanceId;
                _Capabilities = capabilities?
                    .Where(capability => !String.IsNullOrWhiteSpace(capability))
                    .Select(capability => capability.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    ?? new List<string>
                    {
                        "dashboard.http.relay",
                        "dashboard.websocket.relay"
                    };
            }

            public int GetRequestCount(string path)
            {
                return _HttpRequestCounts.TryGetValue(path, out int count) ? count : 0;
            }

            public async Task ConnectAsync()
            {
                if (_Socket != null && _Socket.State == WebSocketState.Open)
                {
                    return;
                }

                _Socket?.Dispose();
                _Socket = new ClientWebSocket();
                using CancellationTokenSource connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _Socket.ConnectAsync(new Uri("ws://127.0.0.1:" + _ProxyPort + "/tunnel"), connectTimeout.Token).ConfigureAwait(false);

                string timestampUtc = DateTime.UtcNow.ToString("o");
                string nonce = RemoteTunnelAuth.CreateNonce();
                string proof = RemoteTunnelAuth.ComputeTunnelHandshakeProof(_Password, _InstanceId, timestampUtc, nonce);
                RemoteTunnelEnvelope handshake = RemoteTunnelProtocol.CreateRequest(
                    "armada.tunnel.handshake",
                    new RemoteTunnelHandshakePayload
                    {
                        ProtocolVersion = Constants.RemoteTunnelProtocolVersion,
                        ArmadaVersion = Constants.ProductVersion,
                        InstanceId = _InstanceId,
                        PasswordNonce = nonce,
                        PasswordTimestampUtc = timestampUtc,
                        PasswordProofSha256 = proof,
                        Capabilities = new List<string>(_Capabilities)
                    });

                await SendEnvelopeAsync(handshake).ConfigureAwait(false);
                using CancellationTokenSource handshakeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                RemoteTunnelEnvelope response = await ReceiveEnvelopeAsync(_Socket, handshakeTimeout.Token).ConfigureAwait(false);
                if ((response.StatusCode ?? 0) != 200)
                {
                    throw new Exception("Tunnel handshake failed: " + response.Message);
                }

                _ReceiveLoop = Task.Run(() => ReceiveLoopAsync(_Socket), CancellationToken.None);
            }

            public async Task DisconnectAsync()
            {
                if (_Socket == null)
                {
                    return;
                }

                ClientWebSocket socket = _Socket;
                Task? receiveLoop = _ReceiveLoop;

                try
                {
                    if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                    {
                        using CancellationTokenSource closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test disconnect", closeTimeout.Token).ConfigureAwait(false);
                    }
                }
                catch
                {
                }

                try
                {
                    socket.Abort();
                }
                catch
                {
                }

                if (receiveLoop != null)
                {
                    await Task.WhenAny(receiveLoop, Task.Delay(2000)).ConfigureAwait(false);
                }

                socket.Dispose();
                _Socket = null;
                _ReceiveLoop = null;
                _OpenSockets.Clear();
            }

            public async ValueTask DisposeAsync()
            {
                await DisconnectAsync().ConfigureAwait(false);
                _SendLock.Dispose();
            }

            private async Task ReceiveLoopAsync(ClientWebSocket socket)
            {
                try
                {
                    while (socket.State == WebSocketState.Open)
                    {
                        RemoteTunnelEnvelope envelope = await ReceiveEnvelopeAsync(socket, CancellationToken.None).ConfigureAwait(false);
                        if (String.Equals(envelope.Type, "ping", StringComparison.OrdinalIgnoreCase))
                        {
                            await SendEnvelopeAsync(RemoteTunnelProtocol.CreatePong(envelope.CorrelationId)).ConfigureAwait(false);
                            continue;
                        }

                        if (!String.Equals(envelope.Type, "request", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        await HandleRequestAsync(envelope).ConfigureAwait(false);
                    }
                }
                catch
                {
                }
            }

            private async Task HandleRequestAsync(RemoteTunnelEnvelope envelope)
            {
                switch (envelope.Method?.Trim().ToLowerInvariant())
                {
                    case "armada.http.request":
                        RemoteTunnelHttpRelayRequest? relayRequest = envelope.Payload?.Deserialize<RemoteTunnelHttpRelayRequest>(RemoteTunnelProtocol.JsonOptions);
                        relayRequest ??= new RemoteTunnelHttpRelayRequest();
                        string path = relayRequest.Path ?? "/";
                        _HttpRequestCounts.AddOrUpdate(path, 1, (_, current) => current + 1);
                        await SendEnvelopeAsync(
                            RemoteTunnelProtocol.CreateResponse(
                                envelope.CorrelationId,
                                HandleHttpRelayRequest(relayRequest))).ConfigureAwait(false);
                        return;
                    case "armada.ws.open":
                        RemoteTunnelWebSocketOpenRequest? openRequest = envelope.Payload?.Deserialize<RemoteTunnelWebSocketOpenRequest>(RemoteTunnelProtocol.JsonOptions);
                        if (openRequest == null || String.IsNullOrWhiteSpace(openRequest.ProxySocketId))
                        {
                            await SendEnvelopeAsync(RemoteTunnelProtocol.CreateResponse(envelope.CorrelationId, new RemoteTunnelRequestResult
                            {
                                StatusCode = 400,
                                ErrorCode = "invalid_request",
                                Message = "proxySocketId is required."
                            })).ConfigureAwait(false);
                            return;
                        }

                        _OpenSockets[openRequest.ProxySocketId] = true;
                        await SendEnvelopeAsync(RemoteTunnelProtocol.CreateResponse(envelope.CorrelationId, new RemoteTunnelRequestResult
                        {
                            StatusCode = 200,
                            Payload = new { proxySocketId = openRequest.ProxySocketId, connected = true }
                        })).ConfigureAwait(false);
                        return;
                    case "armada.ws.message":
                        RemoteTunnelWebSocketMessage? message = envelope.Payload?.Deserialize<RemoteTunnelWebSocketMessage>(RemoteTunnelProtocol.JsonOptions);
                        if (message == null || String.IsNullOrWhiteSpace(message.ProxySocketId) || !_OpenSockets.ContainsKey(message.ProxySocketId))
                        {
                            await SendEnvelopeAsync(RemoteTunnelProtocol.CreateResponse(envelope.CorrelationId, new RemoteTunnelRequestResult
                            {
                                StatusCode = 404,
                                ErrorCode = "not_found",
                                Message = "Websocket relay session was not found."
                            })).ConfigureAwait(false);
                            return;
                        }

                        await SendEnvelopeAsync(RemoteTunnelProtocol.CreateResponse(envelope.CorrelationId, new RemoteTunnelRequestResult
                        {
                            StatusCode = 202,
                            Payload = new { proxySocketId = message.ProxySocketId }
                        })).ConfigureAwait(false);

                        await SendEnvelopeAsync(RemoteTunnelProtocol.CreateEvent("armada.ws.message", new RemoteTunnelWebSocketMessage
                        {
                            ProxySocketId = message.ProxySocketId,
                            Data = "echo:" + (message.Data ?? String.Empty)
                        })).ConfigureAwait(false);
                        return;
                    case "armada.ws.close":
                        RemoteTunnelWebSocketCloseRequest? closeRequest = envelope.Payload?.Deserialize<RemoteTunnelWebSocketCloseRequest>(RemoteTunnelProtocol.JsonOptions);
                        if (closeRequest != null && !String.IsNullOrWhiteSpace(closeRequest.ProxySocketId))
                        {
                            _OpenSockets.TryRemove(closeRequest.ProxySocketId, out bool _);
                        }

                        await SendEnvelopeAsync(RemoteTunnelProtocol.CreateResponse(envelope.CorrelationId, new RemoteTunnelRequestResult
                        {
                            StatusCode = 200,
                            Payload = new { proxySocketId = closeRequest?.ProxySocketId, closed = true }
                        })).ConfigureAwait(false);
                        return;
                }
            }

            private static RemoteTunnelRequestResult HandleHttpRelayRequest(RemoteTunnelHttpRelayRequest request)
            {
                string path = request.Path ?? "/";
                string method = (request.Method ?? "GET").Trim().ToUpperInvariant();
                byte[] requestBody = String.IsNullOrWhiteSpace(request.BodyBase64) ? Array.Empty<byte>() : Convert.FromBase64String(request.BodyBase64);

                if (String.Equals(path, "/api/v1/status/health", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(200, new { status = "healthy", source = "fake-tunnel" });
                }

                if (String.Equals(path, "/api/v1/authenticate", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(200, new { token = "fake-session-token", tenantId = Constants.SystemTenantId, userId = Constants.SystemUserId });
                }

                if (String.Equals(path, "/api/v1/planning-sessions", StringComparison.OrdinalIgnoreCase) && method == "POST")
                {
                    return JsonResponse(201, new { id = "psn_smoke", title = "Proxy smoke plan" });
                }

                if (String.Equals(path, "/api/v1/binary-upload", StringComparison.OrdinalIgnoreCase) && method == "POST")
                {
                    return JsonResponse(200, new { bytes = requestBody.Length, contentType = request.ContentType });
                }

                if (String.Equals(path, "/api/v1/workspace/file", StringComparison.OrdinalIgnoreCase) && method == "PUT")
                {
                    return JsonResponse(200, new { saved = true, bytes = requestBody.Length });
                }

                if (String.Equals(path, "/api/v1/objectives/obj_1", StringComparison.OrdinalIgnoreCase) && method == "DELETE")
                {
                    return new RemoteTunnelRequestResult
                    {
                        StatusCode = 204,
                        Payload = new RemoteTunnelHttpRelayResponse
                        {
                            StatusCode = 204
                        }
                    };
                }

                if (String.Equals(path, "/api/v1/binary-download", StringComparison.OrdinalIgnoreCase))
                {
                    return new RemoteTunnelRequestResult
                    {
                        StatusCode = 200,
                        Payload = new RemoteTunnelHttpRelayResponse
                        {
                            StatusCode = 200,
                            ContentType = "application/octet-stream",
                            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["X-Fake-Tunnel"] = "binary"
                            },
                            BodyBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("binary-download"))
                        }
                    };
                }

                if (String.Equals(path, "/api/v1/upstream-error", StringComparison.OrdinalIgnoreCase))
                {
                    return new RemoteTunnelRequestResult
                    {
                        StatusCode = 502,
                        Payload = new RemoteTunnelHttpRelayResponse
                        {
                            StatusCode = 502,
                            ContentType = "text/plain; charset=utf-8",
                            BodyBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("upstream failed"))
                        }
                    };
                }

                return JsonResponse(200, new
                {
                    method = method,
                    path = path,
                    requestBody = requestBody.Length > 0 ? Encoding.UTF8.GetString(requestBody) : null
                });
            }

            private static RemoteTunnelRequestResult JsonResponse(int statusCode, object payload)
            {
                byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload, RemoteTunnelProtocol.JsonOptions);
                return new RemoteTunnelRequestResult
                {
                    StatusCode = statusCode,
                    Payload = new RemoteTunnelHttpRelayResponse
                    {
                        StatusCode = statusCode,
                        ContentType = "application/json; charset=utf-8",
                        Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["X-Fake-Tunnel"] = "json"
                        },
                        BodyBase64 = Convert.ToBase64String(body)
                    }
                };
            }

            private async Task SendEnvelopeAsync(RemoteTunnelEnvelope envelope)
            {
                if (_Socket == null)
                {
                    throw new InvalidOperationException("Tunnel socket is not connected.");
                }

                string json = JsonSerializer.Serialize(envelope, RemoteTunnelProtocol.JsonOptions);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await _SendLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    await _Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    _SendLock.Release();
                }
            }

            private static async Task<RemoteTunnelEnvelope> ReceiveEnvelopeAsync(ClientWebSocket socket, CancellationToken token)
            {
                byte[] buffer = new byte[8192];
                using MemoryStream stream = new MemoryStream();
                while (true)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        throw new WebSocketException("Tunnel websocket closed.");
                    }

                    if (result.Count > 0)
                    {
                        stream.Write(buffer, 0, result.Count);
                    }

                    if (result.EndOfMessage)
                    {
                        string json = Encoding.UTF8.GetString(stream.ToArray());
                        return JsonSerializer.Deserialize<RemoteTunnelEnvelope>(json, RemoteTunnelProtocol.JsonOptions)
                            ?? throw new JsonException("Tunnel envelope could not be deserialized.");
                    }
                }
            }
        }

        #endregion
    }
}
