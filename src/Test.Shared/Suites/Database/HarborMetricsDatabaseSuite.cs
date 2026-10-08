namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Persistence of the Harbor metrics tables (migration 81): harbor_jobs, harbor_link_samples, and harbor_link_events
    /// round-trip every field, enumerate by Harbor and window, read latest, and delete by cutoff and by Harbor; token usage
    /// round-trips and filters by harbor_id.
    /// </summary>
    public sealed class HarborMetricsDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.HarborMetrics";
        private static readonly DateTime _T = new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("job_roundtrip_and_update", "A job record round-trips every field and updates by id", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                HarborJobRecord job = new HarborJobRecord
                {
                    JobId = "job_rt",
                    HarborId = "hbr_db",
                    TenantId = "ten_db",
                    Kind = HarborJobKindEnum.Refinement,
                    Runtime = "Codex",
                    Model = "gpt-5",
                    MissionId = "msn_db",
                    CaptainId = "cpt_db",
                    LaunchedUtc = _T,
                    CreatedUtc = _T
                };
                await db.HarborJobs.CreateAsync(job).ConfigureAwait(false);
                HarborJobRecord? read = await db.HarborJobs.ReadByJobIdAsync("job_rt").ConfigureAwait(false);
                AssertNotNull(read, "read back");
                AssertEqual(job.Id, read!.Id);
                AssertEqual(HarborJobKindEnum.Refinement, read.Kind);
                AssertEqual("Codex", read.Runtime);
                AssertEqual("gpt-5", read.Model);
                AssertEqual("ten_db", read.TenantId);
                AssertEqual("msn_db", read.MissionId);
                AssertEqual("cpt_db", read.CaptainId);
                AssertEqual(_T, read.LaunchedUtc);
                AssertNull(read.StartedUtc, "not started");
                AssertEqual(HarborJobOutcomeEnum.Running, read.Outcome);
                AssertFalse(read.StopRequested, "no stop");

                read.StartedUtc = _T.AddSeconds(1);
                read.FirstOutputUtc = _T.AddSeconds(2);
                read.EndedUtc = _T.AddSeconds(9);
                read.TimeToFirstOutputMs = 2000;
                read.DurationMs = 9000;
                read.ExitCode = 3;
                read.Outcome = HarborJobOutcomeEnum.Stopped;
                read.StopRequested = true;
                await db.HarborJobs.UpdateAsync(read).ConfigureAwait(false);

                HarborJobRecord updated = (await db.HarborJobs.ReadByJobIdAsync("job_rt").ConfigureAwait(false))!;
                AssertEqual((DateTime?)_T.AddSeconds(1), updated.StartedUtc);
                AssertEqual((DateTime?)_T.AddSeconds(2), updated.FirstOutputUtc);
                AssertEqual((DateTime?)_T.AddSeconds(9), updated.EndedUtc);
                AssertEqual((long?)2000, updated.TimeToFirstOutputMs);
                AssertEqual((long?)9000, updated.DurationMs);
                AssertEqual((int?)3, updated.ExitCode);
                AssertEqual(HarborJobOutcomeEnum.Stopped, updated.Outcome);
                AssertTrue(updated.StopRequested, "stop flag");
                AssertNull(await db.HarborJobs.ReadByJobIdAsync("job_missing").ConfigureAwait(false), "unknown job");
            }));

            cases.Add(CaseAsync("job_window_open_and_prune", "Jobs enumerate by Harbor and window, open jobs list apart, and pruning keeps open and recent jobs", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                await db.HarborJobs.CreateAsync(Job("old", "hbr_w", _T.AddHours(-5), _T.AddHours(-4))).ConfigureAwait(false);
                await db.HarborJobs.CreateAsync(Job("spans", "hbr_w", _T.AddHours(-2), _T.AddMinutes(30))).ConfigureAwait(false);
                await db.HarborJobs.CreateAsync(Job("inside", "hbr_w", _T.AddMinutes(10), _T.AddMinutes(20))).ConfigureAwait(false);
                await db.HarborJobs.CreateAsync(Job("open", "hbr_w", _T.AddHours(-3), null)).ConfigureAwait(false);
                await db.HarborJobs.CreateAsync(Job("later", "hbr_w", _T.AddHours(2), _T.AddHours(3))).ConfigureAwait(false);
                await db.HarborJobs.CreateAsync(Job("other", "hbr_other", _T.AddMinutes(10), _T.AddMinutes(20))).ConfigureAwait(false);

                List<HarborJobRecord> window = await db.HarborJobs.EnumerateAsync("hbr_w", _T, _T.AddHours(1)).ConfigureAwait(false);
                AssertEqual("open,spans,inside", String.Join(",", window.Select(j => j.JobId)), "active or ended in the window, oldest launch first");
                List<HarborJobRecord> open = await db.HarborJobs.EnumerateOpenAsync("hbr_w").ConfigureAwait(false);
                AssertEqual(1, open.Count);
                AssertEqual("open", open[0].JobId);

                int pruned = await db.HarborJobs.DeleteEndedBeforeAsync(_T).ConfigureAwait(false);
                AssertEqual(1, pruned, "only the job that ended before the cutoff");
                AssertNotNull(await db.HarborJobs.ReadByJobIdAsync("open").ConfigureAwait(false), "an open job is never pruned");
                AssertEqual(4, await db.HarborJobs.DeleteByHarborAsync("hbr_w").ConfigureAwait(false), "the Harbor's remaining jobs");
                AssertNotNull(await db.HarborJobs.ReadByJobIdAsync("other").ConfigureAwait(false), "another Harbor's job stays");
            }));

            cases.Add(CaseAsync("samples", "Link samples round-trip, enumerate by window, read latest, and prune", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                await db.HarborLinkSamples.CreateAsync(new HarborLinkSample { HarborId = "hbr_s", BucketStartUtc = _T, HeartbeatCount = 4, RoundTripCount = 3, RoundTripTotalMs = 90, RoundTripMaxMs = 50, ReconnectCount = 2, LastReconnectUtc = _T.AddMinutes(-9) }).ConfigureAwait(false);
                await db.HarborLinkSamples.CreateAsync(new HarborLinkSample { HarborId = "hbr_s", BucketStartUtc = _T.AddMinutes(1), HeartbeatCount = 1 }).ConfigureAwait(false);
                await db.HarborLinkSamples.CreateAsync(new HarborLinkSample { HarborId = "hbr_s", BucketStartUtc = _T.AddDays(-40), HeartbeatCount = 1 }).ConfigureAwait(false);

                List<HarborLinkSample> window = await db.HarborLinkSamples.EnumerateAsync("hbr_s", _T, _T.AddMinutes(1)).ConfigureAwait(false);
                AssertEqual(1, window.Count, "end is exclusive");
                HarborLinkSample first = window[0];
                AssertEqual(4, first.HeartbeatCount);
                AssertEqual(3, first.RoundTripCount);
                AssertEqual(90L, first.RoundTripTotalMs);
                AssertEqual((long?)50, first.RoundTripMaxMs);
                AssertEqual((int?)2, first.ReconnectCount);
                AssertEqual((DateTime?)_T.AddMinutes(-9), first.LastReconnectUtc);

                HarborLinkSample? latest = await db.HarborLinkSamples.ReadLatestAsync("hbr_s").ConfigureAwait(false);
                AssertEqual(_T.AddMinutes(1), latest!.BucketStartUtc);
                AssertNull(latest.RoundTripMaxMs, "no round trip reported");
                AssertNull(await db.HarborLinkSamples.ReadLatestAsync("hbr_none").ConfigureAwait(false), "no samples");
                AssertEqual(1, await db.HarborLinkSamples.DeleteBeforeAsync(_T.AddDays(-30)).ConfigureAwait(false));
                AssertEqual(2, await db.HarborLinkSamples.DeleteByHarborAsync("hbr_s").ConfigureAwait(false));
            }));

            cases.Add(CaseAsync("events", "Link events round-trip, enumerate by window, read the latest before a time, list Harbors, and prune", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                await db.HarborLinkEvents.CreateAsync(new HarborLinkEvent { HarborId = "hbr_e", EventType = HarborLinkEventTypeEnum.Connected, OccurredUtc = _T.AddHours(-2) }).ConfigureAwait(false);
                await db.HarborLinkEvents.CreateAsync(new HarborLinkEvent { HarborId = "hbr_e", EventType = HarborLinkEventTypeEnum.Reconnecting, OccurredUtc = _T.AddHours(-1), Detail = "Link closed" }).ConfigureAwait(false);
                await db.HarborLinkEvents.CreateAsync(new HarborLinkEvent { HarborId = "hbr_e", EventType = HarborLinkEventTypeEnum.Disconnected, OccurredUtc = _T }).ConfigureAwait(false);
                await db.HarborLinkEvents.CreateAsync(new HarborLinkEvent { HarborId = "hbr_f", EventType = HarborLinkEventTypeEnum.Connected, OccurredUtc = _T }).ConfigureAwait(false);

                List<HarborLinkEvent> window = await db.HarborLinkEvents.EnumerateAsync("hbr_e", _T.AddHours(-1), _T.AddHours(1)).ConfigureAwait(false);
                AssertEqual(2, window.Count);
                AssertEqual(HarborLinkEventTypeEnum.Reconnecting, window[0].EventType, "oldest first");
                AssertEqual("Link closed", window[0].Detail);

                HarborLinkEvent? before = await db.HarborLinkEvents.ReadLatestBeforeAsync("hbr_e", _T.AddHours(-1)).ConfigureAwait(false);
                AssertEqual(HarborLinkEventTypeEnum.Connected, before!.EventType, "strictly before");
                AssertNull(await db.HarborLinkEvents.ReadLatestBeforeAsync("hbr_e", _T.AddHours(-3)).ConfigureAwait(false), "nothing earlier");
                AssertEqual("hbr_e,hbr_f", String.Join(",", await db.HarborLinkEvents.EnumerateHarborIdsAsync().ConfigureAwait(false)));

                AssertEqual(2, await db.HarborLinkEvents.DeleteBeforeAsync("hbr_e", _T).ConfigureAwait(false), "older than the latest");
                AssertEqual(1, await db.HarborLinkEvents.DeleteByHarborAsync("hbr_e").ConfigureAwait(false));
                AssertEqual("hbr_f", String.Join(",", await db.HarborLinkEvents.EnumerateHarborIdsAsync().ConfigureAwait(false)));
            }));

            cases.Add(CaseAsync("token_usage_harbor_filter", "Token usage stores the Harbor and filters by it", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                await db.TokenUsage.CreateAsync(new TokenUsageRecord { Model = "m", Source = "mission", HarborId = "hbr_tok", InputTokens = 1, TotalTokens = 1, CreatedUtc = _T }).ConfigureAwait(false);
                await db.TokenUsage.CreateAsync(new TokenUsageRecord { Model = "m", Source = "chat", InputTokens = 2, TotalTokens = 2, CreatedUtc = _T }).ConfigureAwait(false);

                List<TokenUsageRecord> onHarbor = await db.TokenUsage.EnumerateForSummaryAsync(new TokenUsageQuery { HarborId = "hbr_tok" }).ConfigureAwait(false);
                AssertEqual(1, onHarbor.Count);
                AssertEqual("hbr_tok", onHarbor[0].HarborId);
                List<TokenUsageRecord> all = await db.TokenUsage.EnumerateForSummaryAsync(new TokenUsageQuery()).ConfigureAwait(false);
                AssertEqual(2, all.Count);
                AssertEqual(1, all.Count(r => r.HarborId == null), "the Admiral-host record has no Harbor");
            }));

            return new TestSuiteDescriptor(SuiteId, "Harbor Metrics Database", cases);
        }

        #endregion

        #region Private-Methods

        private static HarborJobRecord Job(string jobId, string harborId, DateTime launched, DateTime? ended)
        {
            return new HarborJobRecord
            {
                JobId = jobId,
                HarborId = harborId,
                Runtime = "ClaudeCode",
                LaunchedUtc = launched,
                EndedUtc = ended,
                Outcome = ended.HasValue ? HarborJobOutcomeEnum.Succeeded : HarborJobOutcomeEnum.Running
            };
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag, TestTags.Database });
        }

        #endregion
    }
}
