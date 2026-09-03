namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="SecretRedactor"/>, the shared secret-shaped-value redaction used by both
    /// the runtime-log formatter and request-history capture. Positive cases confirm bearer tokens, OpenAI
    /// and GitHub keys, and labelled assignments are scrubbed; negative cases confirm ordinary text is left
    /// untouched.
    /// </summary>
    public sealed class SecretRedactorSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the secret-redactor suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("redacts_bearer_token", "A bearer token is redacted", TestTags.Positive, () =>
            {
                string redacted = SecretRedactor.Redact("Authorization: Bearer abcdef1234567890", out bool changed);
                AssertTrue(changed, "a bearer token should be redacted");
                AssertContains("[REDACTED]", redacted, "the output should mark the redaction");
                AssertFalse(redacted.Contains("abcdef1234567890", StringComparison.Ordinal), "the secret must not survive");
            }));

            cases.Add(Case("redacts_openai_and_github_keys", "OpenAI and GitHub keys are redacted", TestTags.Positive, () =>
            {
                string redacted = SecretRedactor.Redact("key sk-abcdefghijklmnopqrstuvwx and ghp_abcdefghijklmnopqrstuvwxyz12", out bool changed);
                AssertTrue(changed, "keys should be redacted");
                AssertFalse(redacted.Contains("sk-abcdefghijklmnopqrstuvwx", StringComparison.Ordinal), "the sk- key must not survive");
                AssertFalse(redacted.Contains("ghp_abcdefghijklmnopqrstuvwxyz12", StringComparison.Ordinal), "the gh token must not survive");
            }));

            cases.Add(Case("redacts_labelled_assignment", "A labelled api-key assignment is redacted", TestTags.Positive, () =>
            {
                string redacted = SecretRedactor.Redact("api_key=supersecretvalue", out bool changed);
                AssertTrue(changed, "a labelled assignment should be redacted");
                AssertFalse(redacted.Contains("supersecretvalue", StringComparison.Ordinal), "the value must not survive");
            }));

            cases.Add(Case("leaves_ordinary_text_untouched", "Ordinary text is not redacted", TestTags.Negative, () =>
            {
                string input = "Built 42 files with no warnings in src/Armada.Core.";
                string redacted = SecretRedactor.Redact(input, out bool changed);
                AssertFalse(changed, "ordinary text should not be flagged");
                AssertEqual(input, redacted, "ordinary text should pass through unchanged");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.SecretRedactor",
                displayName: "Secret Redactor",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.SecretRedactor",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
