namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Proxy;
    using Armada.Proxy.Services;
    using Armada.Proxy.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Proxy hardening (security review O-11): the proxy refuses to start with the built-in default password unless
    /// explicitly allowed, the instance list requires a proxy session, failed logins are rate limited per client
    /// address with 429 and Retry-After, forwarded headers are ignored unless trusted, the session cookie can be
    /// marked Secure, native logins (SetCookie false) set no cookie, logout clears the cookie, and unauthenticated login
    /// challenges are capped per address.
    /// </summary>
    public sealed class ProxySecuritySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ProxySecurity";
        private const string TestPassword = "proxy-security-password";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the proxy security suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("startup_refused_with_default_password", "Proxy refuses to start with a blank or default password", TestTags.Negative, async () =>
            {
                foreach (string? password in new string?[] { null, "", Constants.DefaultRemoteTunnelPassword })
                {
                    ProxySettings settings = CreateSettings(password);
                    AssertTrue(settings.IsDefaultPassword, "password '" + (password ?? "(null)") + "' should count as the default");
                    using (ArmadaProxyServer proxy = new ArmadaProxyServer(CreateLogging(), settings, quiet: true))
                    {
                        string? error = null;
                        try
                        {
                            await proxy.StartAsync().ConfigureAwait(false);
                        }
                        catch (InvalidOperationException ex)
                        {
                            error = ex.Message;
                        }

                        AssertNotNull(error, "start should be refused for password '" + (password ?? "(null)") + "'");
                        AssertContains(ProxySettings.PasswordEnvironmentVariable, error!, "error should name the env var to set");
                        AssertContains("AllowDefaultPassword", error!, "error should name the override");
                    }

                    TryDelete(settings.DataDirectory);
                }
            }));

            cases.Add(CaseAsync("startup_allowed_with_override_or_real_password", "Proxy starts with AllowDefaultPassword or a real password", TestTags.Positive, async () =>
            {
                ProxySettings allowDefault = CreateSettings(null);
                allowDefault.AllowDefaultPassword = true;
                AssertNull(allowDefault.GetStartupSecurityError(), "override should allow start");
                await using (RunningProxy running = await RunningProxy.StartAsync(allowDefault).ConfigureAwait(false))
                {
                    HttpResponseMessage health = await running.Client.GetAsync("/proxy-api/v1/status/health").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, health.StatusCode, "health should be public");
                }

                ProxySettings real = CreateSettings(TestPassword);
                AssertFalse(real.IsDefaultPassword, "a configured password is not the default");
                AssertNull(real.GetStartupSecurityError(), "a real password should allow start");
            }));

            cases.Add(Case("environment_overrides_password_and_allow_flag", "ARMADA_PROXY_PASSWORD and ARMADA_PROXY_ALLOW_DEFAULT_PASSWORD override the settings file", TestTags.Positive, () =>
            {
                string? priorPassword = Environment.GetEnvironmentVariable(ProxySettings.PasswordEnvironmentVariable);
                string? priorAllow = Environment.GetEnvironmentVariable(ProxySettings.AllowDefaultPasswordEnvironmentVariable);
                try
                {
                    Environment.SetEnvironmentVariable(ProxySettings.PasswordEnvironmentVariable, "from-env-secret");
                    Environment.SetEnvironmentVariable(ProxySettings.AllowDefaultPasswordEnvironmentVariable, "true");
                    ProxySettings settings = new ProxySettings();
                    settings.ApplyEnvironmentOverrides();
                    AssertEqual("from-env-secret", settings.Password);
                    AssertTrue(settings.AllowDefaultPassword, "allow flag from env");
                }
                finally
                {
                    Environment.SetEnvironmentVariable(ProxySettings.PasswordEnvironmentVariable, priorPassword);
                    Environment.SetEnvironmentVariable(ProxySettings.AllowDefaultPasswordEnvironmentVariable, priorAllow);
                }
            }));

            cases.Add(CaseAsync("instances_requires_proxy_session", "GET /proxy-api/v1/instances is 401 without a session and 200 with one", TestTags.Negative, async () =>
            {
                await using (RunningProxy running = await RunningProxy.StartAsync(CreateSettings(TestPassword)).ConfigureAwait(false))
                {
                    HttpResponseMessage anonymous = await running.Client.GetAsync("/proxy-api/v1/instances").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode, "instances should require a session");

                    HttpResponseMessage login = await running.LoginAsync(TestPassword).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, login.StatusCode, "login should succeed");

                    HttpResponseMessage authenticated = await running.Client.GetAsync("/proxy-api/v1/instances").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, authenticated.StatusCode, "instances should be listed with a session");
                }
            }));

            cases.Add(CaseAsync("login_lockout_returns_429_with_retry_after", "Repeated failed logins lock the client out with 429 and Retry-After, even for the right password", TestTags.Negative, async () =>
            {
                ProxySettings settings = CreateSettings(TestPassword);
                settings.LoginMaxFailures = 3;
                settings.LoginLockoutSeconds = 120;
                await using (RunningProxy running = await RunningProxy.StartAsync(settings).ConfigureAwait(false))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        // Spoofed forwarded addresses must not split the counter: forwarded headers are untrusted by default.
                        HttpResponseMessage bad = await running.LoginAsync("wrong-password", "203.0.113." + (i + 1)).ConfigureAwait(false);
                        AssertEqual(HttpStatusCode.Unauthorized, bad.StatusCode, "failed login " + (i + 1) + " should be 401");
                    }

                    HttpResponseMessage locked = await running.LoginAsync(TestPassword, "198.51.100.7").ConfigureAwait(false);
                    AssertEqual((HttpStatusCode)429, locked.StatusCode, "locked-out client should get 429 even with the right password");
                    AssertTrue(locked.Headers.TryGetValues("Retry-After", out IEnumerable<string>? retryValues), "429 should carry Retry-After");
                    int retryAfter = Int32.Parse(retryValues!.First());
                    AssertTrue(retryAfter > 0 && retryAfter <= 120, "Retry-After should be within the lockout, got " + retryAfter);
                }
            }));

            cases.Add(Case("rate_limiter_window_lockout_and_success_reset", "Limiter counts failures in the window, locks out, expires, and resets on success", TestTags.Positive, () =>
            {
                DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                ProxySettings settings = CreateSettings(TestPassword);
                settings.LoginMaxFailures = 3;
                settings.LoginFailureWindowSeconds = 60;
                settings.LoginLockoutSeconds = 30;
                ProxyLoginRateLimiter limiter = new ProxyLoginRateLimiter(settings, () => now);

                AssertFalse(limiter.RecordFailure("10.0.0.1", out int _), "first failure");
                AssertFalse(limiter.RecordFailure("10.0.0.1", out int _), "second failure");
                limiter.RecordSuccess("10.0.0.1");
                AssertFalse(limiter.RecordFailure("10.0.0.1", out int _), "success reset the count");
                AssertFalse(limiter.RecordFailure("10.0.0.1", out int _), "second after reset");

                now = now.AddSeconds(61);
                AssertFalse(limiter.RecordFailure("10.0.0.1", out int _), "old failures left the window");
                AssertFalse(limiter.RecordFailure("10.0.0.1", out int _), "second in new window");
                AssertTrue(limiter.RecordFailure("10.0.0.1", out int lockout), "third in window locks out");
                AssertEqual(30, lockout);
                AssertTrue(limiter.IsLockedOut("10.0.0.1", out int retry), "locked out");
                AssertEqual(30, retry);
                AssertFalse(limiter.IsLockedOut("10.0.0.2", out int _), "other client unaffected");

                now = now.AddSeconds(31);
                AssertFalse(limiter.IsLockedOut("10.0.0.1", out int _), "lockout expired");
            }));

            cases.Add(Case("rate_limiter_lockouts_survive_restart", "Active lockouts are persisted to the state file and restored by a new limiter; expired ones are not", TestTags.Negative, () =>
            {
                DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                ProxySettings settings = CreateSettings(TestPassword);
                settings.LoginMaxFailures = 2;
                settings.LoginFailureWindowSeconds = 60;
                settings.LoginLockoutSeconds = 120;
                string statePath = ProxyLoginRateLimiter.DefaultStatePath(settings.DataDirectory);
                try
                {
                    ProxyLoginRateLimiter first = new ProxyLoginRateLimiter(settings, statePath, () => now);
                    AssertFalse(first.RecordFailure("10.0.0.9", out int _), "first failure");
                    AssertTrue(first.RecordFailure("10.0.0.9", out int _), "second failure locks out");
                    AssertTrue(File.Exists(statePath), "the lockout is written to the state file");

                    now = now.AddSeconds(30);
                    ProxyLoginRateLimiter restarted = new ProxyLoginRateLimiter(settings, statePath, () => now);
                    AssertTrue(restarted.IsLockedOut("10.0.0.9", out int retry), "the lockout survives a restart");
                    AssertEqual(90, retry, "remaining lockout time");
                    AssertFalse(restarted.IsLockedOut("10.0.0.10", out int _), "other clients unaffected");

                    now = now.AddSeconds(91);
                    ProxyLoginRateLimiter later = new ProxyLoginRateLimiter(settings, statePath, () => now);
                    AssertFalse(later.IsLockedOut("10.0.0.9", out int _), "an expired lockout is not restored");
                }
                finally
                {
                    try { Directory.Delete(settings.DataDirectory, true); } catch (IOException) { }
                }
            }));

            cases.Add(CaseAsync("secure_cookie_setting_marks_session_cookie_secure", "SecureCookie adds the Secure attribute to the session cookie", TestTags.Positive, async () =>
            {
                ProxySettings settings = CreateSettings(TestPassword);
                settings.SecureCookie = true;
                await using (RunningProxy running = await RunningProxy.StartAsync(settings).ConfigureAwait(false))
                {
                    HttpResponseMessage login = await running.LoginAsync(TestPassword).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, login.StatusCode, "login should succeed");
                    AssertTrue(login.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies), "login should set a cookie");
                    AssertContains("Secure", String.Join(";", cookies!), "cookie should be Secure");
                }
            }));

            cases.Add(CaseAsync("native_login_sets_no_cookie", "A login with SetCookie false returns the token but sets no cookie; the token works as a bearer credential", TestTags.Positive, async () =>
            {
                await using (RunningProxy running = await RunningProxy.StartAsync(CreateSettings(TestPassword)).ConfigureAwait(false))
                {
                    HttpResponseMessage login = await running.LoginAsync(TestPassword, setCookie: false).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, login.StatusCode, "login should succeed");
                    AssertFalse(login.Headers.Contains("Set-Cookie"), "a native login must not set the session cookie");
                    LoginBody body = JsonSerializer.Deserialize<LoginBody>(await login.Content.ReadAsStringAsync().ConfigureAwait(false), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                    AssertFalse(String.IsNullOrEmpty(body.Token), "the token is returned");

                    HttpResponseMessage noCredential = await running.Client.GetAsync("/proxy-api/v1/instances").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Unauthorized, noCredential.StatusCode, "nothing was stored in the client's cookie jar");
                    HttpRequestMessage bearer = new HttpRequestMessage(HttpMethod.Get, "/proxy-api/v1/instances");
                    bearer.Headers.Add("Authorization", "Bearer " + body.Token);
                    AssertEqual(HttpStatusCode.OK, (await running.Client.SendAsync(bearer).ConfigureAwait(false)).StatusCode, "the token works as a bearer credential");
                }
            }));

            cases.Add(CaseAsync("browser_login_cookie_is_cleared_by_logout", "A browser login sets the cookie; logout invalidates the session and clears the cookie", TestTags.Positive, async () =>
            {
                await using (RunningProxy running = await RunningProxy.StartAsync(CreateSettings(TestPassword)).ConfigureAwait(false))
                {
                    HttpResponseMessage login = await running.LoginAsync(TestPassword).ConfigureAwait(false);
                    AssertTrue(login.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies), "a browser login sets the cookie");
                    AssertContains(Constants.ProxySessionCookieName + "=", String.Join(";", cookies!), "session cookie");
                    AssertEqual(HttpStatusCode.OK, (await running.Client.GetAsync("/proxy-api/v1/instances").ConfigureAwait(false)).StatusCode, "the cookie authenticates");

                    HttpResponseMessage logout = await running.Client.PostAsync("/proxy-api/v1/auth/logout", new StringContent("{}", Encoding.UTF8, "application/json")).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, logout.StatusCode, "logout");
                    AssertTrue(logout.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cleared), "logout clears the cookie");
                    AssertContains("Max-Age=0", String.Join(";", cleared!), "expired cookie");
                    AssertEqual(HttpStatusCode.Unauthorized, (await running.Client.GetAsync("/proxy-api/v1/instances").ConfigureAwait(false)).StatusCode, "the session is gone");
                }
            }));

            cases.Add(CaseAsync("challenge_flood_from_one_address_is_refused", "Unauthenticated challenges are capped per address: 429 with Retry-After and a typed refusal", TestTags.Negative, async () =>
            {
                await using (RunningProxy running = await RunningProxy.StartAsync(CreateSettings(TestPassword)).ConfigureAwait(false))
                {
                    for (int i = 0; i < ProxyAuthService.DefaultMaxPendingChallengesPerAddress; i++)
                    {
                        HttpResponseMessage ok = await running.Client.GetAsync("/proxy-api/v1/auth/challenge").ConfigureAwait(false);
                        AssertEqual(HttpStatusCode.OK, ok.StatusCode, "challenge " + i);
                    }

                    HttpResponseMessage refused = await running.Client.GetAsync("/proxy-api/v1/auth/challenge").ConfigureAwait(false);
                    AssertEqual((HttpStatusCode)429, refused.StatusCode, "the address holds too many challenges");
                    AssertTrue(refused.Headers.TryGetValues("Retry-After", out IEnumerable<string>? retry) && Int32.Parse(retry!.First()) >= 1, "Retry-After");
                    AssertContains("AddressLimit", await refused.Content.ReadAsStringAsync().ConfigureAwait(false), "typed refusal");
                }
            }));

            cases.Add(CaseAsync("malformed_login_body_is_400_and_counts_toward_lockout", "A malformed login body is 400 and counts as a failed login", TestTags.Negative, async () =>
            {
                ProxySettings settings = CreateSettings(TestPassword);
                settings.LoginMaxFailures = 2;
                settings.LoginLockoutSeconds = 120;
                await using (RunningProxy running = await RunningProxy.StartAsync(settings).ConfigureAwait(false))
                {
                    string[] malformedBodies = new string[] { "{not json", "{\"nonce\": 42, \"proofSha256\": []}" };
                    foreach (string malformed in malformedBodies)
                    {
                        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/proxy-api/v1/auth/login");
                        request.Content = new StringContent(malformed, Encoding.UTF8, "application/json");
                        HttpResponseMessage response = await running.Client.SendAsync(request).ConfigureAwait(false);
                        AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, "malformed body '" + malformed + "' should be 400");
                    }

                    HttpResponseMessage locked = await running.LoginAsync(TestPassword).ConfigureAwait(false);
                    AssertEqual((HttpStatusCode)429, locked.StatusCode, "malformed bodies must count toward the lockout");
                }
            }));

            cases.Add(Case("settings_file_loads_typed_values_and_rejects_wrong_types", "proxysettings.json is read into typed values; a wrong type fails loudly", TestTags.Negative, () =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "armada-proxy-settings-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                try
                {
                    string good = Path.Combine(directory, "good.json");
                    File.WriteAllText(good, "{ \"ArmadaProxy\": { \"Port\": \"8123\", \"TrustForwardedHeaders\": \"true\", \"SecureCookie\": true, " +
                        "\"LoginMaxFailures\": 4, \"Password\": \"typed-secret\", \"EnrollmentTokens\": [\" a \", \"a\", \"\", \"b\"] } }");
                    ProxySettings loaded = ProxySettings.LoadFromFile(good);
                    AssertEqual(8123, loaded.Port);
                    AssertTrue(loaded.TrustForwardedHeaders, "string boolean accepted");
                    AssertTrue(loaded.SecureCookie, "boolean accepted");
                    AssertEqual(4, loaded.LoginMaxFailures);
                    AssertEqual("typed-secret", loaded.Password);
                    AssertEqual(2, loaded.EnrollmentTokens.Count, "tokens trimmed, blank dropped, distinct");

                    string[] badFiles = new string[]
                    {
                        "{ \"TrustForwardedHeaders\": \"yes\" }",
                        "{ \"LoginMaxFailures\": true }",
                        "{ \"Password\": 12345 }",
                        "{ \"EnrollmentTokens\": \"single\" }",
                        "[ 1, 2 ]"
                    };
                    foreach (string badJson in badFiles)
                    {
                        string bad = Path.Combine(directory, "bad.json");
                        File.WriteAllText(bad, badJson);
                        bool threw = false;
                        try
                        {
                            ProxySettings.LoadFromFile(bad);
                        }
                        catch (InvalidDataException)
                        {
                            threw = true;
                        }

                        AssertTrue(threw, "settings '" + badJson + "' should fail to load");
                    }
                }
                finally
                {
                    TryDelete(directory);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Proxy Security",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ProxySettings CreateSettings(string? password)
        {
            string dataDirectory = Path.Combine(Path.GetTempPath(), "armada-proxy-security-" + Guid.NewGuid().ToString("N"));
            ProxySettings settings = new ProxySettings
            {
                Hostname = "127.0.0.1",
                Port = ReservePort(),
                Password = password,
                DataDirectory = dataDirectory,
                LogDirectory = Path.Combine(dataDirectory, "logs")
            };
            settings.InitializeDirectories();
            return settings;
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

        private static void TryDelete(string directory)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch
            {
            }
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
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

        #region Nested-Types

        private sealed class RunningProxy : IAsyncDisposable
        {
            public ArmadaProxyServer Proxy { get; private set; } = null!;

            public HttpClient Client { get; private set; } = null!;

            public ProxySettings Settings { get; private set; } = null!;

            public static async Task<RunningProxy> StartAsync(ProxySettings settings)
            {
                RunningProxy running = new RunningProxy();
                running.Settings = settings;
                running.Proxy = await TestPorts.StartOnFreePortsAsync(1, async ports =>
                {
                    settings.Port = ports[0];
                    ArmadaProxyServer candidate = new ArmadaProxyServer(CreateLogging(), settings, quiet: true);
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
                HttpClientHandler handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    CookieContainer = new CookieContainer(),
                    UseCookies = true
                };
                running.Client = new HttpClient(handler)
                {
                    BaseAddress = new Uri("http://127.0.0.1:" + settings.Port + "/"),
                    Timeout = TimeSpan.FromSeconds(15)
                };
                return running;
            }

            public async Task<HttpResponseMessage> LoginAsync(string password, string? forwardedFor = null, bool? setCookie = null)
            {
                HttpResponseMessage challengeResponse = await Client.GetAsync("/proxy-api/v1/auth/challenge").ConfigureAwait(false);
                string challengeJson = await challengeResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                ChallengeBody challenge = JsonSerializer.Deserialize<ChallengeBody>(challengeJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                string proof = RemoteTunnelAuth.ComputeBrowserLoginProof(password, challenge.Nonce);
                string body = setCookie.HasValue
                    ? JsonSerializer.Serialize(new { nonce = challenge.Nonce, proofSha256 = proof, setCookie = setCookie.Value })
                    : JsonSerializer.Serialize(new { nonce = challenge.Nonce, proofSha256 = proof });
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/proxy-api/v1/auth/login");
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                if (!String.IsNullOrEmpty(forwardedFor)) request.Headers.Add("X-Forwarded-For", forwardedFor);
                return await Client.SendAsync(request).ConfigureAwait(false);
            }

            public ValueTask DisposeAsync()
            {
                Client?.Dispose();
                Proxy?.Dispose();
                TryDelete(Settings.DataDirectory);
                return ValueTask.CompletedTask;
            }
        }

        private sealed class ChallengeBody
        {
            public string Nonce { get; set; } = String.Empty;
        }

        private sealed class LoginBody
        {
            public string Token { get; set; } = String.Empty;
        }

        #endregion
    }
}
