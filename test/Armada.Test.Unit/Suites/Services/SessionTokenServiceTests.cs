namespace Armada.Test.Unit.Suites.Services
{
    using System.Security.Cryptography;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Test.Common;

    public class SessionTokenServiceTests : TestSuite
    {
        public override string Name => "SessionTokenService";

        protected override async Task RunTestsAsync()
        {
            await RunTest("CreateToken ReturnsSuccessWithToken", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                AuthenticateResult result = svc.CreateToken("ten_abc", "usr_xyz");

                AssertTrue(result.Success);
                AssertNotNull(result.Token);
                AssertTrue(result.Token!.Length > 0, "Token should not be empty");
                AssertNotNull(result.ExpiresUtc);
            });

            await RunTest("CreateToken ExpiresUtc IsInFuture", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                AuthenticateResult result = svc.CreateToken("ten_abc", "usr_xyz");

                AssertTrue(result.ExpiresUtc > DateTime.UtcNow, "ExpiresUtc should be in the future");
                // Should be approximately SessionTokenLifetimeHours from now
                double hoursUntilExpiry = (result.ExpiresUtc!.Value - DateTime.UtcNow).TotalHours;
                AssertTrue(hoursUntilExpiry > Constants.SessionTokenLifetimeHours - 1, "Expiry should be close to lifetime");
                AssertTrue(hoursUntilExpiry <= Constants.SessionTokenLifetimeHours, "Expiry should not exceed lifetime");
            });

            await RunTest("CreateToken NullTenantId Throws", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                AssertThrows<ArgumentNullException>(() => svc.CreateToken(null!, "usr_xyz"));
            });

            await RunTest("CreateToken EmptyTenantId Throws", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                AssertThrows<ArgumentNullException>(() => svc.CreateToken("", "usr_xyz"));
            });

            await RunTest("CreateToken NullUserId Throws", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                AssertThrows<ArgumentNullException>(() => svc.CreateToken("ten_abc", null!));
            });

            await RunTest("CreateToken EmptyUserId Throws", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                AssertThrows<ArgumentNullException>(() => svc.CreateToken("ten_abc", ""));
            });

            await RunTest("ValidateToken ValidToken ReturnsAuthContext", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                AuthenticateResult result = svc.CreateToken("ten_abc", "usr_xyz");

                AuthContext? ctx = svc.ValidateToken(result.Token!);
                AssertNotNull(ctx);
                AssertTrue(ctx!.IsAuthenticated);
                AssertEqual("ten_abc", ctx.TenantId);
                AssertEqual("usr_xyz", ctx.UserId);
                AssertEqual("Session", ctx.AuthMethod);
            });

            await RunTest("ValidateToken NullToken ReturnsNull", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                AuthContext? ctx = svc.ValidateToken(null!);
                AssertNull(ctx);
            });

            await RunTest("ValidateToken EmptyToken ReturnsNull", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                AuthContext? ctx = svc.ValidateToken("");
                AssertNull(ctx);
            });

            await RunTest("ValidateToken TamperedToken ReturnsNull", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                AuthenticateResult result = svc.CreateToken("ten_abc", "usr_xyz");

                // Tamper with the token
                string tampered = result.Token! + "TAMPERED";
                AuthContext? ctx = svc.ValidateToken(tampered);
                AssertNull(ctx);
            });

            await RunTest("ValidateToken GarbageString ReturnsNull", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                AuthContext? ctx = svc.ValidateToken("not-a-valid-token-at-all");
                AssertNull(ctx);
            });

            await RunTest("ValidateToken DifferentKey ReturnsNull", () =>
            {
                SessionTokenService svc1 = new SessionTokenService();
                SessionTokenService svc2 = new SessionTokenService();

                AuthenticateResult result = svc1.CreateToken("ten_abc", "usr_xyz");

                // Validate with a different key
                AuthContext? ctx = svc2.ValidateToken(result.Token!);
                AssertNull(ctx);
            });

            await RunTest("Constructor WithExplicitKey UsesProvidedKey", () =>
            {
                byte[] key = new byte[32];
                RandomNumberGenerator.Fill(key);
                string keyBase64 = Convert.ToBase64String(key);

                SessionTokenService svc = new SessionTokenService(keyBase64);
                AssertEqual(keyBase64, svc.GetKeyBase64());
            });

            await RunTest("Constructor WithoutKey GeneratesKey", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                string keyBase64 = svc.GetKeyBase64();
                AssertNotNull(keyBase64);
                byte[] key = Convert.FromBase64String(keyBase64);
                AssertEqual(32, key.Length, "AES-256 key should be 32 bytes");
            });

            await RunTest("CreateToken ThenValidate WithExplicitKey RoundTrips", () =>
            {
                byte[] key = new byte[32];
                RandomNumberGenerator.Fill(key);
                string keyBase64 = Convert.ToBase64String(key);

                SessionTokenService svc1 = new SessionTokenService(keyBase64);
                AuthenticateResult result = svc1.CreateToken("ten_test", "usr_test");

                // Validate with same key, different instance
                SessionTokenService svc2 = new SessionTokenService(keyBase64);
                AuthContext? ctx = svc2.ValidateToken(result.Token!);

                AssertNotNull(ctx);
                AssertEqual("ten_test", ctx!.TenantId);
                AssertEqual("usr_test", ctx.UserId);
            });

            await RunTest("CreateToken UniqueTokensPerCall", () =>
            {
                SessionTokenService svc = new SessionTokenService();
                AuthenticateResult r1 = svc.CreateToken("ten_abc", "usr_xyz");
                AuthenticateResult r2 = svc.CreateToken("ten_abc", "usr_xyz");

                // Tokens should differ because of random IV
                AssertNotEqual(r1.Token, r2.Token);
            });
        }
    }
}
