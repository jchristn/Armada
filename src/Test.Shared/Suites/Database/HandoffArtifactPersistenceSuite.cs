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
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// SQLite persistence of handoff artifacts (migration 97): a fully populated artifact and a minimal one round-trip,
    /// the newest artifact of a mission is found, paging and filters work, updates keep the id, tenant and creation time,
    /// unreadable list columns read as empty lists, and the data expiry removes artifacts of deleted missions.
    /// </summary>
    public sealed class HandoffArtifactPersistenceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.HandoffArtifacts";
        private static readonly DateTime _Fixed = new DateTime(2026, 10, 27, 8, 9, 10, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("roundtrip_all_fields", "A fully populated handoff artifact round-trips every field", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                HandoffArtifact artifact = Full("msn_full");
                await db.HandoffArtifacts.CreateAsync(artifact).ConfigureAwait(false);

                HandoffArtifact? read = await db.HandoffArtifacts.ReadAsync(artifact.Id).ConfigureAwait(false);
                AssertNotNull(read, "read back");
                AssertTrue(read!.Id.StartsWith("hoa_", StringComparison.Ordinal), "hoa_ prefix: " + read.Id);
                AssertEqual(1, read.SchemaVersion, "schema version");
                AssertEqual("ten_full", read.TenantId);
                AssertEqual("usr_full", read.UserId);
                AssertEqual("msn_full", read.MissionId);
                AssertEqual("vyg_full", read.VoyageId);
                AssertEqual("vsl_full", read.VesselId);
                AssertEqual("sto_full", read.StageOutcomeId);
                AssertEqual("Judge", read.Persona);
                AssertEqual("cpt_full", read.CaptainId);
                AssertEqual("Full captain", read.CaptainName);
                AssertEqual(3, read.Attempt);
                AssertEqual(2, read.ReworkRound);
                AssertEqual("NEEDS_REVISION", read.Verdict);
                AssertEqual("armada/full", read.BranchName);
                AssertEqual("abc1234", read.CommitHash);
                AssertEqual(2, read.Findings.Count, "findings");
                AssertEqual(1, read.Findings[0].Number);
                AssertEqual("src/Parser.cs", read.Findings[0].File);
                AssertEqual((int?)42, read.Findings[0].Line);
                AssertEqual(HandoffFindingSeverityEnum.Blocking, read.Findings[0].Severity);
                AssertEqual("Guard the empty input.", read.Findings[0].RequiredFix);
                AssertEqual("File: src/Parser.cs:42\nScenario: empty input returns success", read.Findings[0].Summary, "multi-line summary intact");
                AssertNull(read.Findings[1].File, "finding without a file");
                AssertNull(read.Findings[1].Line, "finding without a line");
                AssertEqual(HandoffFindingSeverityEnum.NonBlocking, read.Findings[1].Severity);
                AssertEqual("AC-1: parse empty input|AC-2: report errors", String.Join("|", read.AcceptanceCriteria));
                AssertEqual("Resolve finding 1.", read.RequiredActions.Single());
                StageCheckResult check = read.CheckResults.Single();
                AssertEqual(StageCheckStatusEnum.Failed, check.Status);
                AssertEqual(StageCheckReasonEnum.None, check.Reason);
                AssertEqual("dotnet build|dotnet test", String.Join("|", check.Commands));
                AssertEqual("dotnet test", check.FailedCommand);
                AssertEqual((int?)1, check.ExitCode);
                AssertEqual("dotnet build", check.PreExistingFailures.Single());
                AssertEqual("Tests failed.", check.FailureMessage);
                AssertEqual(1234L, check.DurationMs);
                AssertEqual(_Fixed, check.CheckedUtc.ToUniversalTime(), "check time");
                AssertEqual("abc1234|def5678", String.Join("|", read.CommitReferences));
                AssertEqual("[ARMADA:VERDICT] NEEDS_REVISION", read.CompletionLines.Single());
                AssertEqual("full output \u00e9", read.Output, "output (non-ASCII kept)");
                AssertEqual(16, read.OutputLength);
                AssertTrue(read.OutputComplete, "output complete");
                AssertEqual("diff --git a/x b/x\n", read.Diff);
                AssertEqual(19, read.DiffLength);
                AssertTrue(read.DiffComplete, "diff complete");
                AssertEqual("Branch armada/full", read.DiffPointer);
                AssertEqual(_Fixed, read.CreatedUtc.ToUniversalTime(), "created");
            }));

            cases.Add(CaseAsync("roundtrip_minimal_and_incomplete", "A minimal artifact with an oversized (unstored) output and diff round-trips with empty lists and the incomplete flags", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HandoffArtifact artifact = new HandoffArtifact { MissionId = "msn_min", OutputLength = 2000000, OutputComplete = false, DiffLength = 3000000, DiffComplete = false };
                await testDb.Driver.HandoffArtifacts.CreateAsync(artifact).ConfigureAwait(false);
                HandoffArtifact read = (await testDb.Driver.HandoffArtifacts.ReadAsync(artifact.Id).ConfigureAwait(false))!;
                AssertNull(read.TenantId, "no tenant");
                AssertNull(read.StageOutcomeId, "no outcome");
                AssertNull(read.Verdict, "no verdict");
                AssertNull(read.Output, "no output");
                AssertNull(read.Diff, "no diff");
                AssertEqual(2000000, read.OutputLength);
                AssertFalse(read.OutputComplete, "output incomplete");
                AssertEqual(3000000, read.DiffLength);
                AssertFalse(read.DiffComplete, "diff incomplete");
                AssertEqual(0, read.Findings.Count, "findings empty");
                AssertEqual(0, read.AcceptanceCriteria.Count, "criteria empty");
                AssertEqual(0, read.RequiredActions.Count, "actions empty");
                AssertEqual(0, read.CheckResults.Count, "checks empty");
                AssertEqual(0, read.CommitReferences.Count, "commits empty");
                AssertEqual(0, read.CompletionLines.Count, "lines empty");
                AssertNull(await testDb.Driver.HandoffArtifacts.ReadAsync("hoa_missing").ConfigureAwait(false), "missing id");
                AssertNull(await testDb.Driver.HandoffArtifacts.ReadLatestByMissionAsync("msn_none").ConfigureAwait(false), "no artifact for mission");
                AssertEqual(0, HandoffArtifactJson.Read<HandoffFinding>("not json").Count, "unreadable column reads empty");
                AssertEqual(0, HandoffArtifactJson.Read<string>(null).Count, "null column reads empty");
            }));

            cases.Add(CaseAsync("latest_enumerate_update_delete", "The newest artifact of a mission is found; enumerate pages and filters newest first; update keeps id, tenant and creation time; delete by mission", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                HandoffArtifact older = Full("msn_a");
                older.CreatedUtc = _Fixed.AddMinutes(-10);
                HandoffArtifact newer = Full("msn_a");
                newer.CreatedUtc = _Fixed;
                HandoffArtifact other = Full("msn_b");
                other.TenantId = "ten_other";
                other.VoyageId = "vyg_other";
                await db.HandoffArtifacts.CreateAsync(older).ConfigureAwait(false);
                await db.HandoffArtifacts.CreateAsync(newer).ConfigureAwait(false);
                await db.HandoffArtifacts.CreateAsync(other).ConfigureAwait(false);

                AssertEqual(newer.Id, (await db.HandoffArtifacts.ReadLatestByMissionAsync("msn_a").ConfigureAwait(false))!.Id, "newest of the mission");

                EnumerationResult<HandoffArtifact> page = await db.HandoffArtifacts.EnumerateAsync(new HandoffArtifactQuery { MissionId = "msn_a", PageSize = 1 }).ConfigureAwait(false);
                AssertEqual(2, (int)page.TotalRecords, "two for the mission");
                AssertEqual(2, page.TotalPages, "two pages");
                AssertEqual(newer.Id, page.Objects.Single().Id, "newest first");
                EnumerationResult<HandoffArtifact> second = await db.HandoffArtifacts.EnumerateAsync(new HandoffArtifactQuery { MissionId = "msn_a", PageSize = 1, PageNumber = 2 }).ConfigureAwait(false);
                AssertEqual(older.Id, second.Objects.Single().Id, "second page");
                AssertEqual(1, (int)(await db.HandoffArtifacts.EnumerateAsync(new HandoffArtifactQuery { TenantId = "ten_other" }).ConfigureAwait(false)).TotalRecords, "tenant filter");
                AssertEqual(1, (int)(await db.HandoffArtifacts.EnumerateAsync(new HandoffArtifactQuery { VoyageId = "vyg_other" }).ConfigureAwait(false)).TotalRecords, "voyage filter");
                AssertEqual(3, (int)(await db.HandoffArtifacts.EnumerateAsync(new HandoffArtifactQuery { PageSize = 0, PageNumber = 0 }).ConfigureAwait(false)).TotalRecords, "clamped paging");

                newer.StageOutcomeId = "sto_linked";
                newer.TenantId = "ten_changed";
                newer.Findings.Clear();
                await db.HandoffArtifacts.UpdateAsync(newer).ConfigureAwait(false);
                HandoffArtifact updated = (await db.HandoffArtifacts.ReadAsync(newer.Id).ConfigureAwait(false))!;
                AssertEqual("sto_linked", updated.StageOutcomeId, "outcome linked");
                AssertEqual("ten_full", updated.TenantId, "tenant is not rewritten");
                AssertEqual(0, updated.Findings.Count, "findings rewritten");
                AssertEqual(_Fixed, updated.CreatedUtc.ToUniversalTime(), "creation time kept");

                AssertEqual(2, await db.HandoffArtifacts.DeleteByMissionAsync("msn_a").ConfigureAwait(false), "deleted by mission");
                AssertNull(await db.HandoffArtifacts.ReadLatestByMissionAsync("msn_a").ConfigureAwait(false), "gone");
                AssertNotNull(await db.HandoffArtifacts.ReadAsync(other.Id).ConfigureAwait(false), "other mission kept");
            }));

            cases.Add(CaseAsync("expiry_removes_orphaned_artifacts", "Data expiry removes the artifacts of missions that no longer exist and keeps the others", TestTags.Positive, async () =>
            {
                // DataExpiryService takes a SQLite connection string; the Admiral runs it only on SQLite.
                if (!TestDatabaseConfig.IsSqlite) return;
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Mission live = await db.Missions.CreateAsync(new Mission("Live", "Still here")).ConfigureAwait(false);
                HandoffArtifact kept = await db.HandoffArtifacts.CreateAsync(new HandoffArtifact { MissionId = live.Id }).ConfigureAwait(false);
                HandoffArtifact orphan = await db.HandoffArtifacts.CreateAsync(new HandoffArtifact { MissionId = "msn_deleted" }).ConfigureAwait(false);

                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                DataExpiryService expiry = new DataExpiryService(logging, testDb.ConnectionString, 30);
                await expiry.PurgeExpiredDataAsync().ConfigureAwait(false);

                AssertNotNull(await db.HandoffArtifacts.ReadAsync(kept.Id).ConfigureAwait(false), "artifact of a live mission kept");
                AssertNull(await db.HandoffArtifacts.ReadAsync(orphan.Id).ConfigureAwait(false), "artifact of a deleted mission removed");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Database: handoff artifacts",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static HandoffArtifact Full(string missionId)
        {
            HandoffArtifact artifact = new HandoffArtifact
            {
                TenantId = "ten_full",
                UserId = "usr_full",
                MissionId = missionId,
                VoyageId = "vyg_full",
                VesselId = "vsl_full",
                StageOutcomeId = "sto_full",
                Persona = "Judge",
                CaptainId = "cpt_full",
                CaptainName = "Full captain",
                Attempt = 3,
                ReworkRound = 2,
                Verdict = "NEEDS_REVISION",
                BranchName = "armada/full",
                CommitHash = "abc1234",
                Output = "full output \u00e9",
                OutputLength = 16,
                Diff = "diff --git a/x b/x\n",
                DiffLength = 19,
                DiffPointer = "Branch armada/full",
                CreatedUtc = _Fixed
            };
            artifact.Findings.Add(new HandoffFinding { Number = 1, File = "src/Parser.cs", Line = 42, Severity = HandoffFindingSeverityEnum.Blocking, Summary = "File: src/Parser.cs:42\nScenario: empty input returns success", RequiredFix = "Guard the empty input." });
            artifact.Findings.Add(new HandoffFinding { Number = 2, Severity = HandoffFindingSeverityEnum.NonBlocking, Summary = "Optional: rename a helper" });
            artifact.AcceptanceCriteria.Add("AC-1: parse empty input");
            artifact.AcceptanceCriteria.Add("AC-2: report errors");
            artifact.RequiredActions.Add("Resolve finding 1.");
            artifact.CheckResults.Add(new StageCheckResult
            {
                Status = StageCheckStatusEnum.Failed,
                Commands = new List<string> { "dotnet build", "dotnet test" },
                FailedCommand = "dotnet test",
                ExitCode = 1,
                PreExistingFailures = new List<string> { "dotnet build" },
                FailureMessage = "Tests failed.",
                DurationMs = 1234,
                CheckedUtc = _Fixed
            });
            artifact.CommitReferences.Add("abc1234");
            artifact.CommitReferences.Add("def5678");
            artifact.CompletionLines.Add("[ARMADA:VERDICT] NEEDS_REVISION");
            return artifact;
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
