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
    /// Descriptors for <see cref="ProviderResetParser"/>. Positive cases confirm retry-after seconds, a
    /// relative "in N minutes" phrase, and an explicit ISO timestamp parse into an in-range reset time;
    /// negative cases confirm noise, a past timestamp, and an absurdly-far value are all rejected.
    /// </summary>
    public sealed class ProviderResetParserSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the provider-reset-parser suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            DateTime now = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

            cases.Add(Case("retry_after_seconds", "A retry-after in seconds parses", TestTags.Positive, () =>
            {
                bool ok = ProviderResetParser.TryParseResetUtc("429 Too Many Requests. retry-after: 120", now, out DateTime? reset);
                AssertTrue(ok, "retry-after should parse");
                AssertEqual(now.AddSeconds(120), reset!.Value);
            }));

            cases.Add(Case("relative_minutes", "A relative 'try again in N minutes' parses", TestTags.Positive, () =>
            {
                bool ok = ProviderResetParser.TryParseResetUtc("usage limit reached; try again in 30 minutes", now, out DateTime? reset);
                AssertTrue(ok, "relative minutes should parse");
                AssertEqual(now.AddMinutes(30), reset!.Value);
            }));

            cases.Add(Case("iso_timestamp", "An explicit reset ISO timestamp parses", TestTags.Positive, () =>
            {
                bool ok = ProviderResetParser.TryParseResetUtc("rate limited; resets at 2026-01-15T12:45:00Z", now, out DateTime? reset);
                AssertTrue(ok, "iso reset should parse");
                AssertEqual(new DateTime(2026, 1, 15, 12, 45, 0, DateTimeKind.Utc), reset!.Value);
            }));

            cases.Add(Case("noise_is_rejected", "Unrelated output yields no reset", TestTags.Negative, () =>
            {
                bool ok = ProviderResetParser.TryParseResetUtc("Segmentation fault (core dumped)", now, out DateTime? reset);
                AssertFalse(ok, "noise should not parse");
                AssertNull(reset);
            }));

            cases.Add(Case("past_timestamp_is_rejected", "A reset time in the past is rejected", TestTags.Negative, () =>
            {
                bool ok = ProviderResetParser.TryParseResetUtc("resets at 2020-01-01T00:00:00Z", now, out DateTime? reset);
                AssertFalse(ok, "a past reset is not trusted");
            }));

            cases.Add(Case("absurd_future_is_rejected", "A reset beyond the 24h cap is rejected", TestTags.Negative, () =>
            {
                bool ok = ProviderResetParser.TryParseResetUtc("try again in 100 hours", now, out DateTime? reset);
                AssertFalse(ok, "an out-of-range reset is not trusted");
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
