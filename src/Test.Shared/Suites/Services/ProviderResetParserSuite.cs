namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ProviderResetParser"/>. Positive cases confirm a structured Retry-After value and
    /// an explicit provider reset time set the quarantine window; negative cases confirm message text is never read
    /// for a time and out-of-range values are rejected.
    /// </summary>
    public sealed class ProviderResetParserSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            DateTime now = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

            cases.Add(Case("retry_after_seconds", "A Retry-After value sets the reset time", TestTags.Positive, () =>
            {
                bool ok = ProviderResetParser.TryGetResetUtc(new RuntimeProviderError { HttpStatusCode = 429, RetryAfterSeconds = 120 }, now, out DateTime? reset);
                AssertTrue(ok, "retry-after accepted");
                AssertEqual(now.AddSeconds(120), reset!.Value);
            }));

            cases.Add(Case("explicit_reset_time", "An explicit provider reset time is used", TestTags.Positive, () =>
            {
                DateTime resetAt = now.AddMinutes(45);
                bool ok = ProviderResetParser.TryGetResetUtc(new RuntimeProviderError { ErrorType = "usage_limit_reached", ResetUtc = resetAt }, now, out DateTime? reset);
                AssertTrue(ok, "explicit reset accepted");
                AssertEqual(resetAt, reset!.Value);
            }));

            cases.Add(Case("explicit_reset_wins_over_retry_after", "An explicit reset time wins over Retry-After", TestTags.Positive, () =>
            {
                DateTime resetAt = now.AddMinutes(10);
                bool ok = ProviderResetParser.TryGetResetUtc(new RuntimeProviderError { ResetUtc = resetAt, RetryAfterSeconds = 3600 }, now, out DateTime? reset);
                AssertTrue(ok);
                AssertEqual(resetAt, reset!.Value);
            }));

            cases.Add(Case("no_structured_reset", "No provider error, or one without reset data, yields no reset time", TestTags.Negative, () =>
            {
                AssertFalse(ProviderResetParser.TryGetResetUtc(null, now, out DateTime? none), "null error");
                AssertNull(none);
                AssertFalse(ProviderResetParser.TryGetResetUtc(new RuntimeProviderError { HttpStatusCode = 429, Message = "try again in 30 minutes; resets at 2026-01-15T12:45:00Z" }, now, out DateTime? fromText), "message text is never read for a reset time");
                AssertNull(fromText);
            }));

            cases.Add(Case("past_reset_rejected", "A reset time in the past is rejected", TestTags.Negative, () =>
            {
                AssertFalse(ProviderResetParser.TryGetResetUtc(new RuntimeProviderError { ResetUtc = now.AddMinutes(-5) }, now, out DateTime? reset));
                AssertNull(reset);
            }));

            cases.Add(Case("far_future_reset_rejected", "A reset more than 24 hours out is rejected", TestTags.Negative, () =>
            {
                AssertFalse(ProviderResetParser.TryGetResetUtc(new RuntimeProviderError { RetryAfterSeconds = 100 * 3600 }, now, out DateTime? reset));
                AssertNull(reset);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.ProviderResetParser",
                displayName: "Provider Reset Parser",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.ProviderResetParser",
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
