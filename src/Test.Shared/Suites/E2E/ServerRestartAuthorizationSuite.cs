namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Hosting;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Restarting the Admiral (<c>POST /api/v1/server/restart</c>, what Harbor's Restart Admiral sends) requires a global
    /// administrator: an ordinary user and a tenant administrator get 403 and nothing restarts; the local API key and a
    /// global administrator's credential succeed. The restart itself is stubbed through
    /// <see cref="Armada.Server.ArmadaServer.RestartOverride"/>, so the shared server never restarts. Also covers how
    /// Harbor decides up front, from <c>GET /api/v1/whoami</c>, whether its credential may restart the Admiral.
    /// </summary>
    public sealed class ServerRestartAuthorizationSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.ServerRestartAuthorization";
        private static readonly SemaphoreSlim _OverrideLock = new SemaphoreSlim(1, 1);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("non_admin_forbidden", "An ordinary user and a tenant admin get 403 and the restart never runs", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser tenantAdmin = await E2ETenantUser.CreateAsync(fx.AuthClient, "rst-tadmin", true).ConfigureAwait(false);
                E2ETenantUser member = await E2ETenantUser.CreateAsync(fx.AuthClient, "rst-member", false, tenantAdmin.TenantId).ConfigureAwait(false);

                int calls = await WithStubbedRestartAsync(fx, async () =>
                {
                    foreach (E2ETenantUser user in new E2ETenantUser[] { tenantAdmin, member })
                    {
                        using (HttpClient client = user.CreateClient(fx.BaseUrl))
                        {
                            HttpResponseMessage response = await client.PostAsync("/api/v1/server/restart", null).ConfigureAwait(false);
                            AssertStatusCode(HttpStatusCode.Forbidden, response, user.UserId);
                            E2eCodedErrorBody error = JsonHelper.Deserialize<E2eCodedErrorBody>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                            AssertEqual(WatsonWebserver.Core.ApiResultEnum.Forbidden, error.Error);
                        }
                    }
                }).ConfigureAwait(false);

                AssertEqual(0, calls, "a refused request never reaches the restart");
                HttpResponseMessage health = await fx.AuthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, health, "the server is still running");
            }));

            cases.Add(CaseAsync("unauthenticated_refused", "No credential gets 401 and the restart never runs", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                int calls = await WithStubbedRestartAsync(fx, async () =>
                {
                    HttpResponseMessage response = await fx.UnauthClient.PostAsync("/api/v1/server/restart", null).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.Unauthorized, response);
                }).ConfigureAwait(false);
                AssertEqual(0, calls);
            }));

            cases.Add(CaseAsync("admin_succeeds", "The local API key and a global admin's credential restart (stubbed)", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                string adminToken = await CreateGlobalAdminCredentialAsync(fx).ConfigureAwait(false);

                int calls = await WithStubbedRestartAsync(fx, async () =>
                {
                    HttpResponseMessage byKey = await fx.AuthClient.PostAsync("/api/v1/server/restart", null).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, byKey, "API key");
                    AssertEqual("restarting", (await JsonHelper.DeserializeAsync<E2eServerControlResult>(byKey).ConfigureAwait(false)).Status);

                    using (HttpClient admin = new HttpClient { BaseAddress = new Uri(fx.BaseUrl) })
                    {
                        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
                        HttpResponseMessage byCredential = await admin.PostAsync("/api/v1/server/restart", null).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.OK, byCredential, "global admin credential");
                    }
                }).ConfigureAwait(false);

                AssertEqual(2, calls, "each admin request ran the (stubbed) restart once");
            }));

            cases.Add(CaseAsync("failed_launch_is_500", "When the restart cannot start, an admin gets 500 and the server keeps running", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                await _OverrideLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    fx.Server.RestartOverride = () => Task.FromResult(false);
                    HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/server/restart", null).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.InternalServerError, response);
                }
                finally
                {
                    fx.Server.RestartOverride = null;
                    _OverrideLock.Release();
                }
            }));

            cases.Add(CaseAsync("whoami_reports_admin", "Harbor's up-front check: whoami says whether the credential is a global admin", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage byKey = await fx.AuthClient.GetAsync("/api/v1/whoami").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, byKey);
                AdminPrivilegeStatus keyStatus = AdminPrivilegeCheck.FromWhoAmI(await JsonHelper.DeserializeAsync<WhoAmIResult>(byKey).ConfigureAwait(false));
                AssertEqual(AdminPrivilegeStateEnum.Admin, keyStatus.State, "the local API key is the system administrator");

                E2ETenantUser tenantAdmin = await E2ETenantUser.CreateAsync(fx.AuthClient, "rst-who", true).ConfigureAwait(false);
                using (HttpClient client = tenantAdmin.CreateClient(fx.BaseUrl))
                {
                    HttpResponseMessage response = await client.GetAsync("/api/v1/whoami").ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, response);
                    AdminPrivilegeStatus status = AdminPrivilegeCheck.FromWhoAmI(await JsonHelper.DeserializeAsync<WhoAmIResult>(response).ConfigureAwait(false));
                    AssertEqual(AdminPrivilegeStateEnum.NotAdmin, status.State, "a tenant admin is not a global admin");
                    AssertFalse(status.IsAdmin);
                    AssertNotNull(status.Principal);
                }
            }));

            cases.Add(Case("privilege_check_states", "The privilege check maps whoami and errors to states, and only Admin allows", TestTags.Positive, () =>
            {
                AssertEqual(AdminPrivilegeStateEnum.Unknown, AdminPrivilegeCheck.FromWhoAmI(null).State);
                AssertEqual(AdminPrivilegeStateEnum.Unknown, AdminPrivilegeCheck.FromWhoAmI(new WhoAmIResult()).State);
                AssertEqual(AdminPrivilegeStateEnum.Admin, AdminPrivilegeCheck.FromWhoAmI(new WhoAmIResult { User = new UserMaster { Email = "a@x", IsAdmin = true } }).State);
                AdminPrivilegeStatus tenant = AdminPrivilegeCheck.FromWhoAmI(new WhoAmIResult { User = new UserMaster { Email = "t@x", IsAdmin = false, IsTenantAdmin = true } });
                AssertEqual(AdminPrivilegeStateEnum.NotAdmin, tenant.State);
                AssertEqual("t@x", tenant.Principal);
                AssertEqual(AdminPrivilegeStateEnum.Unauthenticated, AdminPrivilegeCheck.FromError(401, "no").State);
                AssertEqual(AdminPrivilegeStateEnum.Unauthenticated, AdminPrivilegeCheck.FromError(403, null).State);
                AssertEqual(AdminPrivilegeStateEnum.Unreachable, AdminPrivilegeCheck.FromError(0, "refused").State);
                AssertEqual(AdminPrivilegeStateEnum.Unknown, AdminPrivilegeCheck.FromError(500, null).State);
                foreach (AdminPrivilegeStateEnum state in Enum.GetValues<AdminPrivilegeStateEnum>())
                    AssertEqual(state == AdminPrivilegeStateEnum.Admin, new AdminPrivilegeStatus { State = state }.IsAdmin, state.ToString());
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Server Restart Authorization",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<int> WithStubbedRestartAsync(E2EServerFixture fx, Func<Task> body)
        {
            int calls = 0;
            await _OverrideLock.WaitAsync().ConfigureAwait(false);
            try
            {
                fx.Server.RestartOverride = () =>
                {
                    Interlocked.Increment(ref calls);
                    return Task.FromResult(true);
                };
                await body().ConfigureAwait(false);
            }
            finally
            {
                fx.Server.RestartOverride = null;
                _OverrideLock.Release();
            }

            return calls;
        }

        private static async Task<string> CreateGlobalAdminCredentialAsync(E2EServerFixture fx)
        {
            HttpResponseMessage userResp = await fx.AuthClient.PostAsync("/api/v1/users", JsonHelper.ToJsonContent(new
            {
                TenantId = Armada.Core.Constants.SystemTenantId,
                Email = "rst-admin-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@ws.armada",
                PasswordSha256 = UserMaster.ComputePasswordHash("testpass"),
                IsAdmin = true,
                IsTenantAdmin = true
            })).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.Created, userResp, "create a global admin");
            UserMaster user = await JsonHelper.DeserializeAsync<UserMaster>(userResp).ConfigureAwait(false);
            AssertTrue(user.IsAdmin, "the new user is a global admin");

            HttpResponseMessage credResp = await fx.AuthClient.PostAsync("/api/v1/credentials", JsonHelper.ToJsonContent(new
            {
                TenantId = Armada.Core.Constants.SystemTenantId,
                UserId = user.Id,
                Name = "rst-admin-cred"
            })).ConfigureAwait(false);
            credResp.EnsureSuccessStatusCode();
            Credential credential = await JsonHelper.DeserializeAsync<Credential>(credResp).ConfigureAwait(false);
            return credential.BearerToken;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
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
    }
}
