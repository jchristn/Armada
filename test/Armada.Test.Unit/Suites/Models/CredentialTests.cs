namespace Armada.Test.Unit.Suites.Models
{
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Test.Common;

    public class CredentialTests : TestSuite
    {
        public override string Name => "Credential Model";

        protected override async Task RunTestsAsync()
        {
            await RunTest("Credential DefaultConstructor GeneratesIdWithPrefix", () =>
            {
                Credential cred = new Credential();
                AssertStartsWith(Constants.CredentialIdPrefix, cred.Id);
            });

            await RunTest("Credential DefaultConstructor HasDefaultValues", () =>
            {
                Credential cred = new Credential();
                AssertEqual(Constants.DefaultTenantId, cred.TenantId);
                AssertEqual(Constants.DefaultUserId, cred.UserId);
                AssertNull(cred.Name);
                AssertTrue(cred.Active);
            });

            await RunTest("Credential DefaultConstructor GeneratesBearerToken", () =>
            {
                Credential cred = new Credential();
                AssertNotNull(cred.BearerToken);
                AssertEqual(64, cred.BearerToken.Length, "Bearer token should be 64 chars");
            });

            await RunTest("Credential TenantUserConstructor SetsProperties", () =>
            {
                Credential cred = new Credential("ten_abc", "usr_xyz");
                AssertEqual("ten_abc", cred.TenantId);
                AssertEqual("usr_xyz", cred.UserId);
            });

            await RunTest("Credential TenantUserConstructor StillGeneratesToken", () =>
            {
                Credential cred = new Credential("ten_abc", "usr_xyz");
                AssertNotNull(cred.BearerToken);
                AssertEqual(64, cred.BearerToken.Length);
            });

            await RunTest("GenerateBearerToken Returns64CharAlphanumeric", () =>
            {
                string token = Credential.GenerateBearerToken();
                AssertEqual(64, token.Length);

                foreach (char c in token)
                {
                    AssertTrue(char.IsLetterOrDigit(c), "Token char '" + c + "' should be alphanumeric");
                }
            });

            await RunTest("GenerateBearerToken UniqueAcrossCalls", () =>
            {
                string t1 = Credential.GenerateBearerToken();
                string t2 = Credential.GenerateBearerToken();
                AssertNotEqual(t1, t2);
            });

            await RunTest("Credential UniqueTokensAcrossInstances", () =>
            {
                Credential c1 = new Credential();
                Credential c2 = new Credential();
                AssertNotEqual(c1.BearerToken, c2.BearerToken);
            });

            await RunTest("Credential SetId Null Throws", () =>
            {
                Credential cred = new Credential();
                AssertThrows<ArgumentNullException>(() => cred.Id = null!);
            });

            await RunTest("Credential SetId Empty Throws", () =>
            {
                Credential cred = new Credential();
                AssertThrows<ArgumentNullException>(() => cred.Id = "");
            });

            await RunTest("Credential SetTenantId Null Throws", () =>
            {
                Credential cred = new Credential();
                AssertThrows<ArgumentNullException>(() => cred.TenantId = null!);
            });

            await RunTest("Credential SetUserId Null Throws", () =>
            {
                Credential cred = new Credential();
                AssertThrows<ArgumentNullException>(() => cred.UserId = null!);
            });

            await RunTest("Credential SetBearerToken Empty Throws", () =>
            {
                Credential cred = new Credential();
                AssertThrows<ArgumentNullException>(() => cred.BearerToken = "");
            });

            await RunTest("Credential SetName AcceptsNull", () =>
            {
                Credential cred = new Credential();
                cred.Name = "My API Key";
                AssertEqual("My API Key", cred.Name);
                cred.Name = null;
                AssertNull(cred.Name);
            });

            await RunTest("Credential Serialization RoundTrip", () =>
            {
                Credential cred = new Credential("ten_test", "usr_test");
                cred.Name = "Test Credential";
                cred.Active = false;

                string json = JsonSerializer.Serialize(cred);
                Credential deserialized = JsonSerializer.Deserialize<Credential>(json)!;

                AssertEqual(cred.Id, deserialized.Id);
                AssertEqual(cred.TenantId, deserialized.TenantId);
                AssertEqual(cred.UserId, deserialized.UserId);
                AssertEqual(cred.BearerToken, deserialized.BearerToken);
                AssertEqual(cred.Name, deserialized.Name);
                AssertEqual(cred.Active, deserialized.Active);
            });
        }
    }
}
