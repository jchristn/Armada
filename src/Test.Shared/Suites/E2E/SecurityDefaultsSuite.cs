namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// W1.3 and W1.4 coverage on dedicated servers: MCP authentication by binding (loopback vs non-loopback), the
    /// non-loopback default-credential refusal, the default-password flag (not a block) for the seeded admin, retirement of the
    /// seeded "default" bearer token, and password login refusal for the synthetic system identity.
    /// </summary>
    public sealed class SecurityDefaultsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.SecurityDefaults";
        private const string NewPassword = "correct-horse-battery-staple";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("loopback_mcp_allows_unauthenticated", "Loopback-bound MCP accepts unauthenticated calls (local Claude Code setup)", TestTags.Positive, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false))
                {
                    await server.StartAsync().ConfigureAwait(false);
                    using (HttpClient mcp = server.CreateMcpClient(false))
                    {
                        HttpResponseMessage response = await InitializeMcpAsync(mcp, null).ConfigureAwait(false);
                        AssertEqual(200, (int)response.StatusCode, "unauthenticated loopback initialize");
                    }
                }
            }));

            cases.Add(CaseAsync("loopback_mcp_rejects_invalid_credential", "A presented but invalid MCP credential is refused even on loopback", TestTags.Negative, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false))
                {
                    await server.StartAsync().ConfigureAwait(false);
                    using (HttpClient mcp = server.CreateMcpClient(false))
                    {
                        HttpResponseMessage response = await InitializeMcpAsync(mcp, "Bearer not-a-real-token").ConfigureAwait(false);
                        AssertEqual(401, (int)response.StatusCode, "invalid bearer on MCP");
                    }
                }
            }));

            cases.Add(CaseAsync("loopback_mcp_unauthenticated_can_be_disabled", "Mcp.AllowUnauthenticatedLoopback=false requires a credential on loopback", TestTags.Negative, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false))
                {
                    server.Settings.Mcp.AllowUnauthenticatedLoopback = false;
                    await server.StartAsync().ConfigureAwait(false);
                    using (HttpClient anonymous = server.CreateMcpClient(false))
                    {
                        HttpResponseMessage refused = await InitializeMcpAsync(anonymous, null).ConfigureAwait(false);
                        AssertEqual(401, (int)refused.StatusCode, "unauthenticated MCP with the setting off");
                    }

                    using (HttpClient keyed = server.CreateMcpClient(true))
                    {
                        HttpResponseMessage accepted = await InitializeMcpAsync(keyed, null).ConfigureAwait(false);
                        AssertEqual(200, (int)accepted.StatusCode, "API key on MCP");
                    }
                }
            }));

            cases.Add(CaseAsync("non_loopback_mcp_requires_credential", "Non-loopback MCP refuses unauthenticated calls and accepts a credential", TestTags.Negative, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("*", NewPassword).ConfigureAwait(false))
                {
                    await server.StartAsync().ConfigureAwait(false);
                    using (HttpClient anonymous = server.CreateMcpClient(false))
                    {
                        HttpResponseMessage refused = await InitializeMcpAsync(anonymous, null).ConfigureAwait(false);
                        AssertEqual(401, (int)refused.StatusCode, "unauthenticated MCP on a non-loopback listener");
                    }

                    using (HttpClient keyed = server.CreateMcpClient(true))
                    {
                        HttpResponseMessage accepted = await InitializeMcpAsync(keyed, null).ConfigureAwait(false);
                        AssertEqual(200, (int)accepted.StatusCode, "API key on a non-loopback MCP listener");
                    }

                    using (HttpClient rest = server.CreateRestClient(false))
                    {
                        HttpResponseMessage fleets = await rest.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                        AssertEqual(401, (int)fleets.StatusCode, "unauthenticated REST");
                    }
                }
            }));

            cases.Add(CaseAsync("non_loopback_refused_with_default_credentials", "The Admiral refuses a non-loopback hostname while default credentials are in use", TestTags.Negative, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("*", null).ConfigureAwait(false))
                {
                    UnsafeListenerConfigurationException? refused = null;
                    try
                    {
                        await server.StartAsync().ConfigureAwait(false);
                    }
                    catch (UnsafeListenerConfigurationException ex)
                    {
                        refused = ex;
                    }

                    AssertNotNull(refused, "start must be refused with the typed exception");
                    AssertEqual("*", refused!.Hostname, "exception carries the configured hostname");
                    AssertContains("Refusing to listen", refused.Message);
                }
            }));

            cases.Add(CaseAsync("non_loopback_override_allows_default_credentials", "AllowDefaultCredentialsOnNetwork lets the Admiral start with defaults", TestTags.Positive, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("*", null).ConfigureAwait(false))
                {
                    server.Settings.AllowDefaultCredentialsOnNetwork = true;
                    await server.StartAsync().ConfigureAwait(false);
                    using (HttpClient rest = server.CreateRestClient(true))
                    {
                        HttpResponseMessage status = await rest.GetAsync("/api/v1/status").ConfigureAwait(false);
                        AssertEqual(200, (int)status.StatusCode);
                    }
                }
            }));

            cases.Add(CaseAsync("default_password_change_flow", "A session for admin@armada with the default password is flagged, not blocked, and can change it", TestTags.Positive, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false))
                {
                    await server.StartAsync().ConfigureAwait(false);
                    using (HttpClient rest = server.CreateRestClient(false))
                    {
                        // Default bearer works while the default password is in place (local CLI and scripts).
                        HttpResponseMessage bearerBefore = await SendAsync(rest, HttpMethod.Get, "/api/v1/fleets", null, "Bearer " + Constants.DefaultBearerToken, null).ConfigureAwait(false);
                        AssertEqual(200, (int)bearerBefore.StatusCode, "Bearer default before the change");

                        AuthenticateResult login = await LoginAsync(rest, Constants.DefaultUserPassword).ConfigureAwait(false);
                        AssertTrue(login.Success, "default login");
                        AssertTrue(login.PasswordChangeRequired, "login reports the required change");

                        // Clients warn (and the dashboard prompts) but the API is not blocked while the default password is in place.
                        HttpResponseMessage notBlocked = await SendAsync(rest, HttpMethod.Get, "/api/v1/fleets", null, null, login.Token).ConfigureAwait(false);
                        AssertEqual(200, (int)notBlocked.StatusCode, "API usable before the password changes");

                        HttpResponseMessage whoami = await SendAsync(rest, HttpMethod.Get, "/api/v1/whoami", null, null, login.Token).ConfigureAwait(false);
                        AssertEqual(200, (int)whoami.StatusCode, "whoami allowed");
                        WhoAmIResult who = JsonHelper.Deserialize<WhoAmIResult>(await whoami.Content.ReadAsStringAsync().ConfigureAwait(false));
                        AssertTrue(who.PasswordChangeRequired, "whoami flag");
                        AssertTrue(who.DefaultCredentialsInUse, "banner flag");

                        HttpResponseMessage wrongCurrent = await SendAsync(rest, HttpMethod.Put, "/api/v1/account/password", new { CurrentPassword = "nope-nope", NewPassword = NewPassword }, null, login.Token).ConfigureAwait(false);
                        AssertEqual(403, (int)wrongCurrent.StatusCode, "wrong current password");

                        HttpResponseMessage tooShort = await SendAsync(rest, HttpMethod.Put, "/api/v1/account/password", new { CurrentPassword = Constants.DefaultUserPassword, NewPassword = "short" }, null, login.Token).ConfigureAwait(false);
                        AssertEqual(400, (int)tooShort.StatusCode, "too short");

                        HttpResponseMessage changed = await SendAsync(rest, HttpMethod.Put, "/api/v1/account/password", new { CurrentPassword = Constants.DefaultUserPassword, NewPassword = NewPassword }, null, login.Token).ConfigureAwait(false);
                        AssertEqual(200, (int)changed.StatusCode, "password changed");

                        HttpResponseMessage bearerAfter = await SendAsync(rest, HttpMethod.Get, "/api/v1/fleets", null, "Bearer " + Constants.DefaultBearerToken, null).ConfigureAwait(false);
                        AssertEqual(401, (int)bearerAfter.StatusCode, "Bearer default retired after the change");

                        AuthenticateResult relogin = await LoginAsync(rest, NewPassword).ConfigureAwait(false);
                        AssertTrue(relogin.Success, "login with the new password");
                        AssertFalse(relogin.PasswordChangeRequired, "no change required");
                        HttpResponseMessage allowed = await SendAsync(rest, HttpMethod.Get, "/api/v1/fleets", null, null, relogin.Token).ConfigureAwait(false);
                        AssertEqual(200, (int)allowed.StatusCode, "API usable after the change");

                        HttpResponseMessage whoamiAfter = await SendAsync(rest, HttpMethod.Get, "/api/v1/whoami", null, null, relogin.Token).ConfigureAwait(false);
                        WhoAmIResult whoAfter = JsonHelper.Deserialize<WhoAmIResult>(await whoamiAfter.Content.ReadAsStringAsync().ConfigureAwait(false));
                        AssertFalse(whoAfter.DefaultCredentialsInUse, "banner cleared");

                        AuthenticateResult oldLogin = await LoginAsync(rest, Constants.DefaultUserPassword).ConfigureAwait(false);
                        AssertFalse(oldLogin.Success, "old password rejected");
                    }
                }
            }));

            cases.Add(CaseAsync("system_identity_password_login_refused", "The synthetic system identity cannot log in with a password", TestTags.Negative, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false))
                {
                    await server.StartAsync().ConfigureAwait(false);
                    using (HttpClient rest = server.CreateRestClient(false))
                    {
                        HttpResponseMessage response = await SendAsync(rest, HttpMethod.Post, "/api/v1/authenticate",
                            new { TenantId = Constants.SystemTenantId, Email = Constants.SystemUserEmail, Password = "system" }, null, null).ConfigureAwait(false);
                        AssertEqual(401, (int)response.StatusCode, "system@armada password login");
                    }
                }
            }));

            cases.Add(CaseAsync("mcp_admin_tools_need_admin", "Admin-only MCP tools refuse the unauthenticated loopback caller and accept the API key", TestTags.Negative, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false))
                {
                    await server.StartAsync().ConfigureAwait(false);
                    string target = Path.Combine(server.TempDir, "mcp-backup.zip");

                    using (HttpClient anonymous = server.CreateMcpClient(false))
                    {
                        string text = await CallToolTextAsync(anonymous, "backup", new { outputPath = target }).ConfigureAwait(false);
                        AssertContains("requires", text);
                        AssertFalse(File.Exists(target), "no backup written for the loopback default caller");
                    }

                    using (HttpClient keyed = server.CreateMcpClient(true))
                    {
                        string text = await CallToolTextAsync(keyed, "status", new { }).ConfigureAwait(false);
                        AssertFalse(text.Contains("requires an admin"), "status works with the API key");
                    }
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Security Defaults",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<HttpResponseMessage> InitializeMcpAsync(HttpClient mcp, string? authorization)
        {
            object request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new { protocolVersion = "2024-11-05", capabilities = new { }, clientInfo = new { name = "security-test", version = "1.0" } }
            };

            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, "/rpc");
            message.Content = JsonHelper.ToJsonContent(request);
            if (!String.IsNullOrEmpty(authorization)) message.Headers.TryAddWithoutValidation("Authorization", authorization);
            return await mcp.SendAsync(message).ConfigureAwait(false);
        }

        private static async Task<string> CallToolTextAsync(HttpClient mcp, string tool, object arguments)
        {
            HttpResponseMessage init = await InitializeMcpAsync(mcp, null).ConfigureAwait(false);
            string sessionId = init.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values) ? values.First() : String.Empty;

            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, "/rpc");
            message.Content = JsonHelper.ToJsonContent(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = tool, arguments = arguments } });
            if (!String.IsNullOrEmpty(sessionId)) message.Headers.Add("Mcp-Session-Id", sessionId);
            HttpResponseMessage response = await mcp.SendAsync(message).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        private static async Task<AuthenticateResult> LoginAsync(HttpClient rest, string password)
        {
            HttpResponseMessage response = await SendAsync(rest, HttpMethod.Post, "/api/v1/authenticate",
                new { TenantId = Constants.DefaultTenantId, Email = Constants.DefaultUserEmail, Password = password }, null, null).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonHelper.Deserialize<AuthenticateResult>(body);
        }

        private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body, string? authorization, string? sessionToken)
        {
            HttpRequestMessage message = new HttpRequestMessage(method, path);
            if (body != null) message.Content = JsonHelper.ToJsonContent(body);
            if (!String.IsNullOrEmpty(authorization)) message.Headers.TryAddWithoutValidation("Authorization", authorization);
            if (!String.IsNullOrEmpty(sessionToken)) message.Headers.Add("X-Token", sessionToken);
            return await client.SendAsync(message).ConfigureAwait(false);
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

        #endregion
    }
}
