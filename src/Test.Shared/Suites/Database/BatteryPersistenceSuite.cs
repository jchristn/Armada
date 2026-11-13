namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Battery run and task result persistence on SQLite: every field round-trips (fully populated and minimal rows),
    /// details JSON survives, enumeration filters and pages, and deleting a run removes its results.
    /// </summary>
    public sealed class BatteryPersistenceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.BatteryPersistence";
        private static readonly DateTime _Fixed = new DateTime(2026, 10, 27, 8, 9, 10, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("run_roundtrip_all_fields", "A fully populated battery run round-trips every field", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                BatteryRun run = FullRun();
                await db.BatteryRuns.CreateAsync(run).ConfigureAwait(false);

                BatteryRun? read = await db.BatteryRuns.ReadAsync(run.Id).ConfigureAwait(false);
                AssertNotNull(read, "read back");
                AssertTrue(read!.Id.StartsWith("bat_", StringComparison.Ordinal), "bat_ prefix: " + read.Id);
                AssertEqual("ten_full", read.TenantId);
                AssertEqual("usr_full", read.UserId);
                AssertEqual("cpt_full", read.CaptainId);
                AssertEqual("Full captain", read.CaptainName);
                AssertEqual("csharp,python", String.Join(",", read.Packs));
                AssertEqual("csharp@1,python@2", read.BatteryVersion);
                AssertEqual(5, read.RunCount);
                AssertEqual(BatteryRunStatusEnum.Completed, read.Status);
                AssertFalse(read.IsEvaluation, "is_evaluation false persisted");
                AssertEqual("ModelEvaluation", read.Pipeline);
                AssertEqual((int?)424242, read.Seed);
                AssertEqual("{\"temperature\":0.2}", read.SamplingJson);
                AssertEqual("0123456789abcdef", read.ConfigurationRevision);
                AssertEqual("{\"armadaVersion\":\"1.1.0\"}", read.ConfigurationJson);
                AssertEqual("ApiEndpoint", read.Runtime);
                AssertEqual("qwen3:32b", read.Model);
                AssertEqual("mep_full", read.ModelEndpointId);
                AssertEqual("gb10.local", read.InferenceHost);
                AssertEqual(20, read.TotalTasks);
                AssertEqual(11, read.PassedCount);
                AssertEqual(4, read.FailedCount);
                AssertEqual(3, read.CouldNotRunCount);
                AssertEqual(2, read.PendingEvaluationCount);
                AssertEqual("stopped", read.FailureReason);
                AssertSameInstant(_Fixed, read.CreatedUtc, "created");
                AssertSameInstant(_Fixed.AddSeconds(5), read.StartedUtc!.Value, "started");
                AssertSameInstant(_Fixed.AddMinutes(30), read.CompletedUtc!.Value, "completed");
                AssertEqual(0, read.Results.Count, "ReadAsync returns no results");
            }));

            cases.Add(CaseAsync("run_minimal_defaults", "A run with only defaults round-trips: empty packs, null provenance, evaluation true, run count 3", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                BatteryRun run = new BatteryRun();
                await db.BatteryRuns.CreateAsync(run).ConfigureAwait(false);
                BatteryRun? read = await db.BatteryRuns.ReadAsync(run.Id).ConfigureAwait(false);
                AssertNotNull(read, "read back");
                AssertEqual(0, read!.Packs.Count, "no packs");
                AssertEqual(3, read.RunCount, "default run count");
                AssertTrue(read.IsEvaluation, "evaluation by default");
                AssertEqual(BatteryRunStatusEnum.Queued, read.Status);
                AssertEqual("ModelEvaluation", read.Pipeline);
                AssertNull(read.Seed, "seed");
                AssertNull(read.SamplingJson, "sampling");
                AssertNull(read.ConfigurationRevision, "revision");
                AssertNull(read.StartedUtc, "started");
                AssertNull(read.CompletedUtc, "completed");
            }));

            cases.Add(CaseAsync("run_update_changes_status_and_counts", "Update persists status, counts and completion", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                BatteryRun run = new BatteryRun { TenantId = "ten_a", Packs = new List<string> { "csharp" } };
                await db.BatteryRuns.CreateAsync(run).ConfigureAwait(false);
                run.Status = BatteryRunStatusEnum.Running;
                run.PassedCount = 2;
                run.CouldNotRunCount = 1;
                run.StartedUtc = _Fixed;
                await db.BatteryRuns.UpdateAsync(run).ConfigureAwait(false);
                BatteryRun? read = await db.BatteryRuns.ReadAsync(run.Id).ConfigureAwait(false);
                AssertEqual(BatteryRunStatusEnum.Running, read!.Status);
                AssertEqual(2, read.PassedCount);
                AssertEqual(1, read.CouldNotRunCount);
                AssertSameInstant(_Fixed, read.StartedUtc!.Value, "started");
                AssertEqual("ten_a", read.TenantId, "tenant not changed by update");
            }));

            cases.Add(CaseAsync("result_roundtrip_with_details", "A task result round-trips every field, including the grader's evidence JSON", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                BatteryRun run = new BatteryRun { TenantId = "ten_full" };
                await db.BatteryRuns.CreateAsync(run).ConfigureAwait(false);
                BatteryTaskResult result = FullResult(run.Id);
                await db.BatteryTaskResults.CreateAsync(result).ConfigureAwait(false);

                List<BatteryTaskResult> read = await db.BatteryTaskResults.ReadByRunAsync(run.Id).ConfigureAwait(false);
                AssertEqual(1, read.Count, "one result");
                BatteryTaskResult r = read[0];
                AssertTrue(r.Id.StartsWith("btr_", StringComparison.Ordinal), "btr_ prefix: " + r.Id);
                AssertEqual(run.Id, r.RunId);
                AssertEqual("ten_full", r.TenantId);
                AssertEqual("csharp", r.PackId);
                AssertEqual(1, r.PackVersion);
                AssertEqual("judge-negative", r.TaskId);
                AssertEqual("Review a diff", r.TaskTitle);
                AssertEqual("Judge", r.Persona);
                AssertEqual(BatteryGraderTypeEnum.SeededSet, r.Grader);
                AssertEqual(2, r.Attempt);
                AssertEqual(BatteryTaskOutcomeEnum.Failed, r.Outcome);
                AssertEqual((double?)(2.0 / 3.0), r.Score);
                AssertEqual("Found 2 of 3", r.Summary);
                AssertEqual("missed one", r.Reason);
                AssertEqual("raw output\r\nwith CRLF", r.Output);
                AssertEqual("vsl_fix", r.VesselId);
                AssertEqual("vyg_x", r.VoyageId);
                AssertEqual("msn_x", r.MissionId);
                AssertEqual("WorkProduced", r.MissionStatus);
                AssertEqual("abc123", r.FixtureCommit);
                AssertEqual(98765L, r.DurationMs);
                AssertSameInstant(_Fixed, r.CreatedUtc, "created");
                AssertSameInstant(_Fixed.AddMinutes(2), r.CompletedUtc!.Value, "completed");
                AssertEqual(2, r.Details.Matched.Count, "matched");
                AssertEqual("off-by-one", r.Details.Matched[0].Id);
                AssertEqual("the loop starts at 1", r.Details.Matched[0].Finding);
                AssertEqual("integer-division", r.Details.Missed.Single());
                AssertEqual("invented defect", r.Details.FalsePositives.Single());
                AssertEqual("FAIL", r.Details.Verdict);
                AssertEqual(1, r.Details.Commands.Count, "commands");
                AssertEqual(2, r.Details.Commands[0].ExitCode);
                AssertEqual("src/a.cs", r.Details.Files.Single().Path);
                AssertEqual("The byte order mark was dropped (line endings kept).", r.Details.Files.Single().Reason);
                AssertTrue(r.Details.Mutants.Single().Killed, "mutant killed");
            }));

            cases.Add(CaseAsync("result_minimal_defaults", "A result with only defaults round-trips with empty evidence and null score", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                BatteryTaskResult result = new BatteryTaskResult { RunId = "bat_min" };
                await db.BatteryTaskResults.CreateAsync(result).ConfigureAwait(false);
                BatteryTaskResult r = (await db.BatteryTaskResults.ReadByRunAsync("bat_min").ConfigureAwait(false)).Single();
                AssertNull(r.Score, "score");
                AssertNull(r.Output, "output");
                AssertEqual(BatteryTaskOutcomeEnum.Failed, r.Outcome);
                AssertEqual(0, r.Details.Matched.Count);
                AssertEqual(0, r.Details.Commands.Count);
                AssertNull(r.CompletedUtc, "completed");
            }));

            cases.Add(CaseAsync("enumerate_filters_and_pages", "Enumerate filters by tenant, captain and status, newest first, and pages", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                for (int i = 0; i < 5; i++)
                {
                    BatteryRun run = new BatteryRun
                    {
                        TenantId = i < 3 ? "ten_a" : "ten_b",
                        CaptainId = i % 2 == 0 ? "cpt_even" : "cpt_odd",
                        Status = i == 4 ? BatteryRunStatusEnum.Running : BatteryRunStatusEnum.Completed,
                        CreatedUtc = _Fixed.AddMinutes(i)
                    };
                    await db.BatteryRuns.CreateAsync(run).ConfigureAwait(false);
                }

                EnumerationResult<BatteryRun> tenantA = await db.BatteryRuns.EnumerateAsync(new BatteryRunQuery { TenantId = "ten_a" }).ConfigureAwait(false);
                AssertEqual(3L, tenantA.TotalRecords, "tenant a");
                AssertTrue(tenantA.Objects[0].CreatedUtc > tenantA.Objects[2].CreatedUtc, "newest first");
                EnumerationResult<BatteryRun> even = await db.BatteryRuns.EnumerateAsync(new BatteryRunQuery { CaptainId = "cpt_even" }).ConfigureAwait(false);
                AssertEqual(3L, even.TotalRecords, "captain filter");
                EnumerationResult<BatteryRun> running = await db.BatteryRuns.EnumerateAsync(new BatteryRunQuery { Status = BatteryRunStatusEnum.Running }).ConfigureAwait(false);
                AssertEqual(1L, running.TotalRecords, "status filter");
                EnumerationResult<BatteryRun> page = await db.BatteryRuns.EnumerateAsync(new BatteryRunQuery { PageNumber = 2, PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(5L, page.TotalRecords, "total");
                AssertEqual(3, page.TotalPages, "pages");
                AssertEqual(2, page.Objects.Count, "page 2 size");
            }));

            cases.Add(CaseAsync("delete_removes_results", "Deleting a run removes its results; DeleteByRun counts rows", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                BatteryRun run = new BatteryRun();
                await db.BatteryRuns.CreateAsync(run).ConfigureAwait(false);
                await db.BatteryTaskResults.CreateAsync(new BatteryTaskResult { RunId = run.Id }).ConfigureAwait(false);
                await db.BatteryTaskResults.CreateAsync(new BatteryTaskResult { RunId = run.Id }).ConfigureAwait(false);
                BatteryRun other = new BatteryRun();
                await db.BatteryRuns.CreateAsync(other).ConfigureAwait(false);
                await db.BatteryTaskResults.CreateAsync(new BatteryTaskResult { RunId = other.Id }).ConfigureAwait(false);

                AssertTrue(await db.BatteryRuns.DeleteAsync(run.Id).ConfigureAwait(false), "deleted");
                AssertNull(await db.BatteryRuns.ReadAsync(run.Id).ConfigureAwait(false), "run gone");
                AssertEqual(0, (await db.BatteryTaskResults.ReadByRunAsync(run.Id).ConfigureAwait(false)).Count, "results gone");
                AssertEqual(1, await db.BatteryTaskResults.DeleteByRunAsync(other.Id).ConfigureAwait(false), "other run's result count");
                AssertFalse(await db.BatteryRuns.DeleteAsync("bat_missing").ConfigureAwait(false), "missing run");
            }));

            cases.Add(CaseAsync("results_enumerate_pages_in_database", "EnumerateByRunAsync pages one run's results with an exact total, outcome filter, both orders, an id tie-break and an empty page past the end", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                BatteryRun run = new BatteryRun();
                await db.BatteryRuns.CreateAsync(run).ConfigureAwait(false);
                BatteryRun other = new BatteryRun();
                await db.BatteryRuns.CreateAsync(other).ConfigureAwait(false);
                for (int i = 0; i < 7; i++)
                {
                    // Pairs share a timestamp so the id tie-break decides their order.
                    await db.BatteryTaskResults.CreateAsync(new BatteryTaskResult
                    {
                        RunId = run.Id,
                        TaskId = "t" + i,
                        Outcome = i % 2 == 0 ? BatteryTaskOutcomeEnum.Passed : BatteryTaskOutcomeEnum.Failed,
                        CreatedUtc = _Fixed.AddMinutes(i / 2)
                    }).ConfigureAwait(false);
                }

                await db.BatteryTaskResults.CreateAsync(new BatteryTaskResult { RunId = other.Id, Outcome = BatteryTaskOutcomeEnum.Passed }).ConfigureAwait(false);

                List<string> ascending = new List<string>();
                for (int pageNumber = 1; pageNumber <= 3; pageNumber++)
                {
                    EnumerationResult<BatteryTaskResult> page = await db.BatteryTaskResults.EnumerateByRunAsync(run.Id, new EnumerationQuery { PageNumber = pageNumber, PageSize = 3, Order = EnumerationOrderEnum.CreatedAscending }).ConfigureAwait(false);
                    AssertEqual(7L, page.TotalRecords, "total counts only this run");
                    AssertEqual(3, page.TotalPages, "pages");
                    AssertEqual(pageNumber < 3 ? 3 : 1, page.Objects.Count, "page " + pageNumber + " size");
                    ascending.AddRange(page.Objects.Select(r => r.Id));
                }

                AssertEqual(7, ascending.Distinct().Count(), "pages neither overlap nor skip");
                List<BatteryTaskResult> all = await db.BatteryTaskResults.ReadByRunAsync(run.Id).ConfigureAwait(false);
                List<string> expected = all.OrderBy(r => r.CreatedUtc).ThenBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Id).ToList();
                AssertEqual(String.Join(",", expected), String.Join(",", ascending), "created then id ascending");

                EnumerationResult<BatteryTaskResult> newest = await db.BatteryTaskResults.EnumerateByRunAsync(run.Id, new EnumerationQuery { PageSize = 7 }).ConfigureAwait(false);
                AssertEqual(String.Join(",", Enumerable.Reverse(expected)), String.Join(",", newest.Objects.Select(r => r.Id)), "default order is newest first with the id tie-break");

                EnumerationResult<BatteryTaskResult> passed = await db.BatteryTaskResults.EnumerateByRunAsync(run.Id, new EnumerationQuery { PageSize = 2, Status = "Passed" }).ConfigureAwait(false);
                AssertEqual(4L, passed.TotalRecords, "outcome filter applies before counting");
                AssertEqual(2, passed.TotalPages);
                AssertTrue(passed.Objects.All(r => r.Outcome == BatteryTaskOutcomeEnum.Passed), "filtered rows");

                EnumerationResult<BatteryTaskResult> past = await db.BatteryTaskResults.EnumerateByRunAsync(run.Id, new EnumerationQuery { PageNumber = 5, PageSize = 3 }).ConfigureAwait(false);
                AssertEqual(0, past.Objects.Count, "empty page past the end");
                AssertEqual(7L, past.TotalRecords, "accurate total past the end");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Database: battery runs and task results",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static BatteryRun FullRun()
        {
            return new BatteryRun
            {
                TenantId = "ten_full",
                UserId = "usr_full",
                CaptainId = "cpt_full",
                CaptainName = "Full captain",
                Packs = new List<string> { "csharp", "python" },
                BatteryVersion = "csharp@1,python@2",
                RunCount = 5,
                Status = BatteryRunStatusEnum.Completed,
                IsEvaluation = false,
                Pipeline = "ModelEvaluation",
                Seed = 424242,
                SamplingJson = "{\"temperature\":0.2}",
                ConfigurationRevision = "0123456789abcdef",
                ConfigurationJson = "{\"armadaVersion\":\"1.1.0\"}",
                Runtime = "ApiEndpoint",
                Model = "qwen3:32b",
                ModelEndpointId = "mep_full",
                InferenceHost = "gb10.local",
                TotalTasks = 20,
                PassedCount = 11,
                FailedCount = 4,
                CouldNotRunCount = 3,
                PendingEvaluationCount = 2,
                FailureReason = "stopped",
                CreatedUtc = _Fixed,
                StartedUtc = _Fixed.AddSeconds(5),
                CompletedUtc = _Fixed.AddMinutes(30)
            };
        }

        private static BatteryTaskResult FullResult(string runId)
        {
            BatteryGradeDetails details = new BatteryGradeDetails
            {
                Matched = new List<BatterySeededMatch>
                {
                    new BatterySeededMatch { Id = "off-by-one", Finding = "the loop starts at 1" },
                    new BatterySeededMatch { Id = "unknown-sku-null", Finding = "null dereference" }
                },
                Missed = new List<string> { "integer-division" },
                FalsePositives = new List<string> { "invented defect" },
                FindingCount = 3,
                Verdict = "FAIL",
                Commands = new List<BatteryCommandOutcome> { new BatteryCommandOutcome { CommandLine = "dotnet run", ExitCode = 2, Passed = false } },
                Files = new List<BatteryFileComparison> { new BatteryFileComparison { Path = "src/a.cs", Reason = "The byte order mark was dropped (line endings kept)." } },
                Mutants = new List<BatteryMutantOutcome> { new BatteryMutantOutcome { Id = "m1", Applied = true, Killed = true } }
            };
            return new BatteryTaskResult
            {
                RunId = runId,
                TenantId = "ten_full",
                PackId = "csharp",
                PackVersion = 1,
                TaskId = "judge-negative",
                TaskTitle = "Review a diff",
                Persona = "Judge",
                Grader = BatteryGraderTypeEnum.SeededSet,
                Attempt = 2,
                Outcome = BatteryTaskOutcomeEnum.Failed,
                Score = 2.0 / 3.0,
                Summary = "Found 2 of 3",
                Reason = "missed one",
                Details = details,
                Output = "raw output\r\nwith CRLF",
                VesselId = "vsl_fix",
                VoyageId = "vyg_x",
                MissionId = "msn_x",
                MissionStatus = "WorkProduced",
                FixtureCommit = "abc123",
                DurationMs = 98765,
                CreatedUtc = _Fixed,
                CompletedUtc = _Fixed.AddMinutes(2)
            };
        }

        private static void AssertSameInstant(DateTime expected, DateTime actual, string label)
        {
            double delta = Math.Abs((expected.ToUniversalTime() - actual.ToUniversalTime()).TotalMilliseconds);
            AssertTrue(delta < 1.0, label + ": expected " + expected.ToString("o") + " but was " + actual.ToString("o"));
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
