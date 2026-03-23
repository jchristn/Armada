namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Core.Authorization;
    using Armada.Test.Common;

    public class AuthorizationConfigTests : TestSuite
    {
        public override string Name => "AuthorizationConfig";

        protected override async Task RunTestsAsync()
        {
            // --- NoAuthRequired endpoints ---

            await RunTest("HealthEndpoint IsNoAuthRequired", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/status/health");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            });

            await RunTest("Authenticate POST IsNoAuthRequired", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/authenticate");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            });

            await RunTest("TenantsLookup POST IsNoAuthRequired", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/tenants/lookup");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            });

            await RunTest("Onboarding POST IsNoAuthRequired", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/onboarding");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            });

            await RunTest("Dashboard IsNoAuthRequired", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/dashboard");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            });

            await RunTest("DashboardSubpath IsNoAuthRequired", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/dashboard/missions");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            });

            await RunTest("Root IsNoAuthRequired", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            });

            // --- AdminOnly endpoints ---

            await RunTest("Tenants GET IsAdminOnly", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/tenants");
                AssertEqual(PermissionLevel.AdminOnly, level);
            });

            await RunTest("Tenants POST IsAdminOnly", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/tenants");
                AssertEqual(PermissionLevel.AdminOnly, level);
            });

            await RunTest("Tenant PUT IsAdminOnly", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("PUT", "/api/v1/tenants/ten_abc");
                AssertEqual(PermissionLevel.AdminOnly, level);
            });

            await RunTest("Tenant DELETE IsAdminOnly", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("DELETE", "/api/v1/tenants/ten_abc");
                AssertEqual(PermissionLevel.AdminOnly, level);
            });

            await RunTest("Users GET IsAuthenticated", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/users");
                AssertEqual(PermissionLevel.Authenticated, level);
            });

            await RunTest("Users POST IsTenantAdmin", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/users");
                AssertEqual(PermissionLevel.TenantAdmin, level);
            });

            await RunTest("User PUT IsAuthenticated", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("PUT", "/api/v1/users/usr_abc");
                AssertEqual(PermissionLevel.Authenticated, level);
            });

            await RunTest("User DELETE IsAuthenticated", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("DELETE", "/api/v1/users/usr_abc");
                AssertEqual(PermissionLevel.Authenticated, level);
            });

            await RunTest("Credential PUT IsAuthenticated", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("PUT", "/api/v1/credentials/crd_abc");
                AssertEqual(PermissionLevel.Authenticated, level);
            });

            // --- Authenticated endpoints (everything else) ---

            await RunTest("Fleets GET IsAuthenticated", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/fleets");
                AssertEqual(PermissionLevel.Authenticated, level);
            });

            await RunTest("Missions GET IsAuthenticated", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/missions");
                AssertEqual(PermissionLevel.Authenticated, level);
            });

            await RunTest("Captains GET IsAuthenticated", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/captains");
                AssertEqual(PermissionLevel.Authenticated, level);
            });

            await RunTest("Vessels GET IsAuthenticated", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/vessels");
                AssertEqual(PermissionLevel.Authenticated, level);
            });

            await RunTest("Credentials GET IsAuthenticated", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/credentials");
                AssertEqual(PermissionLevel.Authenticated, level);
            });

            await RunTest("Credentials POST IsAuthenticated", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/credentials");
                AssertEqual(PermissionLevel.Authenticated, level);
            });

            await RunTest("Credential DELETE IsAuthenticated", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("DELETE", "/api/v1/credentials/crd_abc");
                AssertEqual(PermissionLevel.Authenticated, level);
            });

            await RunTest("Fleets POST IsTenantAdmin", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/fleets");
                AssertEqual(PermissionLevel.TenantAdmin, level);
            });

            await RunTest("Server POST IsAdminOnly", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/server/stop");
                AssertEqual(PermissionLevel.AdminOnly, level);
            });

            // --- Case insensitivity ---

            await RunTest("MethodCaseInsensitive", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("get", "/api/v1/status/health");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            });

            await RunTest("PathCaseInsensitive", () =>
            {
                PermissionLevel level = AuthorizationConfig.GetPermissionLevel("GET", "/API/V1/Status/Health");
                AssertEqual(PermissionLevel.NoAuthRequired, level);
            });
        }
    }
}
