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
    using Armada.Client;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Proxy;
    using Armada.Proxy.Services;
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

            cases.Add(CaseAsync("native_bearer_session_login_select_relay_and_websocket", "NativeBearerSessionLoginSelectRelayAndWebSocket", TestTags.Positive, async () =>
            {
                ProxyTestHarness harness = await ProxyTestHarness.StartAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                try
                {
                    NativeLoginResult login = await harness.NativeLoginAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, login.Response.StatusCode, "native login should succeed");
                    string proxyToken = login.Body?.Token ?? String.Empty;
                    AssertFalse(String.IsNullOrWhiteSpace(proxyToken), "login body should carry the proxy session token");
                    AssertTrue(login.Response.Headers.CacheControl?.NoStore == true, "a response carrying a session token should be Cache-Control: no-store");

                    Dictionary<string, string> bearer = new Dictionary<string, string> { ["Authorization"] = "Bearer " + proxyToken };

                    HttpResponseMessage instancesResponse = await harness.SendNativeAsync(HttpMethod.Get, "/proxy-api/v1/instances", bearer).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, instancesResponse.StatusCode, "Authorization: Bearer <proxy token> should list instances");
                    InstancesBody instances = await ReadBodyAsync<InstancesBody>(instancesResponse).ConfigureAwait(false);
                    AssertEqual(1, instances.Count, "one tunneled instance should be visible");
                    AssertEqual("smoke-instance", instances.Instances[0].InstanceId, "instance id should match the fake tunnel");

                    HttpResponseMessage selectResponse = await harness.SendNativeAsync(HttpMethod.Post, "/proxy-api/v1/session/instance", bearer, new { instanceId = "smoke-instance" }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, selectResponse.StatusCode, "selection with a bearer session should succeed");

                    HttpResponseMessage contextResponse = await harness.SendNativeAsync(HttpMethod.Get, "/proxy-api/v1/session/context", bearer).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, contextResponse.StatusCode, "session context with a bearer session");
                    SessionContextBody context = await ReadBodyAsync<SessionContextBody>(contextResponse).ConfigureAwait(false);
                    AssertEqual("smoke-instance", context.SelectedInstanceId, "the selection is bound to the bearer session server-side");

                    // Relayed REST: the proxy session rides in X-Armada-Proxy-Session; Authorization belongs to the Admiral.
                    HttpResponseMessage relayedGet = await harness.SendNativeAsync(HttpMethod.Get, "/api/v1/status/health", new Dictionary<string, string>
                    {
                        [Constants.ProxySessionTokenHeader] = proxyToken,
                        ["Authorization"] = "Bearer admiral-bearer-token"
                    }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, relayedGet.StatusCode, "relayed GET with the proxy header should succeed");
                    Dictionary<string, string> getHeaders = harness.Tunnel.GetLastHeaders("/api/v1/status/health");
                    AssertEqual("Bearer admiral-bearer-token", getHeaders.GetValueOrDefault("Authorization"), "the Admiral's Authorization header is relayed untouched");
                    AssertFalse(getHeaders.ContainsKey(Constants.ProxySessionTokenHeader), "the proxy session header must not be relayed to the Admiral");

                    HttpResponseMessage relayedPost = await harness.SendNativeAsync(HttpMethod.Post, "/api/v1/planning-sessions", new Dictionary<string, string>
                    {
                        [Constants.ProxySessionTokenHeader] = proxyToken,
                        ["X-Token"] = "admiral-session-token"
                    }, new { title = "Native plan" }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, relayedPost.StatusCode, "relayed POST with the proxy header should succeed");
                    Dictionary<string, string> postHeaders = harness.Tunnel.GetLastHeaders("/api/v1/planning-sessions");
                    AssertEqual("admiral-session-token", postHeaders.GetValueOrDefault("X-Token"), "the Admiral's X-Token is relayed untouched");
                    AssertFalse(postHeaders.ContainsKey(Constants.ProxySessionTokenHeader), "the proxy session header must not be relayed on POST");

                    // Relayed WebSocket: the proxy token as a subprotocol entry; the Admiral token as armada-token.<base64url>.
                    string admiralEntry = "armada-token." + Base64Url("admiral-session-token");
                    using (ClientWebSocket socket = await harness.ConnectNativeWebSocketAsync(new[]
                    {
                        "armada",
                        admiralEntry,
                        ProxySessionCredentials.EncodeProtocolEntry(proxyToken)
                    }).WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false))
                    {
                        await SendWebSocketTextAsync(socket, "native hello").ConfigureAwait(false);
                        AssertEqual("echo:native hello", await ReceiveWebSocketTextAsync(socket).ConfigureAwait(false), "the subprotocol-authenticated socket should relay traffic");
                    }

                    RemoteTunnelWebSocketOpenRequest open = harness.Tunnel.GetOpenRequests().Last();
                    AssertNotNull(open.Subprotocols, "the Admiral's subprotocol entries should be relayed");
                    AssertContains(admiralEntry, open.Subprotocols!, "the armada-token entry reaches the Admiral");
                    AssertFalse(open.Subprotocols!.Contains(Constants.ProxySessionProtocolPrefix, StringComparison.OrdinalIgnoreCase), "the proxy session entry must not reach the Admiral: " + open.Subprotocols);
                    AssertFalse(open.Subprotocols!.Contains(Base64Url(proxyToken), StringComparison.Ordinal), "the proxy token must not reach the Admiral in any form");

                    // The dedicated header also works on the upgrade (React Native's WebSocket can set headers); the
                    // query string stays the Admiral's.
                    using (ClientWebSocket socket = await harness.ConnectNativeWebSocketAsync(
                        null,
                        new Dictionary<string, string> { [Constants.ProxySessionTokenHeader] = proxyToken },
                        "?token=admiral-session-token").WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false))
                    {
                        await SendWebSocketTextAsync(socket, "header hello").ConfigureAwait(false);
                        AssertEqual("echo:header hello", await ReceiveWebSocketTextAsync(socket).ConfigureAwait(false), "the header-authenticated socket should relay traffic");
                    }

                    AssertEqual("token=admiral-session-token", harness.Tunnel.GetOpenRequests().Last().QueryString, "the Admiral's query token is relayed untouched");
                }
                finally
                {
                    await harness.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("native_bearer_logout_invalidates_token", "NativeBearerLogoutInvalidatesToken", TestTags.Negative, async () =>
            {
                ProxyTestHarness harness = await ProxyTestHarness.StartAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                try
                {
                    NativeLoginResult login = await harness.NativeLoginAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    string proxyToken = login.Body?.Token ?? String.Empty;
                    Dictionary<string, string> bearer = new Dictionary<string, string> { ["Authorization"] = "Bearer " + proxyToken };
                    HttpResponseMessage select = await harness.SendNativeAsync(HttpMethod.Post, "/proxy-api/v1/session/instance", bearer, new { instanceId = "smoke-instance" }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, select.StatusCode, "selection before logout");

                    HttpResponseMessage logout = await harness.SendNativeAsync(HttpMethod.Post, "/proxy-api/v1/auth/logout", bearer).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, logout.StatusCode, "bearer logout should succeed");

                    HttpResponseMessage instances = await harness.SendNativeAsync(HttpMethod.Get, "/proxy-api/v1/instances", bearer).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Unauthorized, instances.StatusCode, "a logged-out bearer token is rejected on /proxy-api");

                    HttpResponseMessage relayed = await harness.SendNativeAsync(HttpMethod.Get, "/api/v1/status/health", new Dictionary<string, string> { [Constants.ProxySessionTokenHeader] = proxyToken }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Unauthorized, relayed.StatusCode, "a logged-out token is rejected on relayed routes");

                    using ClientWebSocket socket = await harness.ConnectNativeWebSocketAsync(new[] { ProxySessionCredentials.EncodeProtocolEntry(proxyToken) }).WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    AssertTrue(await WaitForWebSocketCloseAsync(socket).ConfigureAwait(false), "a logged-out token is rejected on /ws");
                    AssertEqual(0, harness.Tunnel.GetOpenRequests().Count, "no websocket should be opened on the Admiral for a logged-out token");
                }
                finally
                {
                    await harness.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("native_bearer_expired_token_rejected", "NativeBearerExpiredTokenRejected", TestTags.Negative, async () =>
            {
                ManualClock clock = new ManualClock(DateTime.UtcNow);
                ProxyTestHarness harness = await ProxyTestHarness.StartAsync(utcNow: clock.Now).WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                try
                {
                    NativeLoginResult login = await harness.NativeLoginAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    string proxyToken = login.Body?.Token ?? String.Empty;
                    Dictionary<string, string> bearer = new Dictionary<string, string> { ["Authorization"] = "Bearer " + proxyToken };
                    HttpResponseMessage select = await harness.SendNativeAsync(HttpMethod.Post, "/proxy-api/v1/session/instance", bearer, new { instanceId = "smoke-instance" }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, select.StatusCode, "selection before expiry");

                    clock.Advance(TimeSpan.FromHours(Constants.SessionTokenLifetimeHours).Add(TimeSpan.FromMinutes(1)));

                    HttpResponseMessage instances = await harness.SendNativeAsync(HttpMethod.Get, "/proxy-api/v1/instances", bearer).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Unauthorized, instances.StatusCode, "an expired bearer token is rejected on /proxy-api");

                    HttpResponseMessage relayed = await harness.SendNativeAsync(HttpMethod.Get, "/api/v1/status/health", new Dictionary<string, string> { [Constants.ProxySessionTokenHeader] = proxyToken }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Unauthorized, relayed.StatusCode, "an expired token is rejected on relayed routes");
                    AssertEqual(0, harness.Tunnel.GetRequestCount("/api/v1/status/health"), "nothing is relayed for an expired token");
                }
                finally
                {
                    await harness.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("native_login_lockout_applies", "NativeLoginLockoutApplies", TestTags.Negative, async () =>
            {
                ProxyTestHarness harness = await ProxyTestHarness.StartAsync(configure: settings =>
                {
                    settings.LoginMaxFailures = 3;
                    settings.LoginLockoutSeconds = 120;
                }).WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                try
                {
                    for (int i = 0; i < 3; i++)
                    {
                        NativeLoginResult bad = await harness.NativeLoginAsync("wrong-password").WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                        AssertEqual(HttpStatusCode.Unauthorized, bad.Response.StatusCode, "failed native login " + (i + 1));
                    }

                    NativeLoginResult locked = await harness.NativeLoginAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    AssertEqual((HttpStatusCode)429, locked.Response.StatusCode, "a locked-out native client gets 429 even with the right password");
                    AssertNull(locked.Body, "no session token is issued while locked out");
                }
                finally
                {
                    await harness.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("proxy_and_admiral_credentials_are_separated", "ProxyAndAdmiralCredentialsAreSeparated", TestTags.Negative, async () =>
            {
                ProxyTestHarness harness = await ProxyTestHarness.StartAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                try
                {
                    NativeLoginResult login = await harness.NativeLoginAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    string proxyToken = login.Body?.Token ?? String.Empty;
                    HttpResponseMessage select = await harness.SendNativeAsync(HttpMethod.Post, "/proxy-api/v1/session/instance", new Dictionary<string, string> { [Constants.ProxySessionTokenHeader] = proxyToken }, new { instanceId = "smoke-instance" }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, select.StatusCode, "selection with the dedicated header");

                    // On relayed routes Authorization is the Admiral's: even a valid proxy token there is not a proxy session.
                    HttpResponseMessage bearerOnRelay = await harness.SendNativeAsync(HttpMethod.Get, "/api/v1/separation-probe", new Dictionary<string, string> { ["Authorization"] = "Bearer " + proxyToken }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Unauthorized, bearerOnRelay.StatusCode, "Authorization on a relayed route is never read as the proxy session");
                    AssertEqual(0, harness.Tunnel.GetRequestCount("/api/v1/separation-probe"), "nothing is relayed without a proxy session");

                    // On /ws an armada-token entry is the Admiral's, never the proxy's.
                    using (ClientWebSocket socket = await harness.ConnectNativeWebSocketAsync(new[] { "armada-token." + Base64Url(proxyToken) }).WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false))
                    {
                        AssertTrue(await WaitForWebSocketCloseAsync(socket).ConfigureAwait(false), "an armada-token entry is not a proxy session");
                    }

                    // An Admiral credential on /proxy-api is simply not a valid proxy session.
                    HttpResponseMessage admiralOnProxyApi = await harness.SendNativeAsync(HttpMethod.Get, "/proxy-api/v1/instances", new Dictionary<string, string> { ["Authorization"] = "Bearer admiral-bearer-token" }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Unauthorized, admiralOnProxyApi.StatusCode, "an Admiral token is not a proxy session");

                    // The dedicated header wins over Authorization on /proxy-api; an invalid header does not fall back.
                    HttpResponseMessage headerWins = await harness.SendNativeAsync(HttpMethod.Get, "/proxy-api/v1/instances", new Dictionary<string, string>
                    {
                        [Constants.ProxySessionTokenHeader] = "not-a-session",
                        ["Authorization"] = "Bearer " + proxyToken
                    }).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Unauthorized, headerWins.StatusCode, "a present X-Armada-Proxy-Session is authoritative");

                    // Browser flow: the proxy cookie stays at the proxy.
                    await harness.LoginAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    await harness.SelectInstanceAsync("smoke-instance").WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    HttpResponseMessage cookieRelay = await harness.Browser.GetAsync("/api/v1/cookie-probe").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, cookieRelay.StatusCode, "cookie-authenticated relay still works");
                    Dictionary<string, string> cookieHeaders = harness.Tunnel.GetLastHeaders("/api/v1/cookie-probe");
                    AssertFalse(cookieHeaders.ContainsKey("Cookie"), "the proxy session cookie must not be relayed to the Admiral");
                }
                finally
                {
                    await harness.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("armada_client_proxy_session_token_option", "ArmadaClientProxySessionTokenOption", TestTags.Positive, async () =>
            {
                ProxyTestHarness harness = await ProxyTestHarness.StartAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                try
                {
                    NativeLoginResult login = await harness.NativeLoginAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    string proxyToken = login.Body?.Token ?? String.Empty;

                    using (ArmadaClient anonymous = new ArmadaClient(new ArmadaClientOptions("http://127.0.0.1:" + harness.Port)))
                    {
                        AssertNull(await anonymous.GetProxySessionContextAsync().ConfigureAwait(false), "no proxy session without the option");
                    }

                    ArmadaClientOptions options = new ArmadaClientOptions("http://127.0.0.1:" + harness.Port)
                    {
                        ProxySessionToken = proxyToken,
                        Token = "admiral-session-token"
                    };
                    using (ArmadaClient client = new ArmadaClient(options))
                    {
                        AssertNotNull(await client.GetProxySessionContextAsync().ConfigureAwait(false), "ProxySessionToken authenticates to the proxy");
                    }
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

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static async Task<T> ReadBodyAsync<T>(HttpResponseMessage response) where T : class, new()
        {
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(text, _JsonOptions) ?? new T();
        }

        private static string Base64Url(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

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

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private sealed class ChallengeBody
        {
            public string? Nonce { get; set; }
        }

        private sealed class LoginBody
        {
            public string? Token { get; set; }

            public DateTime ExpiresUtc { get; set; }

            public string? SelectedInstanceId { get; set; }
        }

        private sealed class InstancesBody
        {
            public int Count { get; set; }

            public List<InstanceBody> Instances { get; set; } = new List<InstanceBody>();
        }

        private sealed class InstanceBody
        {
            public string? InstanceId { get; set; }

            public string? State { get; set; }
        }

        private sealed class SessionContextBody
        {
            public string? SelectedInstanceId { get; set; }
        }

        private sealed class NativeLoginResult
        {
            public NativeLoginResult(HttpResponseMessage response, LoginBody? body)
            {
                Response = response;
                Body = body;
            }

            public HttpResponseMessage Response { get; }

            public LoginBody? Body { get; }
        }

        private sealed class ManualClock
        {
            private long _Ticks;

            public ManualClock(DateTime startUtc)
            {
                _Ticks = startUtc.Ticks;
            }

            public DateTime Now()
            {
                return new DateTime(Interlocked.Read(ref _Ticks), DateTimeKind.Utc);
            }

            public void Advance(TimeSpan by)
            {
                Interlocked.Add(ref _Ticks, by.Ticks);
            }
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
                Native = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
                {
                    BaseAddress = baseUri,
                    Timeout = TimeSpan.FromSeconds(15)
                };
            }

            public ArmadaProxyServer Proxy { get; }

            public FakeTunnelClient Tunnel { get; }

            public HttpClient Browser { get; }

            /// <summary>
            /// Native-app style client: no cookie jar, every credential is an explicit header.
            /// </summary>
            public HttpClient Native { get; }

            public int Port => _BaseUri.Port;

            public static async Task<ProxyTestHarness> StartAsync(
                IEnumerable<string>? tunnelCapabilities = null,
                Action<ProxySettings>? configure = null,
                Func<DateTime>? utcNow = null)
            {
                EnsureStaticProxyAssets();

                string password = "proxy-smoke-password";
                string dataDirectory = Path.Combine(Path.GetTempPath(), "armada-proxy-smoke-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dataDirectory);

                ProxySettings settings = new ProxySettings
                {
                    Hostname = "127.0.0.1",
                    Password = password,
                    DataDirectory = dataDirectory,
                    LogDirectory = Path.Combine(dataDirectory, "logs")
                };
                configure?.Invoke(settings);
                settings.InitializeDirectories();

                LoggingModule logging = CreateLogging();
                ArmadaProxyServer proxy = await TestPorts.StartOnFreePortsAsync(1, async ports =>
                {
                    settings.Port = ports[0];
                    ArmadaProxyServer candidate = new ArmadaProxyServer(logging, settings, quiet: true, utcNow: utcNow);
                    try
                    {
                        await candidate.StartAsync().ConfigureAwait(false);
                        return candidate;
                    }
                    catch
                    {
                        candidate.Dispose();
                        throw;
                    }
                }).ConfigureAwait(false);
                int port = settings.Port;

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

            /// <summary>
            /// Sign in like a native client: challenge, proof, and the session token from the JSON body.
            /// </summary>
            public async Task<NativeLoginResult> NativeLoginAsync(string password = "proxy-smoke-password")
            {
                HttpResponseMessage challengeResponse = await Native.GetAsync("/proxy-api/v1/auth/challenge").ConfigureAwait(false);
                ChallengeBody challenge = await ReadBodyAsync<ChallengeBody>(challengeResponse).ConfigureAwait(false);
                string proof = RemoteTunnelAuth.ComputeBrowserLoginProof(password, challenge.Nonce ?? String.Empty);
                HttpResponseMessage response = await Native.PostAsync("/proxy-api/v1/auth/login", CreateJsonContent(new
                {
                    nonce = challenge.Nonce,
                    proofSha256 = proof
                })).ConfigureAwait(false);
                string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                LoginBody? body = (int)response.StatusCode == 200 ? JsonSerializer.Deserialize<LoginBody>(text, _JsonOptions) : null;
                return new NativeLoginResult(response, body);
            }

            public Task<HttpResponseMessage> SendNativeAsync(HttpMethod method, string path, Dictionary<string, string>? headers = null, object? body = null)
            {
                HttpRequestMessage request = new HttpRequestMessage(method, path);
                foreach (KeyValuePair<string, string> header in headers ?? new Dictionary<string, string>())
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                if (body != null)
                {
                    request.Content = CreateJsonContent(body);
                }

                return Native.SendAsync(request);
            }

            public async Task<ClientWebSocket> ConnectNativeWebSocketAsync(IEnumerable<string>? subprotocols, Dictionary<string, string>? headers = null, string? query = null)
            {
                ClientWebSocket socket = new ClientWebSocket();
                foreach (string protocol in subprotocols ?? Array.Empty<string>())
                {
                    socket.Options.AddSubProtocol(protocol);
                }

                foreach (KeyValuePair<string, string> header in headers ?? new Dictionary<string, string>())
                {
                    socket.Options.SetRequestHeader(header.Key, header.Value);
                }

                using CancellationTokenSource connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await socket.ConnectAsync(new Uri("ws://127.0.0.1:" + _BaseUri.Port + "/ws" + (query ?? String.Empty)), connectTimeout.Token).ConfigureAwait(false);
                return socket;
            }

            public async ValueTask DisposeAsync()
            {
                Native.Dispose();
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
            private readonly ConcurrentDictionary<string, Dictionary<string, string>> _LastHeaders = new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            private readonly ConcurrentQueue<RemoteTunnelWebSocketOpenRequest> _OpenRequests = new ConcurrentQueue<RemoteTunnelWebSocketOpenRequest>();
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

            /// <summary>
            /// Headers of the last relayed HTTP request for a path, as the Admiral side would receive them.
            /// </summary>
            public Dictionary<string, string> GetLastHeaders(string path)
            {
                return _LastHeaders.TryGetValue(path, out Dictionary<string, string>? headers)
                    ? headers
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            /// <summary>
            /// Every websocket open request relayed to this instance, in order.
            /// </summary>
            public List<RemoteTunnelWebSocketOpenRequest> GetOpenRequests()
            {
                return _OpenRequests.ToList();
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
                        _LastHeaders[path] = new Dictionary<string, string>(relayRequest.Headers ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
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

                        _OpenRequests.Enqueue(openRequest);
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
