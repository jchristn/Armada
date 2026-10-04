namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Authorization;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="AuthorizationConfig"/>: the static endpoint-to-permission
    /// matrix. Positive cases assert the expected <see cref="PermissionLevel"/> for each
    /// documented route (NoAuthRequired, AdminOnly, TenantAdmin, Authenticated) plus method
    /// and path case-insensitivity. Negative cases cover the default fallback and method-gated
    /// routes that legacy coverage skipped.
    /// </summary>
    public sealed class AuthorizationConfigSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the AuthorizationConfig suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // --- NoAuthRequired endpoints ---

            cases.Add(Case("health_endpoint_is_no_auth_required", "HealthEndpoint IsNoAuthRequired", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/status/health");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            }));

            cases.Add(Case("authenticate_post_is_no_auth_required", "Authenticate POST IsNoAuthRequired", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/authenticate");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            }));

            cases.Add(Case("tenants_lookup_post_is_no_auth_required", "TenantsLookup POST IsNoAuthRequired", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/tenants/lookup");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            }));

            cases.Add(Case("onboarding_post_is_no_auth_required", "Onboarding POST IsNoAuthRequired", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/onboarding");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            }));

            cases.Add(Case("dashboard_is_no_auth_required", "Dashboard IsNoAuthRequired", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/dashboard");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            }));

            cases.Add(Case("dashboard_subpath_is_no_auth_required", "DashboardSubpath IsNoAuthRequired", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/dashboard/missions");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            }));

            cases.Add(Case("root_is_no_auth_required", "Root IsNoAuthRequired", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            }));

            // --- AdminOnly endpoints ---

            cases.Add(Case("tenants_get_is_admin_only", "Tenants GET IsAdminOnly", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/tenants");
                AssertEqual(PermissionLevel.AdminOnly, level);
            }));

            cases.Add(Case("tenants_post_is_admin_only", "Tenants POST IsAdminOnly", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/tenants");
                AssertEqual(PermissionLevel.AdminOnly, level);
            }));

            cases.Add(Case("tenant_put_is_admin_only", "Tenant PUT IsAdminOnly", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("PUT", "/api/v1/tenants/ten_abc");
                AssertEqual(PermissionLevel.AdminOnly, level);
            }));

            cases.Add(Case("tenant_delete_is_admin_only", "Tenant DELETE IsAdminOnly", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("DELETE", "/api/v1/tenants/ten_abc");
                AssertEqual(PermissionLevel.AdminOnly, level);
            }));

            cases.Add(Case("users_get_is_authenticated", "Users GET IsAuthenticated", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/users");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            cases.Add(Case("users_post_is_tenant_admin", "Users POST IsTenantAdmin", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/users");
                AssertEqual(PermissionLevel.TenantAdmin, level);
            }));

            cases.Add(Case("user_put_is_authenticated", "User PUT IsAuthenticated", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("PUT", "/api/v1/users/usr_abc");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            cases.Add(Case("user_delete_is_authenticated", "User DELETE IsAuthenticated", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("DELETE", "/api/v1/users/usr_abc");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            cases.Add(Case("credential_put_is_authenticated", "Credential PUT IsAuthenticated", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("PUT", "/api/v1/credentials/crd_abc");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            // --- Authenticated endpoints (everything else) ---

            cases.Add(Case("fleets_get_is_authenticated", "Fleets GET IsAuthenticated", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/fleets");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            cases.Add(Case("missions_get_is_authenticated", "Missions GET IsAuthenticated", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/missions");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            cases.Add(Case("planning_sessions_get_is_authenticated", "PlanningSessions GET IsAuthenticated", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/planning-sessions");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            cases.Add(Case("captains_get_is_authenticated", "Captains GET IsAuthenticated", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/captains");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            cases.Add(Case("vessels_get_is_authenticated", "Vessels GET IsAuthenticated", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/vessels");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            cases.Add(Case("credentials_get_is_authenticated", "Credentials GET IsAuthenticated", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/credentials");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            cases.Add(Case("credentials_post_is_authenticated", "Credentials POST IsAuthenticated", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/credentials");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            cases.Add(Case("credential_delete_is_authenticated", "Credential DELETE IsAuthenticated", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("DELETE", "/api/v1/credentials/crd_abc");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            cases.Add(Case("fleets_post_is_tenant_admin", "Fleets POST IsTenantAdmin", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/fleets");
                AssertEqual(PermissionLevel.TenantAdmin, level);
            }));

            cases.Add(Case("planning_sessions_post_is_tenant_admin", "PlanningSessions POST IsTenantAdmin", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/planning-sessions");
                AssertEqual(PermissionLevel.TenantAdmin, level);
            }));

            cases.Add(Case("request_history_get_is_authenticated", "RequestHistory GET IsAuthenticated", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/request-history");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            cases.Add(Case("request_history_delete_is_tenant_admin", "RequestHistory DELETE IsTenantAdmin", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("DELETE", "/api/v1/request-history/req_abc");
                AssertEqual(PermissionLevel.TenantAdmin, level);
            }));

            cases.Add(Case("releases_get_is_authenticated", "Releases GET IsAuthenticated", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/releases");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            cases.Add(Case("releases_post_is_tenant_admin", "Releases POST IsTenantAdmin", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/releases");
                AssertEqual(PermissionLevel.TenantAdmin, level);
            }));

            cases.Add(Case("environments_get_is_authenticated", "Environments GET IsAuthenticated", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/environments");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            cases.Add(Case("environments_post_is_tenant_admin", "Environments POST IsTenantAdmin", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/environments");
                AssertEqual(PermissionLevel.TenantAdmin, level);
            }));

            cases.Add(Case("server_post_is_admin_only", "Server POST IsAdminOnly", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/server/stop");
                AssertEqual(PermissionLevel.AdminOnly, level);
            }));

            // --- Case insensitivity ---

            cases.Add(Case("method_case_insensitive", "MethodCaseInsensitive", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("get", "/api/v1/status/health");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            }));

            cases.Add(Case("path_case_insensitive", "PathCaseInsensitive", TestTags.Positive, () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/API/V1/Status/Health");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            }));

            // --- Audit additions: default fallback and method-gated boundaries (confirmed against source) ---

            cases.Add(Case("unmapped_path_fails_closed", "Unmapped path fails closed to AdminOnly", TestTags.Negative, () =>
            {
                // No route declares an unknown path, so it requires a global admin (fail closed).
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/unmapped-resource");
                AssertEqual(PermissionLevel.AdminOnly, level);
            }));

            cases.Add(Case("tenant_detail_get_is_authenticated_not_admin", "Tenant detail GET is Authenticated not AdminOnly", TestTags.Negative, () =>
            {
                // The tenant-id pattern only elevates PUT/DELETE to AdminOnly; a GET on a tenant
                // detail route is not admin-gated and falls through to Authenticated.
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/tenants/ten_abc");
                AssertEqual(PermissionLevel.Authenticated, level);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.AuthorizationConfig",
                displayName: "AuthorizationConfig",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.AuthorizationConfig",
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
                suiteId: "Services.AuthorizationConfig",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
