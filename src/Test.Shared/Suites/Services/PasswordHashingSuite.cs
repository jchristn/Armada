namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Unit coverage for <see cref="PasswordHasher"/> (O-05): the stored PBKDF2 format, per-hash salts, verification of
    /// new and legacy unsalted SHA-256 hashes, rehash detection, conversion to the storage format, and the cached
    /// default-password check.
    /// </summary>
    public sealed class PasswordHashingSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.PasswordHashing";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("format", "A new hash is pbkdf2-sha256$600000$salt$hash with a 16-byte salt and 32-byte key", TestTags.Positive, () =>
            {
                string stored = PasswordHasher.HashPassword("s3cret-password");
                string[] parts = stored.Split('$');
                AssertEqual(4, parts.Length);
                AssertEqual("pbkdf2-sha256", parts[0]);
                AssertEqual(PasswordHasher.Iterations.ToString(), parts[1]);
                AssertEqual(600000, PasswordHasher.Iterations);
                AssertEqual(16, Convert.FromBase64String(parts[2]).Length);
                AssertEqual(32, Convert.FromBase64String(parts[3]).Length);
                AssertTrue(PasswordHasher.IsAdaptiveHash(stored));
                AssertFalse(stored.Contains(UserMaster.ComputePasswordHash("s3cret-password")), "no unsalted digest inside");
            }));

            cases.Add(Case("salt_unique", "Two hashes of the same password differ (per-hash random salt)", TestTags.Positive, () =>
            {
                string a = PasswordHasher.HashPassword("same-password");
                string b = PasswordHasher.HashPassword("same-password");
                AssertNotEqual(a, b);
                AssertNotEqual(a.Split('$')[2], b.Split('$')[2]);
                AssertTrue(PasswordHasher.Verify("same-password", a));
                AssertTrue(PasswordHasher.Verify("same-password", b));
            }));

            cases.Add(Case("verify", "Verify accepts the right password and rejects wrong, empty, and null ones", TestTags.Positive, () =>
            {
                string stored = PasswordHasher.HashPassword("right-password");
                AssertTrue(PasswordHasher.Verify("right-password", stored));
                AssertFalse(PasswordHasher.Verify("wrong-password", stored));
                AssertFalse(PasswordHasher.Verify("Right-password", stored));
                AssertFalse(PasswordHasher.Verify("", stored));
                AssertFalse(PasswordHasher.Verify(null, stored));
                AssertFalse(PasswordHasher.Verify("right-password", null));
            }));

            cases.Add(Case("legacy_verify_and_rehash", "Legacy unsalted SHA-256 hashes verify and are flagged for rehash", TestTags.Positive, () =>
            {
                string legacy = UserMaster.ComputePasswordHash("legacy-password");
                AssertFalse(PasswordHasher.IsAdaptiveHash(legacy));
                AssertTrue(PasswordHasher.Verify("legacy-password", legacy));
                AssertTrue(PasswordHasher.Verify("legacy-password", legacy.ToUpperInvariant()), "case-insensitive hex");
                AssertFalse(PasswordHasher.Verify("other", legacy));
                AssertTrue(PasswordHasher.NeedsRehash(legacy));
                AssertFalse(PasswordHasher.NeedsRehash(PasswordHasher.HashPassword("legacy-password")));
            }));

            cases.Add(Case("weaker_iterations_rehash", "A PBKDF2 hash with fewer iterations verifies and is flagged for rehash", TestTags.Positive, () =>
            {
                byte[] salt = RandomNumberGenerator.GetBytes(16);
                byte[] key = Rfc2898DeriveBytes.Pbkdf2(Encoding.ASCII.GetBytes(UserMaster.ComputePasswordHash("older")), salt, 10000, HashAlgorithmName.SHA256, 32);
                string stored = "pbkdf2-sha256$10000$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(key);
                AssertTrue(PasswordHasher.IsAdaptiveHash(stored));
                AssertTrue(PasswordHasher.Verify("older", stored));
                AssertTrue(PasswordHasher.NeedsRehash(stored));
            }));

            cases.Add(Case("storage_format", "ToStorageFormat stretches a SHA-256 value (still verifies) and keeps a PBKDF2 value", TestTags.Positive, () =>
            {
                string sha = UserMaster.ComputePasswordHash("api-client-password");
                string stored = PasswordHasher.ToStorageFormat(sha);
                AssertTrue(PasswordHasher.IsAdaptiveHash(stored));
                AssertTrue(PasswordHasher.Verify("api-client-password", stored));
                AssertEqual(stored, PasswordHasher.ToStorageFormat(stored));
                AssertThrows<ArgumentNullException>(() => PasswordHasher.ToStorageFormat(""));
            }));

            cases.Add(Case("malformed_is_not_adaptive", "Malformed PBKDF2-looking values are not treated as PBKDF2 and do not verify", TestTags.Negative, () =>
            {
                string[] malformed = new string[]
                {
                    "pbkdf2-sha256$abc$AAAA$AAAA",
                    "pbkdf2-sha256$600000$not-base64!$AAAA",
                    "pbkdf2-sha256$5$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    "pbkdf2-sha256$600000$AAAA",
                    "pbkdf2-sha256$999999999$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
                };
                foreach (string value in malformed)
                {
                    AssertFalse(PasswordHasher.IsAdaptiveHash(value), value);
                    AssertFalse(PasswordHasher.Verify("password", value), value);
                }
            }));

            cases.Add(Case("user_master_uses_hasher", "UserMaster verifies PBKDF2 and legacy hashes and detects the default password in both", TestTags.Positive, () =>
            {
                UserMaster user = new UserMaster("default", Constants.DefaultUserEmail, "password");
                AssertTrue(user.UsesDefaultPassword(), "legacy default");
                user.PasswordSha256 = PasswordHasher.HashPassword(Constants.DefaultUserPassword);
                AssertTrue(user.VerifyPassword(Constants.DefaultUserPassword));
                AssertTrue(user.UsesDefaultPassword(), "pbkdf2 default");
                AssertTrue(user.UsesDefaultPassword(), "pbkdf2 default (cached)");
                user.PasswordSha256 = PasswordHasher.HashPassword("a-new-password");
                AssertFalse(user.UsesDefaultPassword(), "changed password");
                AssertTrue(user.VerifyPassword("a-new-password"));
                AssertFalse(user.VerifyPassword(Constants.DefaultUserPassword));
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Password Hashing",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
