namespace Armada.Test.Unit.Suites.Models
{
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Test.Common;

    public class UserMasterTests : TestSuite
    {
        public override string Name => "UserMaster Model";

        protected override async Task RunTestsAsync()
        {
            await RunTest("UserMaster DefaultConstructor GeneratesIdWithPrefix", () =>
            {
                UserMaster user = new UserMaster();
                AssertStartsWith(Constants.UserIdPrefix, user.Id);
            });

            await RunTest("UserMaster DefaultConstructor HasDefaultValues", () =>
            {
                UserMaster user = new UserMaster();
                AssertEqual(Constants.DefaultTenantId, user.TenantId);
                AssertEqual("admin@armada", user.Email);
                AssertFalse(user.IsAdmin);
                AssertTrue(user.Active);
                AssertNull(user.FirstName);
                AssertNull(user.LastName);
            });

            await RunTest("UserMaster ParameterizedConstructor SetsProperties", () =>
            {
                UserMaster user = new UserMaster("ten_abc", "alice@example.com", "secret123");
                AssertEqual("ten_abc", user.TenantId);
                AssertEqual("alice@example.com", user.Email);
                AssertNotEqual("secret123", user.PasswordSha256, "Password should be hashed, not stored plaintext");
            });

            await RunTest("UserMaster ParameterizedConstructor HashesPassword", () =>
            {
                UserMaster user = new UserMaster("ten_abc", "alice@example.com", "secret123");
                string expectedHash = UserMaster.ComputePasswordHash("secret123");
                AssertEqual(expectedHash, user.PasswordSha256);
            });

            await RunTest("ComputePasswordHash ReturnsSha256HexLowercase", () =>
            {
                string hash = UserMaster.ComputePasswordHash("password");
                AssertEqual(64, hash.Length, "SHA256 hex should be 64 chars");
                AssertEqual(hash, hash.ToLowerInvariant(), "Should be lowercase");
            });

            await RunTest("ComputePasswordHash DeterministicForSameInput", () =>
            {
                string hash1 = UserMaster.ComputePasswordHash("mypassword");
                string hash2 = UserMaster.ComputePasswordHash("mypassword");
                AssertEqual(hash1, hash2);
            });

            await RunTest("ComputePasswordHash DiffersForDifferentInput", () =>
            {
                string hash1 = UserMaster.ComputePasswordHash("password1");
                string hash2 = UserMaster.ComputePasswordHash("password2");
                AssertNotEqual(hash1, hash2);
            });

            await RunTest("ComputePasswordHash NullThrows", () =>
            {
                AssertThrows<ArgumentNullException>(() => UserMaster.ComputePasswordHash(null!));
            });

            await RunTest("ComputePasswordHash EmptyThrows", () =>
            {
                AssertThrows<ArgumentNullException>(() => UserMaster.ComputePasswordHash(""));
            });

            await RunTest("VerifyPassword CorrectPassword ReturnsTrue", () =>
            {
                UserMaster user = new UserMaster("ten_abc", "alice@example.com", "correct-password");
                AssertTrue(user.VerifyPassword("correct-password"));
            });

            await RunTest("VerifyPassword WrongPassword ReturnsFalse", () =>
            {
                UserMaster user = new UserMaster("ten_abc", "alice@example.com", "correct-password");
                AssertFalse(user.VerifyPassword("wrong-password"));
            });

            await RunTest("VerifyPassword Null ReturnsFalse", () =>
            {
                UserMaster user = new UserMaster("ten_abc", "alice@example.com", "correct-password");
                AssertFalse(user.VerifyPassword(null!));
            });

            await RunTest("VerifyPassword Empty ReturnsFalse", () =>
            {
                UserMaster user = new UserMaster("ten_abc", "alice@example.com", "correct-password");
                AssertFalse(user.VerifyPassword(""));
            });

            await RunTest("Redact ReplacesPasswordWithStars", () =>
            {
                UserMaster user = new UserMaster("ten_abc", "alice@example.com", "secret");
                UserMaster redacted = UserMaster.Redact(user);
                AssertEqual("********", redacted.PasswordSha256);
            });

            await RunTest("Redact PreservesOtherFields", () =>
            {
                UserMaster user = new UserMaster("ten_abc", "alice@example.com", "secret");
                user.FirstName = "Alice";
                user.LastName = "Smith";
                user.IsAdmin = true;
                user.Active = false;

                UserMaster redacted = UserMaster.Redact(user);
                AssertEqual(user.Id, redacted.Id);
                AssertEqual(user.TenantId, redacted.TenantId);
                AssertEqual(user.Email, redacted.Email);
                AssertEqual("Alice", redacted.FirstName);
                AssertEqual("Smith", redacted.LastName);
                AssertTrue(redacted.IsAdmin);
                AssertFalse(redacted.Active);
                AssertEqual(user.CreatedUtc, redacted.CreatedUtc);
                AssertEqual(user.LastUpdateUtc, redacted.LastUpdateUtc);
            });

            await RunTest("Redact NullUser Throws", () =>
            {
                AssertThrows<ArgumentNullException>(() => UserMaster.Redact(null!));
            });

            await RunTest("UserMaster SetId Null Throws", () =>
            {
                UserMaster user = new UserMaster();
                AssertThrows<ArgumentNullException>(() => user.Id = null!);
            });

            await RunTest("UserMaster SetTenantId Null Throws", () =>
            {
                UserMaster user = new UserMaster();
                AssertThrows<ArgumentNullException>(() => user.TenantId = null!);
            });

            await RunTest("UserMaster SetEmail Null Throws", () =>
            {
                UserMaster user = new UserMaster();
                AssertThrows<ArgumentNullException>(() => user.Email = null!);
            });

            await RunTest("UserMaster SetPasswordSha256 Empty Throws", () =>
            {
                UserMaster user = new UserMaster();
                AssertThrows<ArgumentNullException>(() => user.PasswordSha256 = "");
            });

            await RunTest("UserMaster Serialization RoundTrip", () =>
            {
                UserMaster user = new UserMaster("ten_test", "bob@example.com", "pass123");
                user.FirstName = "Bob";
                user.LastName = "Jones";
                user.IsAdmin = true;

                string json = JsonSerializer.Serialize(user);
                UserMaster deserialized = JsonSerializer.Deserialize<UserMaster>(json)!;

                AssertEqual(user.Id, deserialized.Id);
                AssertEqual(user.TenantId, deserialized.TenantId);
                AssertEqual(user.Email, deserialized.Email);
                AssertEqual(user.PasswordSha256, deserialized.PasswordSha256);
                AssertEqual(user.FirstName, deserialized.FirstName);
                AssertEqual(user.LastName, deserialized.LastName);
                AssertEqual(user.IsAdmin, deserialized.IsAdmin);
            });
        }
    }
}
