namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// O-05 coverage on dedicated servers: salted PBKDF2 storage of new passwords, the startup upgrade and login rehash of
    /// legacy unsalted SHA-256 hashes, per-account lockout (429 with Retry-After, even with the right password), the
    /// counter reset on success, per-address lockout of guessable credentials on REST and MCP (session tokens keep
    /// working), and the tenant lookup budget.
    /// </summary>
    public sealed class LoginSecuritySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.LoginSecurity";
        private const string AdminPassword = "correct-horse-battery-staple";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("startup_upgrades_legacy_hashes", "Admiral start rewrites legacy unsalted hashes to PBKDF2 and the password still works", TestTags.Positive, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false))
                {
                    string legacy = UserMaster.ComputePasswordHash(AdminPassword);
                    await WriteStoredHashAsync(server, Constants.DefaultUserId, legacy).ConfigureAwait(false);
                    AssertEqual(legacy, await ReadStoredHashAsync(server, Constants.DefaultUserId).ConfigureAwait(false));

                    await server.StartAsync().ConfigureAwait(false);
                    string stored = await ReadStoredHashAsync(server, Constants.DefaultUserId).ConfigureAwait(false);
                    AssertStartsWith("pbkdf2-sha256$600000$", stored);

                    using (HttpClient rest = server.CreateRestClient(false))
                    {
                        HttpResponseMessage ok = await LoginAsync(rest, Constants.DefaultUserEmail, AdminPassword).ConfigureAwait(false);
                        AssertEqual(200, (int)ok.StatusCode, "login after upgrade");
                    }
                }
            }));

            cases.Add(CaseAsync("legacy_hash_rehashed_on_login", "New users are stored as PBKDF2; a legacy row logs in and is rewritten to PBKDF2", TestTags.Positive, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", AdminPassword).ConfigureAwait(false))
                {
                    await server.StartAsync().ConfigureAwait(false);
                    using (HttpClient admin = server.CreateRestClient(true))
                    using (HttpClient rest = server.CreateRestClient(false))
                    {
                        string userId = await CreateUserAsync(admin, "legacy@login.armada", "user-password-123").ConfigureAwait(false);
                        string created = await ReadStoredHashAsync(server, userId).ConfigureAwait(false);
                        AssertStartsWith("pbkdf2-sha256$", created, "client-supplied SHA-256 is stretched on create");

                        string legacy = UserMaster.ComputePasswordHash("user-password-123");
                        await WriteStoredHashAsync(server, userId, legacy).ConfigureAwait(false);

                        HttpResponseMessage wrong = await LoginAsync(rest, "legacy@login.armada", "not-the-password").ConfigureAwait(false);
                        AssertEqual(401, (int)wrong.StatusCode, "wrong password against a legacy hash");
                        AssertEqual(legacy, await ReadStoredHashAsync(server, userId).ConfigureAwait(false), "failed login leaves the hash");

                        HttpResponseMessage ok = await LoginAsync(rest, "legacy@login.armada", "user-password-123").ConfigureAwait(false);
                        AssertEqual(200, (int)ok.StatusCode, "legacy login");
                        string rehashed = await ReadStoredHashAsync(server, userId).ConfigureAwait(false);
                        AssertStartsWith("pbkdf2-sha256$600000$", rehashed, "rehashed on login");
                        AssertFalse(rehashed.Contains(legacy), "no unsalted digest left");

                        HttpResponseMessage again = await LoginAsync(rest, "legacy@login.armada", "user-password-123").ConfigureAwait(false);
                        AssertEqual(200, (int)again.StatusCode, "login after rehash");
                        AssertEqual(rehashed, await ReadStoredHashAsync(server, userId).ConfigureAwait(false), "no second rehash");
                    }
                }
            }));

            cases.Add(CaseAsync("account_lockout_returns_429", "After MaxFailuresPerAccount failures the account gets 429 with Retry-After, even with the right password", TestTags.Negative, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", AdminPassword).ConfigureAwait(false))
                {
                    server.Settings.LoginRateLimit.MaxFailuresPerAccount = 3;
                    server.Settings.LoginRateLimit.LockoutMinutes = 1;
                    await server.StartAsync().ConfigureAwait(false);
                    using (HttpClient admin = server.CreateRestClient(true))
                    using (HttpClient rest = server.CreateRestClient(false))
                    {
                        await CreateUserAsync(admin, "bystander@login.armada", "bystander-password").ConfigureAwait(false);

                        for (int i = 0; i < 3; i++)
                        {
                            HttpResponseMessage wrong = await LoginAsync(rest, Constants.DefaultUserEmail, "wrong-" + i).ConfigureAwait(false);
                            AssertEqual(401, (int)wrong.StatusCode, "failure " + i);
                        }

                        HttpResponseMessage locked = await LoginAsync(rest, Constants.DefaultUserEmail, AdminPassword).ConfigureAwait(false);
                        AssertEqual(429, (int)locked.StatusCode, "right password while locked");
                        int retryAfter = RetryAfterSeconds(locked);
                        AssertTrue(retryAfter >= 1 && retryAfter <= 60, "Retry-After " + retryAfter);
                        string body = await locked.Content.ReadAsStringAsync().ConfigureAwait(false);
                        AssertContains("SlowDown", body);

                        HttpResponseMessage other = await LoginAsync(rest, "bystander@login.armada", "bystander-password").ConfigureAwait(false);
                        AssertEqual(200, (int)other.StatusCode, "another account is not locked");
                    }
                }
            }));

            cases.Add(CaseAsync("success_resets_account_counter", "A successful login resets the account's failure counter", TestTags.Positive, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", AdminPassword).ConfigureAwait(false))
                {
                    server.Settings.LoginRateLimit.MaxFailuresPerAccount = 3;
                    await server.StartAsync().ConfigureAwait(false);
                    using (HttpClient rest = server.CreateRestClient(false))
                    {
                        for (int round = 0; round < 3; round++)
                        {
                            AssertEqual(401, (int)(await LoginAsync(rest, Constants.DefaultUserEmail, "wrong-a").ConfigureAwait(false)).StatusCode);
                            AssertEqual(401, (int)(await LoginAsync(rest, Constants.DefaultUserEmail, "wrong-b").ConfigureAwait(false)).StatusCode);
                            AssertEqual(200, (int)(await LoginAsync(rest, Constants.DefaultUserEmail, AdminPassword).ConfigureAwait(false)).StatusCode, "round " + round);
                        }
                    }
                }
            }));

            cases.Add(CaseAsync("address_lockout_blocks_guessable_credentials", "Failed bearer tokens lock the address on REST and MCP; session tokens keep working", TestTags.Negative, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", AdminPassword).ConfigureAwait(false))
                {
                    server.Settings.LoginRateLimit.MaxFailuresPerAddress = 3;
                    server.Settings.LoginRateLimit.MaxFailuresPerAccount = 100;
                    await server.StartAsync().ConfigureAwait(false);
                    using (HttpClient rest = server.CreateRestClient(false))
                    using (HttpClient keyed = server.CreateRestClient(true))
                    using (HttpClient mcp = server.CreateMcpClient(true))
                    {
                        HttpResponseMessage login = await LoginAsync(rest, Constants.DefaultUserEmail, AdminPassword).ConfigureAwait(false);
                        AssertEqual(200, (int)login.StatusCode);
                        AuthenticateResult session = JsonHelper.Deserialize<AuthenticateResult>(await login.Content.ReadAsStringAsync().ConfigureAwait(false));

                        for (int i = 0; i < 3; i++)
                        {
                            HttpRequestMessage guess = new HttpRequestMessage(HttpMethod.Get, "/api/v1/fleets");
                            guess.Headers.TryAddWithoutValidation("Authorization", "Bearer guess-" + i);
                            HttpResponseMessage refused = await rest.SendAsync(guess).ConfigureAwait(false);
                            AssertEqual(401, (int)refused.StatusCode, "guess " + i);
                        }

                        HttpResponseMessage keyedLocked = await keyed.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                        AssertEqual(429, (int)keyedLocked.StatusCode, "valid API key from a locked address");
                        AssertTrue(RetryAfterSeconds(keyedLocked) > 0, "Retry-After on REST");

                        HttpResponseMessage mcpLocked = await InitializeMcpAsync(mcp).ConfigureAwait(false);
                        AssertEqual(429, (int)mcpLocked.StatusCode, "valid API key on MCP from a locked address");
                        AssertTrue(RetryAfterSeconds(mcpLocked) > 0, "Retry-After on MCP");

                        HttpResponseMessage passwordLocked = await LoginAsync(rest, Constants.DefaultUserEmail, AdminPassword).ConfigureAwait(false);
                        AssertEqual(429, (int)passwordLocked.StatusCode, "password login from a locked address");

                        HttpRequestMessage withSession = new HttpRequestMessage(HttpMethod.Get, "/api/v1/whoami");
                        withSession.Headers.Add("X-Token", session.Token!);
                        HttpResponseMessage sessionOk = await rest.SendAsync(withSession).ConfigureAwait(false);
                        AssertEqual(200, (int)sessionOk.StatusCode, "existing session token still works");
                    }
                }
            }));

            cases.Add(CaseAsync("tenant_lookup_budget", "Tenant lookups beyond the per-address budget get 429", TestTags.Negative, async () =>
            {
                using (SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", AdminPassword).ConfigureAwait(false))
                {
                    server.Settings.LoginRateLimit.MaxFailuresPerAddress = 3;
                    await server.StartAsync().ConfigureAwait(false);
                    using (HttpClient rest = server.CreateRestClient(false))
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            HttpResponseMessage ok = await rest.PostAsync("/api/v1/tenants/lookup", JsonHelper.ToJsonContent(new { Email = Constants.DefaultUserEmail })).ConfigureAwait(false);
                            AssertEqual(200, (int)ok.StatusCode, "lookup " + i);
                        }

                        HttpResponseMessage limited = await rest.PostAsync("/api/v1/tenants/lookup", JsonHelper.ToJsonContent(new { Email = Constants.DefaultUserEmail })).ConfigureAwait(false);
                        AssertEqual(429, (int)limited.StatusCode);
                        AssertTrue(RetryAfterSeconds(limited) > 0);

                        HttpResponseMessage login = await LoginAsync(rest, Constants.DefaultUserEmail, AdminPassword).ConfigureAwait(false);
                        AssertEqual(200, (int)login.StatusCode, "lookups do not lock password login");
                    }
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Login Security",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<HttpResponseMessage> LoginAsync(HttpClient rest, string email, string password)
        {
            return await rest.PostAsync("/api/v1/authenticate", JsonHelper.ToJsonContent(new
            {
                TenantId = Constants.DefaultTenantId,
                Email = email,
                Password = password
            })).ConfigureAwait(false);
        }

        private static async Task<string> CreateUserAsync(HttpClient admin, string email, string password)
        {
            HttpResponseMessage response = await admin.PostAsync("/api/v1/users", JsonHelper.ToJsonContent(new
            {
                TenantId = Constants.DefaultTenantId,
                Email = email,
                PasswordSha256 = UserMaster.ComputePasswordHash(password),
                IsTenantAdmin = false
            })).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertTrue(response.IsSuccessStatusCode, "create user: " + (int)response.StatusCode + " " + body);
            UserMaster user = JsonHelper.Deserialize<UserMaster>(body);
            AssertEqual("********", user.PasswordSha256, "hash never returned");
            return user.Id;
        }

        private static async Task<string> ReadStoredHashAsync(SecurityTestServer server, string userId)
        {
            using (SqliteConnection conn = new SqliteConnection("Data Source=" + server.Settings.Database.Filename + ";Pooling=False"))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT password_sha256 FROM users WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", userId);
                    object? value = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                    return value == null || value == DBNull.Value ? String.Empty : value.ToString()!;
                }
            }
        }

        private static async Task WriteStoredHashAsync(SecurityTestServer server, string userId, string value)
        {
            using (SqliteConnection conn = new SqliteConnection("Data Source=" + server.Settings.Database.Filename + ";Pooling=False"))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE users SET password_sha256 = @value WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@value", value);
                    cmd.Parameters.AddWithValue("@id", userId);
                    int rows = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    AssertEqual(1, rows, "user row updated");
                }
            }
        }

        private static int RetryAfterSeconds(HttpResponseMessage response)
        {
            if (response.Headers.RetryAfter != null && response.Headers.RetryAfter.Delta.HasValue)
                return (int)response.Headers.RetryAfter.Delta.Value.TotalSeconds;
            if (response.Headers.TryGetValues("Retry-After", out IEnumerable<string>? values) && Int32.TryParse(values.First(), out int seconds))
                return seconds;
            return 0;
        }

        private static async Task<HttpResponseMessage> InitializeMcpAsync(HttpClient mcp)
        {
            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, "/rpc");
            message.Content = JsonHelper.ToJsonContent(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new { protocolVersion = "2024-11-05", capabilities = new { }, clientInfo = new { name = "login-security-test", version = "1.0" } }
            });
            return await mcp.SendAsync(message).ConfigureAwait(false);
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
