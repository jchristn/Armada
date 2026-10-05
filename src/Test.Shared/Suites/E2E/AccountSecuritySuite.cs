namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// O-06: a new tenant's seeded admin (admin@armada) never gets the well-known default password; the creator supplies
    /// one or the server generates a random one returned once. O-17: a user changing their own password through
    /// <c>PUT /api/v1/users/{id}</c> must present the current password; an administrator setting another user's password
    /// does not.
    /// </summary>
    public sealed class AccountSecuritySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.AccountSecurity";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("new_tenant_admin_gets_generated_password", "A new tenant's seeded admin gets a random password returned once, not the default", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage created = await fx.AuthClient.PostAsync("/api/v1/tenants", JsonHelper.ToJsonContent(new { Name = "o06-" + Guid.NewGuid().ToString("N").Substring(0, 8) })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, created.StatusCode, "tenant created");
                TenantCreateResult tenant = await JsonHelper.DeserializeAsync<TenantCreateResult>(created).ConfigureAwait(false);
                AssertEqual(Constants.DefaultUserEmail, tenant.AdminEmail, "the seeded admin's email is returned");
                AssertNotNull(tenant.AdminPassword, "the generated password is returned once");
                AssertTrue(tenant.AdminPassword!.Length >= 16, "the generated password is long");
                AssertFalse(String.Equals(tenant.AdminPassword, Constants.DefaultUserPassword, StringComparison.Ordinal), "not the default password");

                AssertEqual(HttpStatusCode.Unauthorized, await LoginStatusAsync(fx, tenant.Id, Constants.DefaultUserPassword).ConfigureAwait(false), "the default password does not sign in");
                AssertEqual(HttpStatusCode.OK, await LoginStatusAsync(fx, tenant.Id, tenant.AdminPassword).ConfigureAwait(false), "the generated password signs in");

                HttpResponseMessage read = await fx.AuthClient.GetAsync("/api/v1/tenants/" + tenant.Id).ConfigureAwait(false);
                TenantCreateResult reread = await JsonHelper.DeserializeAsync<TenantCreateResult>(read).ConfigureAwait(false);
                AssertNull(reread.AdminPassword, "the password is never returned again");
            }));

            cases.Add(CaseAsync("new_tenant_admin_uses_supplied_password", "A new tenant's seeded admin uses the creator's AdminPassword, which is not echoed", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                string supplied = "o06-supplied-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage created = await fx.AuthClient.PostAsync("/api/v1/tenants", JsonHelper.ToJsonContent(new { Name = "o06s-" + Guid.NewGuid().ToString("N").Substring(0, 8), AdminPassword = supplied })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, created.StatusCode, "tenant created");
                TenantCreateResult tenant = await JsonHelper.DeserializeAsync<TenantCreateResult>(created).ConfigureAwait(false);
                AssertNull(tenant.AdminPassword, "a supplied password is not echoed");
                AssertEqual(HttpStatusCode.OK, await LoginStatusAsync(fx, tenant.Id, supplied).ConfigureAwait(false), "the supplied password signs in");
            }));

            cases.Add(CaseAsync("new_tenant_rejects_weak_admin_password", "A default or short AdminPassword is rejected and no tenant is created", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                foreach (string weak in new[] { Constants.DefaultUserPassword, "short" })
                {
                    HttpResponseMessage created = await fx.AuthClient.PostAsync("/api/v1/tenants", JsonHelper.ToJsonContent(new { Name = "o06w-" + Guid.NewGuid().ToString("N").Substring(0, 8), AdminPassword = weak })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.BadRequest, created.StatusCode, "weak AdminPassword rejected");
                }
            }));

            cases.Add(CaseAsync("self_password_change_requires_current_password", "PUT /api/v1/users/{id} on your own account needs the current password", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "o17", false).ConfigureAwait(false);
                using (HttpClient self = CreateBearerClient(fx, user.BearerToken))
                {
                    UserMaster me = await JsonHelper.DeserializeAsync<UserMaster>(await self.GetAsync("/api/v1/users/" + user.UserId).ConfigureAwait(false)).ConfigureAwait(false);
                    const string NewPassword = "o17-new-password";

                    HttpResponseMessage missing = await self.PutAsync("/api/v1/users/" + user.UserId, JsonHelper.ToJsonContent(new { Email = me.Email, Password = NewPassword, Active = true })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.BadRequest, missing.StatusCode, "no CurrentPassword");

                    HttpResponseMessage wrong = await self.PutAsync("/api/v1/users/" + user.UserId, JsonHelper.ToJsonContent(new { Email = me.Email, Password = NewPassword, CurrentPassword = "not-the-password", Active = true })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Forbidden, wrong.StatusCode, "wrong CurrentPassword");
                    AssertEqual(HttpStatusCode.Unauthorized, await LoginStatusAsync(fx, user.TenantId, NewPassword, me.Email).ConfigureAwait(false), "the refused change did not take effect");

                    HttpResponseMessage profileOnly = await self.PutAsync("/api/v1/users/" + user.UserId, JsonHelper.ToJsonContent(new { Email = me.Email, FirstName = "Renamed", Active = true })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, profileOnly.StatusCode, "a profile edit without a password change needs no CurrentPassword");

                    HttpResponseMessage ok = await self.PutAsync("/api/v1/users/" + user.UserId, JsonHelper.ToJsonContent(new { Email = me.Email, Password = NewPassword, CurrentPassword = "testpass", Active = true })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, ok.StatusCode, "the right CurrentPassword allows the change");
                    AssertEqual(HttpStatusCode.OK, await LoginStatusAsync(fx, user.TenantId, NewPassword, me.Email).ConfigureAwait(false), "the new password signs in");
                }
            }));

            cases.Add(CaseAsync("admin_sets_other_users_password_without_current", "An administrator sets another user's password without that user's current password", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "o17a", false).ConfigureAwait(false);
                UserMaster target = await JsonHelper.DeserializeAsync<UserMaster>(await fx.AuthClient.GetAsync("/api/v1/users/" + user.UserId).ConfigureAwait(false)).ConfigureAwait(false);
                const string AdminSet = "o17-admin-set-password";
                HttpResponseMessage set = await fx.AuthClient.PutAsync("/api/v1/users/" + user.UserId, JsonHelper.ToJsonContent(new { Email = target.Email, Password = AdminSet, Active = true })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, set.StatusCode, "admin reset");
                AssertEqual(HttpStatusCode.OK, await LoginStatusAsync(fx, user.TenantId, AdminSet, target.Email).ConfigureAwait(false), "the admin-set password signs in");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Account Security (O-06, O-17)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<HttpStatusCode> LoginStatusAsync(E2EServerFixture fx, string tenantId, string password, string? email = null)
        {
            HttpResponseMessage response = await fx.UnauthClient.PostAsync("/api/v1/authenticate", JsonHelper.ToJsonContent(new
            {
                TenantId = tenantId,
                Email = email ?? Constants.DefaultUserEmail,
                Password = password
            })).ConfigureAwait(false);
            return response.StatusCode;
        }

        private static HttpClient CreateBearerClient(E2EServerFixture fx, string bearerToken)
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(fx.BaseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            return client;
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
