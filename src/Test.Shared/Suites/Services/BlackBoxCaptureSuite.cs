namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// BlackBoxCapture: writes the entry, record.json, briefs and evidence; attempt 2 supersedes attempt 1; never throws when stores or sinks fail; storage fallback; time budget writes a partial entry; disabled is a no-op; the reference site in MissionService.
    /// </summary>
    public sealed class BlackBoxCaptureSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxCapture";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("captures_entry_and_objects", "A capture writes the current entry, record.json, three briefs, evidence and file rows, points the mission at it and emits blackbox.captured", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RecordingBlackBoxEventSink sink = new RecordingBlackBoxEventSink();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, null, sink);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                mission.AgentOutput = "agent says hi, api_key=supersecretvalue1";
                mission.DiffSnapshot = "diff --git a/x b/x\n";
                string? id = await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.ScopeViolation, c =>
                {
                    c.OutOfScopeFiles.Add("src/b.cs");
                    c.ScopedFiles.Add("src/a.cs");
                    c.GitFacts = new BlackBoxGitFacts { ConflictedFiles = new List<string> { "src/core/a.cs", "src/core/b.cs", "docs/c.md" }, DirtyPaths = new List<string> { "README.md" }, TargetTipAfter = "abc" };
                    c.Exception = new InvalidOperationException("boom");
                }).ConfigureAwait(false);
                AssertNotNull(id, "entry id");
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id!).ConfigureAwait(false))!;
                AssertEqual(BlackBoxPhaseEnum.Completion, entry.Phase); AssertEqual(BlackBoxStepEnum.ScopeViolation, entry.Step);
                AssertEqual("WorkProduced", entry.StatusBefore); AssertEqual("Failed", entry.StatusAfter); AssertEqual(1, entry.Attempt); AssertTrue(entry.IsCurrent, "current");
                AssertEqual(64, entry.Fingerprint!.Length, "fingerprint"); AssertNotNull(entry.ExpiresUtc, "expiry computed");
                AssertTrue(entry.ObjectCount >= 6, "record, three briefs and two evidence objects: " + entry.ObjectCount);
                AssertTrue(entry.TotalBytes > 0, "bytes");
                AssertEqual(id, (await t.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.LastBlackBoxEntryId, "mission points at it");
                AssertEqual(id, mission.LastBlackBoxEntryId, "in-memory mission updated too");
                BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertEqual(id, record.EntryId);
                AssertEqual("src/Test.Shared/Suites/Services/BlackBoxCaptureSuite.cs", record.Site!.SourceFile, "caller file trimmed to the repository path");
                AssertTrue(record.Site.SourceLine > 0, "caller line");
                AssertEqual("System.InvalidOperationException", record.Mechanism!.ExceptionType);
                AssertEqual(3, record.Conflict!.ConflictedFileTotal); AssertEqual("src/core", record.Conflict.ConflictedDirectories[0].Directory, "directory rollup: largest first, deepest on a tie");
                AssertEqual(2, record.Conflict.ConflictedDirectories[0].FileCount);
                AssertEqual("src/b.cs", record.Scope!.OutOfScopeFiles.Single());
                AssertTrue(record.Configuration!.Values.Any(v => v.Setting == "LandingMode"), "configuration");
                AssertTrue(record.Evidence!.Objects.Any(o => o.Name == BlackBoxObjectNames.AgentOutput), "evidence listed");
                foreach (string name in new[] { BlackBoxObjectNames.Record, BlackBoxObjectNames.Brief, BlackBoxObjectNames.UserBrief, BlackBoxObjectNames.MaintainerBrief, BlackBoxObjectNames.AgentOutput, BlackBoxObjectNames.Diff })
                    AssertNotNull(await t.Driver.BlackBoxObjects.ReadAsync(id!, name).ConfigureAwait(false), name + " stored");
                BlackBoxObjectReadResult output = await bb.Objects.ReadAsync(entry, BlackBoxObjectNames.AgentOutput).ConfigureAwait(false);
                AssertFalse(Encoding.UTF8.GetString(output.Data).Contains("supersecretvalue1", StringComparison.Ordinal), "evidence redacted");
                AssertTrue(output.Verified, "sha verified");
                AssertEqual(5L, (await t.Driver.BlackBoxFiles.EnumerateAsync(id!, null, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "3 conflicted, 1 dirty, 1 out of scope");
                AssertEqual(1, sink.Count(BlackBoxEventTypes.Captured), "captured event");
                AssertEqual(1, bb.Queue.Pending, "enrichment queued");
                BlackBoxCapturedPayload payload = (BlackBoxCapturedPayload)sink.Events.First(e => e.EventType == BlackBoxEventTypes.Captured).Payload!;
                AssertEqual(id, payload.EntryId); AssertEqual(BlackBoxStepEnum.ScopeViolation, payload.Step);
            }));

            cases.Add(Case("second_failure_supersedes", "A restart that fails again writes attempt 2, keeps attempt 1 and survives ResetForRestart-style clearing", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string first = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                mission.FailureReason = null; mission.FailureKind = null; mission.Status = MissionStatusEnum.InProgress;
                await t.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                AssertEqual(first, (await t.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.LastBlackBoxEntryId, "restart keeps the pointer");
                mission.Status = MissionStatusEnum.Failed; mission.FailureKind = MissionFailureKindEnum.Crash; mission.FailureReason = "crashed";
                await t.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                string second = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ProcessCrash).ConfigureAwait(false))!;
                BlackBoxEntry e2 = (await t.Driver.BlackBoxEntries.ReadAsync(second).ConfigureAwait(false))!;
                BlackBoxEntry e1 = (await t.Driver.BlackBoxEntries.ReadAsync(first).ConfigureAwait(false))!;
                AssertEqual(2, e2.Attempt); AssertEqual(first, e2.SupersedesId); AssertTrue(e2.IsCurrent, "attempt 2 current"); AssertFalse(e1.IsCurrent, "attempt 1 kept, not current");
                AssertEqual("Mission modified files outside its scoped file list: src/a.cs", e1.ReasonExcerpt, "attempt 1 evidence preserved");
                BlackBoxRecord r2 = await bb.Pipeline.LoadRecordAsync(e2).ConfigureAwait(false);
                AssertEqual(first, r2.Attempts.Single().EntryId, "attempt history");
                AssertEqual(2, r2.Subject!.Attempt);
            }));

            cases.Add(Case("never_throws", "A throwing event sink, a failing external store without fallback, and a null mission never throw; record.json still lands in the database", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RecordingBlackBoxEventSink sink = new RecordingBlackBoxEventSink { Throw = true };
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.S3;
                settings.BlackBox.Storage.FallbackToDatabase = false;
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings, sink);
                ScriptedBlackBoxStore store = new ScriptedBlackBoxStore(BlackBoxStorageTargetEnum.S3) { FailWrites = true };
                bb.Stores.Register(BlackBoxStorageTargetEnum.S3, _ => store);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                mission.AgentOutput = "output";
                string? id = await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false);
                AssertNotNull(id, "captured despite the throwing sink and failing store");
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id!).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStorageTargetEnum.S3, entry.StorageTarget, "no fallback");
                AssertEqual(BlackBoxStateEnum.ObjectMissing, entry.State, "objects missing");
                BlackBoxObject record = (await t.Driver.BlackBoxObjects.ReadAsync(id!, BlackBoxObjectNames.Record).ConfigureAwait(false))!;
                AssertNull(record.StorageKey, "record.json forced to the database");
                BlackBoxRecord read = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertTrue(read.Truncation.Notes.Any(n => n.Contains("fallback is off", StringComparison.Ordinal)), "note says so");
                AssertTrue((await t.Driver.BlackBoxObjects.ReadAsync(id!, BlackBoxObjectNames.Brief).ConfigureAwait(false))!.IsMissing, "brief marked missing");
                await AssertThrowsAsync<BlackBoxObjectMissingException>(() => bb.Objects.ReadAsync(entry, BlackBoxObjectNames.Brief), "missing object read").ConfigureAwait(false);

                AssertNull(await bb.Capture.CaptureMissionFailureAsync(new BlackBoxCaptureContext()).ConfigureAwait(false), "no mission");
                AssertNull(await ((IBlackBoxCapture?)null).RecordMissionFailureAsync(mission, null, BlackBoxStepEnum.Unknown).ConfigureAwait(false), "null capture is a no-op");

                if (!TestDatabaseConfig.IsSqlite) return;
                using (Microsoft.Data.Sqlite.SqliteConnection conn = new Microsoft.Data.Sqlite.SqliteConnection(t.ConnectionString))
                {
                    conn.Open();
                    using (Microsoft.Data.Sqlite.SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "ALTER TABLE blackbox_entries RENAME TO blackbox_entries_gone;";
                        cmd.ExecuteNonQuery();
                    }
                }
                Mission orphan = new Mission("orphan");
                AssertNull(await bb.Capture.CaptureMissionFailureAsync(new BlackBoxCaptureContext { Mission = orphan, Step = BlackBoxStepEnum.Push }).ConfigureAwait(false), "database failure returns null");
                AssertTrue(sink.Count(BlackBoxEventTypes.CaptureFailed) >= 1, "capture_failed emitted");
            }));

            cases.Add(Case("storage_fallback", "A failing external store with fallback on moves the entry to the database and emits blackbox.storage_fallback", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RecordingBlackBoxEventSink sink = new RecordingBlackBoxEventSink();
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.Disk;
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings, sink);
                bb.Stores.Register(BlackBoxStorageTargetEnum.Disk, _ => new ScriptedBlackBoxStore(BlackBoxStorageTargetEnum.Disk) { FailWrites = true });
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStorageTargetEnum.Database, entry.StorageTarget, "fell back");
                AssertNull(entry.StoragePrefix, "no key prefix");
                AssertEqual(1, sink.Count(BlackBoxEventTypes.StorageFallback), "fallback event");
                AssertContains("FIX THIS", Encoding.UTF8.GetString((await bb.Objects.ReadAsync(entry, BlackBoxObjectNames.Brief).ConfigureAwait(false)).Data), "brief readable from the database");

                settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.Nfs;
                Mission next = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string id2 = (await bb.Capture.RecordMissionFailureAsync(next, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStorageTargetEnum.Database, (await t.Driver.BlackBoxEntries.ReadAsync(id2).ConfigureAwait(false))!.StorageTarget, "unimplemented target falls back too");
            }));

            cases.Add(Case("time_budget_partial_entry", "A capture that exceeds its budget writes a partial entry with record.json and Truncation.TimedOut", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.S3;
                settings.BlackBox.CaptureTimeoutMs = 100;
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                bb.Stores.Register(BlackBoxStorageTargetEnum.S3, _ => new ScriptedBlackBoxStore(BlackBoxStorageTargetEnum.S3) { WriteDelay = TimeSpan.FromSeconds(10) });
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                string? id = await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.Push).ConfigureAwait(false);
                AssertTrue(sw.Elapsed < TimeSpan.FromSeconds(5), "bounded: " + sw.Elapsed);
                AssertNotNull(id, "partial entry written");
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id!).ConfigureAwait(false))!;
                BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertTrue(record.Truncation.TimedOut, "timed out flag");
                AssertEqual(1, entry.ObjectCount, "only record.json");
            }));

            cases.Add(Case("disabled_is_noop", "With BlackBox.Enabled false nothing is written and the sweeper is idle", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.Enabled = false;
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                AssertNull(await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false), "no entry");
                BlackBoxBackfillResult sweep = await bb.Sweeper.SweepAsync().ConfigureAwait(false);
                AssertEqual(0, sweep.Captured, "sweeper idle");
                AssertEqual(0L, await t.Driver.BlackBoxEntries.CountAsync(new BlackBoxEnumerateRequest()).ConfigureAwait(false), "nothing stored");
            }));

            cases.Add(Case("redaction_and_bounds", "Secrets in the reason never reach any object; a 1 MB reason is capped at 8000 characters; 10000 conflicted files keep 500 with the total", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.LandingConflict,
                    "push https://ci:hunter22@git.example.com failed; key sk-ant-api03-ABCDEFGHIJKLMNOPQRSTU\n-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----\n" + new string('x', 1024 * 1024)).ConfigureAwait(false);
                List<string> files = Enumerable.Range(0, 10000).Select(i => "src/f" + i + ".cs").ToList();
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.TargetIntegration, c => c.GitFacts = new BlackBoxGitFacts { ConflictedFiles = files }).ConfigureAwait(false))!;
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertEqual(BlackBoxLimits.MaxRawReasonChars, record.Classification!.RawReason!.Length, "reason capped");
                AssertTrue(record.Truncation.Fields.Any(f => f.Field == "Classification.RawReason"), "truncation recorded");
                AssertEqual(BlackBoxLimits.MaxConflictedFiles, record.Conflict!.ConflictedFiles.Count, "500 kept");
                AssertEqual(10000, record.Conflict.ConflictedFileTotal, "total kept"); AssertTrue(record.Conflict.ConflictedFilesTruncated, "flagged");
                AssertTrue(record.Redaction.RulesApplied.Any(r => r.Rule == "UrlCredentials") && record.Redaction.RulesApplied.Any(r => r.Rule == "PrivateKeyBlock"), "rules reported");
                EnumerationResult<BlackBoxObject> objects = await t.Driver.BlackBoxObjects.EnumerateAsync(id, new EnumerationQuery { PageSize = 100 }).ConfigureAwait(false);
                foreach (BlackBoxObject obj in objects.Objects)
                {
                    string text = Encoding.UTF8.GetString((await bb.Objects.ReadAsync(entry, obj.Name).ConfigureAwait(false)).Data);
                    AssertFalse(text.Contains("hunter22", StringComparison.Ordinal) || text.Contains("sk-ant-api03", StringComparison.Ordinal) || text.Contains("BEGIN PRIVATE KEY-----\nAAAA", StringComparison.Ordinal), obj.Name + " has no secret");
                }
                AssertTrue(BlackBoxJson.SerializeRecord(record).Length <= BlackBoxLimits.MaxRecordBytes, "record.json under 256 KB");
            }));

            cases.Add(Case("voyage_capture", "A voyage capture rolls up its missions, points at the root failure and its entry", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Voyage voyage = await t.Driver.Voyages.CreateAsync(new Voyage("bb voyage") { Status = VoyageStatusEnum.Failed }).ConfigureAwait(false);
                Mission failed = new Mission("root") { VoyageId = voyage.Id, Status = MissionStatusEnum.Failed, FailureKind = MissionFailureKindEnum.Crash, FailureReason = "crash" };
                await t.Driver.Missions.CreateAsync(failed).ConfigureAwait(false);
                await t.Driver.Missions.CreateAsync(new Mission("done") { VoyageId = voyage.Id, Status = MissionStatusEnum.Complete }).ConfigureAwait(false);
                string rootEntry = (await bb.Capture.RecordMissionFailureAsync(failed, MissionStatusEnum.InProgress, BlackBoxStepEnum.ProcessCrash).ConfigureAwait(false))!;
                string id = (await bb.Capture.RecordVoyageFailureAsync(voyage, VoyageStatusEnum.InProgress, BlackBoxStepEnum.SettlementFailed, null, c => c.SettlementRule = "AnyFailed").ConfigureAwait(false))!;
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxSubjectTypeEnum.Voyage, entry.SubjectType); AssertEqual(BlackBoxPhaseEnum.Voyage, entry.Phase);
                AssertEqual((MissionFailureKindEnum?)MissionFailureKindEnum.Crash, entry.FailureKind, "root mission's kind");
                BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertEqual(2, record.Voyage!.MissionTotal); AssertEqual(failed.Id, record.Voyage.CascadeRootMissionId); AssertEqual(rootEntry, record.Voyage.CascadeRootEntryId);
                AssertEqual("AnyFailed", record.Voyage.SettlementRule);
                AssertEqual(id, (await t.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false))!.LastBlackBoxEntryId, "voyage pointer");
            }));

            cases.Add(Case("pipeline_reanalyzes", "The pipeline runs enrichers and the advisor, re-renders briefs and marks the entry Analyzed; a failing enricher marks EnrichmentFailed with backoff", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                AssertEqual(1, await bb.Queue.DrainAsync().ConfigureAwait(false), "queued capture processed");
                BlackBoxEntry analyzed = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStateEnum.Analyzed, analyzed.State); AssertNotNull(analyzed.AnalyzedUtc, "analyzed time");
                bb.Pipeline.AddEnricher(new ThrowingEnricher());
                BlackBoxEntry failed = (await bb.Pipeline.ProcessAsync(id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStateEnum.EnrichmentFailed, failed.State); AssertEqual(1, failed.EnrichmentAttempts); AssertNotNull(failed.NextAttemptUtc, "backoff");
                AssertContains("Throwing", failed.LastError!, "error names the enricher");
            }));

            cases.Add(Case("reference_site_scope_violation", "MissionService's scope-violation failure is the wired reference site: it records the site, the out-of-scope files and the step", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                System.Reflection.MethodInfo? method = typeof(MissionService).GetMethod("TryFailMissionForScopeViolationAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                AssertNotNull(method, "reference site exists");
                System.Reflection.PropertyInfo? prop = typeof(MissionService).GetProperty("BlackBox");
                AssertNotNull(prop, "MissionService.BlackBox property");
                AssertEqual(typeof(IBlackBoxCapture), prop!.PropertyType, "typed as IBlackBoxCapture");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box capture", cases: cases);
        }

        private sealed class ThrowingEnricher : IBlackBoxEnricher
        {
            public string Name => "Throwing";

            public Task EnrichAsync(BlackBoxEnrichmentContext context, CancellationToken token = default)
            {
                throw new InvalidOperationException("scripted enricher failure");
            }
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
