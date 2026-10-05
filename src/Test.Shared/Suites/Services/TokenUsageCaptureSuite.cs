namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="TokenUsageCapture"/>: text-length token estimation, the estimated flag
    /// (set when any count is estimated, clear when all counts are real), total = input + output, model
    /// resolution fallbacks, and that an all-zero observation writes nothing. Capture is exercised against
    /// a real database so the persisted record is verified end to end.
    /// </summary>
    public sealed class TokenUsageCaptureSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.TokenUsageCapture";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Token Usage Capture suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("estimate_tokens_from_text_length", "EstimateTokens uses ~3.5 chars per token", TestTags.Positive, () =>
            {
                AssertEqual(0L, TokenUsageCapture.EstimateTokens(null), "Null text estimates zero");
                AssertEqual(0L, TokenUsageCapture.EstimateTokens(""), "Empty text estimates zero");
                AssertEqual(2L, TokenUsageCapture.EstimateTokens(new string('a', 7)), "7 chars ~ 2 tokens");
            }));

            cases.Add(CaseAsync("real_counts_are_not_flagged_estimated", "Real counts persist un-estimated with total input+output", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    LoggingModule logging = QuietLogging();

                    await TokenUsageCapture.CaptureAsync(db, logging, "chat",
                        model: "claude-sonnet-4", runtime: "claudecode",
                        tenantId: "ten_a", userId: null, vesselId: null, captainId: "cpt_a", sourceId: "msn_a",
                        inputTokens: 100, outputTokens: 250, cachedTokens: 30,
                        inputText: null, outputText: null);

                    EnumerationResult<TokenUsageRecord> all = await db.TokenUsage.EnumerateAsync(new TokenUsageQuery());
                    AssertEqual(1, all.Objects.Count, "One record written");
                    TokenUsageRecord record = all.Objects[0];
                    AssertEqual(100L, record.InputTokens, "Input persists");
                    AssertEqual(250L, record.OutputTokens, "Output persists");
                    AssertEqual(30L, record.CachedTokens, "Cached persists");
                    AssertEqual(350L, record.TotalTokens, "Total is input + output");
                    AssertFalse(record.Estimated, "Real counts are not flagged estimated");
                    AssertEqual("chat", record.Source, "Source persists");
                }
            }));

            cases.Add(CaseAsync("missing_counts_are_estimated_from_text", "Missing counts are estimated from text and flagged", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    LoggingModule logging = QuietLogging();

                    await TokenUsageCapture.CaptureAsync(db, logging, "mission",
                        model: null, runtime: "codex",
                        tenantId: null, userId: null, vesselId: null, captainId: null, sourceId: "msn_b",
                        inputTokens: null, outputTokens: null, cachedTokens: null,
                        inputText: new string('a', 35), outputText: new string('b', 70));

                    EnumerationResult<TokenUsageRecord> all = await db.TokenUsage.EnumerateAsync(new TokenUsageQuery());
                    AssertEqual(1, all.Objects.Count, "One record written");
                    TokenUsageRecord record = all.Objects[0];
                    AssertEqual(10L, record.InputTokens, "Input estimated from 35 chars");
                    AssertEqual(20L, record.OutputTokens, "Output estimated from 70 chars");
                    AssertTrue(record.Estimated, "Estimated counts are flagged");
                    AssertEqual("codex", record.Model, "Model falls back to the runtime name");
                }
            }));

            cases.Add(CaseAsync("reported_token_marker_is_used_as_real", "Reported [ARMADA:TOKENS] marker is used as real counts", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    LoggingModule logging = QuietLogging();

                    string output = "did the work\n[ARMADA:TOKENS] input=500 output=1200 cached=300\n[ARMADA:RESULT] COMPLETE";
                    await TokenUsageCapture.CaptureAsync(db, logging, "mission",
                        model: "claude-sonnet-4", runtime: "claudecode",
                        tenantId: null, userId: null, vesselId: null, captainId: null, sourceId: "msn_c",
                        inputTokens: null, outputTokens: null, cachedTokens: null,
                        inputText: "the prompt", outputText: output);

                    EnumerationResult<TokenUsageRecord> all = await db.TokenUsage.EnumerateAsync(new TokenUsageQuery());
                    AssertEqual(1, all.Objects.Count, "One record written");
                    TokenUsageRecord record = all.Objects[0];
                    AssertEqual(500L, record.InputTokens, "Reported input used");
                    AssertEqual(1200L, record.OutputTokens, "Reported output used");
                    AssertEqual(300L, record.CachedTokens, "Reported cached used");
                    AssertEqual(1700L, record.TotalTokens, "Total is reported input + output");
                    AssertFalse(record.Estimated, "Reported counts are not flagged estimated");
                }
            }));

            cases.Add(CaseAsync("quoted_token_marker_is_not_a_report", "A quoted or echoed [ARMADA:TOKENS] legend is not a token report", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    LoggingModule logging = QuietLogging();

                    // The old unanchored regex took the first marker anywhere, so the echoed prompt legend recorded
                    // 1234/567 as real usage.
                    string output =
                        "- `[ARMADA:TOKENS] input=1234 output=567 cached=0` -- report the tokens you consumed\n" +
                        "I would write [ARMADA:TOKENS] input=9 output=9 here.\n" +
                        "```\n[ARMADA:TOKENS] input=7 output=7\n```\n" +
                        "done";
                    await TokenUsageCapture.CaptureAsync(db, logging, "mission",
                        model: "claude-sonnet-4", runtime: "claudecode",
                        tenantId: null, userId: null, vesselId: null, captainId: null, sourceId: "msn_q",
                        inputTokens: null, outputTokens: null, cachedTokens: null,
                        inputText: "the prompt", outputText: output);

                    EnumerationResult<TokenUsageRecord> all = await db.TokenUsage.EnumerateAsync(new TokenUsageQuery());
                    AssertEqual(1, all.Objects.Count, "One record written");
                    TokenUsageRecord record = all.Objects[0];
                    AssertTrue(record.Estimated, "No whole-line report: counts are estimated from text");
                    AssertFalse(record.InputTokens == 1234L, "The echoed legend is not used as input tokens");
                    AssertFalse(record.OutputTokens == 567L, "The echoed legend is not used as output tokens");
                }
            }));

            cases.Add(CaseAsync("zero_token_observation_writes_nothing", "All-zero observation writes nothing", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    LoggingModule logging = QuietLogging();

                    await TokenUsageCapture.CaptureAsync(db, logging, "chat",
                        model: "m", runtime: "r",
                        tenantId: null, userId: null, vesselId: null, captainId: null, sourceId: null,
                        inputTokens: 0, outputTokens: 0, cachedTokens: 0,
                        inputText: null, outputText: null);

                    EnumerationResult<TokenUsageRecord> all = await db.TokenUsage.EnumerateAsync(new TokenUsageQuery());
                    AssertEqual(0, all.Objects.Count, "No record for an all-zero observation");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Token Usage Capture",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule QuietLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
