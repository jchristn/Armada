namespace Armada.Test.Unit.Suites.Models
{
    using Armada.Core.Models;
    using Armada.Test.Common;

    public class AuthContextTests : TestSuite
    {
        public override string Name => "AuthContext Model";

        protected override async Task RunTestsAsync()
        {
            await RunTest("AuthContext DefaultConstructor IsUnauthenticated", () =>
            {
                AuthContext ctx = new AuthContext();
                AssertFalse(ctx.IsAuthenticated);
                AssertNull(ctx.TenantId);
                AssertNull(ctx.UserId);
                AssertFalse(ctx.IsAdmin);
                AssertFalse(ctx.IsTenantAdmin);
                AssertNull(ctx.AuthMethod);
            });

            await RunTest("AuthContext Authenticated FactoryMethod SetsAllProperties", () =>
            {
                AuthContext ctx = AuthContext.Authenticated("ten_abc", "usr_xyz", true, true, "Bearer");
                AssertTrue(ctx.IsAuthenticated);
                AssertEqual("ten_abc", ctx.TenantId);
                AssertEqual("usr_xyz", ctx.UserId);
                AssertTrue(ctx.IsAdmin);
                AssertTrue(ctx.IsTenantAdmin);
                AssertEqual("Bearer", ctx.AuthMethod);
            });

            await RunTest("AuthContext Authenticated NonAdmin", () =>
            {
                AuthContext ctx = AuthContext.Authenticated("ten_abc", "usr_xyz", false, false, "Session");
                AssertTrue(ctx.IsAuthenticated);
                AssertFalse(ctx.IsAdmin);
                AssertFalse(ctx.IsTenantAdmin);
                AssertEqual("Session", ctx.AuthMethod);
            });

            await RunTest("AuthContext Authenticated DifferentAuthMethods", () =>
            {
                AuthContext bearer = AuthContext.Authenticated("ten_1", "usr_1", false, false, "Bearer");
                AuthContext session = AuthContext.Authenticated("ten_1", "usr_1", false, false, "Session");
                AuthContext apiKey = AuthContext.Authenticated("ten_1", "usr_1", false, false, "ApiKey");

                AssertEqual("Bearer", bearer.AuthMethod);
                AssertEqual("Session", session.AuthMethod);
                AssertEqual("ApiKey", apiKey.AuthMethod);
            });

            await RunTest("AuthContext Properties AreSettable", () =>
            {
                AuthContext ctx = new AuthContext();
                ctx.IsAuthenticated = true;
                ctx.TenantId = "ten_test";
                ctx.UserId = "usr_test";
                ctx.IsAdmin = true;
                ctx.IsTenantAdmin = true;
                ctx.AuthMethod = "Custom";

                AssertTrue(ctx.IsAuthenticated);
                AssertEqual("ten_test", ctx.TenantId);
                AssertEqual("usr_test", ctx.UserId);
                AssertTrue(ctx.IsAdmin);
                AssertTrue(ctx.IsTenantAdmin);
                AssertEqual("Custom", ctx.AuthMethod);
            });
        }
    }
}
