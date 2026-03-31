namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Test.Common;

    public class AuthorizationServiceTests : TestSuite
    {
        public override string Name => "AuthorizationService";

        protected override async Task RunTestsAsync()
        {
            // --- IsAuthorized tests ---

            await RunTest("IsAuthorized NoAuthRequired Endpoint Unauthenticated ReturnsTrue", () =>
            {
                AuthorizationService svc = new AuthorizationService();
                AuthContext ctx = new AuthContext();

                bool result = svc.IsAuthorized(ctx, "GET", "/api/v1/status/health");
                AssertTrue(result, "Unauthenticated request to NoAuthRequired endpoint should be authorized");
            });

            await RunTest("IsAuthorized NoAuthRequired Endpoint Authenticated ReturnsTrue", () =>
            {
                AuthorizationService svc = new AuthorizationService();
                AuthContext ctx = AuthContext.Authenticated("ten_abc", "usr_xyz", false, false, "Session");

                bool result = svc.IsAuthorized(ctx, "GET", "/api/v1/status/health");
                AssertTrue(result, "Authenticated request to NoAuthRequired endpoint should be authorized");
            });

            await RunTest("IsAuthorized Authenticated Endpoint Authenticated ReturnsTrue", () =>
            {
                AuthorizationService svc = new AuthorizationService();
                AuthContext ctx = AuthContext.Authenticated("ten_abc", "usr_xyz", false, false, "Session");

                bool result = svc.IsAuthorized(ctx, "GET", "/api/v1/fleets");
                AssertTrue(result, "Authenticated request to Authenticated endpoint should be authorized");
            });

            await RunTest("IsAuthorized Authenticated Endpoint Unauthenticated ReturnsFalse", () =>
            {
                AuthorizationService svc = new AuthorizationService();
                AuthContext ctx = new AuthContext();

                bool result = svc.IsAuthorized(ctx, "GET", "/api/v1/fleets");
                AssertFalse(result, "Unauthenticated request to Authenticated endpoint should not be authorized");
            });

            await RunTest("IsAuthorized AdminOnly Endpoint Admin ReturnsTrue", () =>
            {
                AuthorizationService svc = new AuthorizationService();
                AuthContext ctx = AuthContext.Authenticated("ten_abc", "usr_xyz", true, true, "Session");

                bool result = svc.IsAuthorized(ctx, "POST", "/api/v1/tenants");
                AssertTrue(result, "Admin request to AdminOnly endpoint should be authorized");
            });

            await RunTest("IsAuthorized AdminOnly Endpoint NonAdmin ReturnsFalse", () =>
            {
                AuthorizationService svc = new AuthorizationService();
                AuthContext ctx = AuthContext.Authenticated("ten_abc", "usr_xyz", false, false, "Session");

                bool result = svc.IsAuthorized(ctx, "POST", "/api/v1/tenants");
                AssertFalse(result, "Non-admin request to AdminOnly endpoint should not be authorized");
            });

            await RunTest("IsAuthorized AdminOnly Endpoint Unauthenticated ReturnsFalse", () =>
            {
                AuthorizationService svc = new AuthorizationService();
                AuthContext ctx = new AuthContext();

                bool result = svc.IsAuthorized(ctx, "POST", "/api/v1/tenants");
                AssertFalse(result, "Unauthenticated request to AdminOnly endpoint should not be authorized");
            });

            // --- RequireAuth tests ---

            await RunTest("RequireAuth Authenticated DoesNotThrow", () =>
            {
                AuthorizationService svc = new AuthorizationService();
                AuthContext ctx = AuthContext.Authenticated("ten_abc", "usr_xyz", false, false, "Session");

                svc.RequireAuth(ctx);
            });

            await RunTest("RequireAuth Unauthenticated ThrowsUnauthorizedAccessException", () =>
            {
                AuthorizationService svc = new AuthorizationService();
                AuthContext ctx = new AuthContext();

                AssertThrows<UnauthorizedAccessException>(() => svc.RequireAuth(ctx));
            });

            // --- RequireAdmin tests ---

            await RunTest("RequireAdmin Admin DoesNotThrow", () =>
            {
                AuthorizationService svc = new AuthorizationService();
                AuthContext ctx = AuthContext.Authenticated("ten_abc", "usr_xyz", true, true, "Session");

                svc.RequireAdmin(ctx);
            });

            await RunTest("RequireAdmin NonAdmin ThrowsUnauthorizedAccessException", () =>
            {
                AuthorizationService svc = new AuthorizationService();
                AuthContext ctx = AuthContext.Authenticated("ten_abc", "usr_xyz", false, false, "Session");

                AssertThrows<UnauthorizedAccessException>(() => svc.RequireAdmin(ctx));
            });

            await RunTest("RequireAdmin Unauthenticated ThrowsUnauthorizedAccessException", () =>
            {
                AuthorizationService svc = new AuthorizationService();
                AuthContext ctx = new AuthContext();

                AssertThrows<UnauthorizedAccessException>(() => svc.RequireAdmin(ctx));
            });

            await RunTest("IsAuthorized TenantAdmin Endpoint TenantAdmin ReturnsTrue", () =>
            {
                AuthorizationService svc = new AuthorizationService();
                AuthContext ctx = AuthContext.Authenticated("ten_abc", "usr_xyz", false, true, "Session");

                bool result = svc.IsAuthorized(ctx, "POST", "/api/v1/fleets");
                AssertTrue(result, "Tenant admin request to TenantAdmin endpoint should be authorized");
            });

            await RunTest("IsAuthorized TenantAdmin Endpoint RegularUser ReturnsFalse", () =>
            {
                AuthorizationService svc = new AuthorizationService();
                AuthContext ctx = AuthContext.Authenticated("ten_abc", "usr_xyz", false, false, "Session");

                bool result = svc.IsAuthorized(ctx, "POST", "/api/v1/fleets");
                AssertFalse(result, "Regular user request to TenantAdmin endpoint should not be authorized");
            });

            await RunTest("RequireTenantAdmin TenantAdmin DoesNotThrow", () =>
            {
                AuthorizationService svc = new AuthorizationService();
                AuthContext ctx = AuthContext.Authenticated("ten_abc", "usr_xyz", false, true, "Session");

                svc.RequireTenantAdmin(ctx);
            });
        }
    }
}
