namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.BlackBox.Enrichment;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The built-in black box enrichers (plan 16.3) against real temporary git repositories and a real database: conflict
    /// attribution to a landed Armada mission (trailer) and to a commit outside Armada (author), directory roll-up,
    /// workspace facts from a stand-in Harbor host, a disconnected or unresponsive Harbor (unknown, no exception), a
    /// missing start commit (target movement omitted with a reason), a deleted branch, an empty record, large inputs
    /// (truncation entries), redaction of evidence, concurrent overlap, attempts and the rescue chain, voyage cascade,
    /// and re-running without duplicate rows.
    /// </summary>
    public sealed class BlackBoxEnrichmentSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.BlackBoxEnrichment";
        private const string HarborId = "hbr_standin";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("landed_armada_mission_attributed", "A conflict with a mission that landed on the target (Armada-Mission-Id trailer) names that mission, its commit and the overlapping files, with a LandedOnTargetSinceStart link", TestTags.Positive, async () =>
            {
                using Fixture f = await Fixture.CreateAsync().ConfigureAwait(false);
                Mission landed = await f.AddMissionAsync("Landed refactor of a.cs", MissionStatusEnum.Complete).ConfigureAwait(false);
                string landedCommit = f.LandOnMain("Refactor a.cs\n\nArmada-Mission-Id: " + landed.Id, "Armada", "armada@example.com", "src/core/a.cs", "theirs\n");
                string entryId = await f.CaptureConflictAsync("src/core/a.cs").ConfigureAwait(false);

                BlackBoxEntry entry = await f.ProcessAsync(entryId).ConfigureAwait(false);
                AssertEqual(BlackBoxStateEnum.Analyzed, entry.State, "analyzed: " + entry.LastError);
                BlackBoxRecord record = await f.Bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                BlackBoxConflictingMission cm = record.Conflict!.ConflictingMissions.Single();
                AssertEqual(landed.Id, cm.MissionId);
                AssertEqual(landedCommit, cm.CommitHash);
                AssertEqual("Landed refactor of a.cs", cm.Title, "title from the mission row");
                AssertFalse(cm.IsOutsideArmada, "Armada commit");
                AssertEqual("src/core/a.cs", cm.OverlappingFiles.Single());
                AssertEqual(1, record.Conflict.TargetCommitTotal);
                AssertEqual(landed.Id, record.Conflict.TargetCommitsSinceStart.Single().MissionId);

                List<BlackBoxLink> links = await f.LinksAsync(entryId).ConfigureAwait(false);
                BlackBoxLink link = links.Single(l => l.Relation == BlackBoxLinkRelationEnum.LandedOnTargetSinceStart);
                AssertEqual(landed.Id, link.MissionId);
                AssertEqual(landedCommit, link.CommitHash);
                AssertEqual(1, link.OverlapFileCount);
                List<BlackBoxFile> files = await f.FilesAsync(entryId).ConfigureAwait(false);
                AssertTrue(files.Any(r => r.Role == BlackBoxFileRoleEnum.ChangedByConflictingMission && r.Path == "src/core/a.cs" && r.ConflictingMissionId == landed.Id), "conflicting-mission file row");
                AssertTrue(files.Any(r => r.Role == BlackBoxFileRoleEnum.ChangedByMission && r.Path == "src/core/a.cs"), "changed-by-mission row");
                AssertTrue(files.Any(r => r.Role == BlackBoxFileRoleEnum.Conflicted && r.Path == "src/core/a.cs"), "captured conflicted row kept");

                string commits = await f.ReadTextAsync(entry, BlackBoxObjectNames.TargetCommits).ConfigureAwait(false);
                AssertContains(landedCommit, commits);
                AssertContains("Armada mission " + landed.Id, commits);

                BlackBoxWorkspace w = record.Workspace!;
                AssertEqual(true, w.WorktreeExists);
                AssertEqual(true, w.BranchExists);
                AssertEqual(f.MissionCommit, w.BranchTip);
                AssertEqual(1, w.CommitsBehindTarget);
                AssertEqual(1, w.CommitsAheadOfTarget);
                AssertEqual("Admiral", w.InspectedHost);
                AssertNotNull(await f.Bb.Objects.ReadAsync(entry, BlackBoxObjectNames.GitStatus).ConfigureAwait(false), "git status evidence");
            }));

            cases.Add(Case("outside_armada_commit_with_author", "A conflicting commit without the trailer is reported as outside Armada with its author, and gets no mission link", TestTags.Positive, async () =>
            {
                using Fixture f = await Fixture.CreateAsync().ConfigureAwait(false);
                string hotfix = f.LandOnMain("Hotfix by hand", "Dana Human", "dana@example.com", "src/core/b.cs", "hotfix\n");
                string entryId = await f.CaptureConflictAsync("src/core/b.cs").ConfigureAwait(false);

                BlackBoxEntry entry = await f.ProcessAsync(entryId).ConfigureAwait(false);
                BlackBoxRecord record = await f.Bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                BlackBoxConflictingMission cm = record.Conflict!.ConflictingMissions.Single();
                AssertTrue(cm.IsOutsideArmada, "outside Armada");
                AssertNull(cm.MissionId);
                AssertEqual("Dana Human <dana@example.com>", cm.Author);
                AssertEqual(hotfix, cm.CommitHash);
                AssertEqual("src/core/b.cs", cm.OverlappingFiles.Single());
                AssertFalse((await f.LinksAsync(entryId).ConfigureAwait(false)).Any(l => l.Relation == BlackBoxLinkRelationEnum.LandedOnTargetSinceStart), "no mission link");
                string brief = await f.ReadTextAsync(entry, BlackBoxObjectNames.Brief).ConfigureAwait(false);
                AssertContains("Commit outside Armada by Dana Human", brief);
            }));

            cases.Add(Case("directory_rollup_three_files", "Three conflicted files in one directory roll up to that directory with count 3, which is also the deepest directory with several conflicts", TestTags.Positive, async () =>
            {
                using Fixture f = await Fixture.CreateAsync().ConfigureAwait(false);
                string entryId = await f.CaptureConflictAsync("src/core/a.cs", "src/core/b.cs", "src/core/c.cs", "docs/readme.md").ConfigureAwait(false);
                BlackBoxEntry entry = await f.ProcessAsync(entryId).ConfigureAwait(false);
                BlackBoxConflict c = (await f.Bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false)).Conflict!;
                BlackBoxDirectoryCount core = c.ConflictedDirectories.Single(d => d.Directory == "src/core");
                AssertEqual(3, core.FileCount);
                AssertEqual(2, core.Depth);
                AssertEqual("src/core", c.ConflictedDirectories[0].Directory, "largest first, deepest on a tie");
                AssertEqual("src/core", c.DeepestConflictedDirectory);
                AssertEqual(3, c.DeepestConflictedDirectoryFileCount);
                AssertEqual(3, c.ConflictedParentDirectories!.Single(d => d.Directory == "src/core").FileCount);
                AssertEqual(1, c.ConflictedParentDirectories!.Single(d => d.Directory == "docs").FileCount);

                List<BlackBoxDirectoryCount> deep = BlackBoxDirectoryRollupEnricher.RollUp(new[] { "a/b/c/d/x.cs", "a/b/c/d/y.cs" });
                AssertFalse(deep.Any(d => d.Depth > 3), "roll-up stops at depth 3");
                AssertEqual("a/b/c/d", BlackBoxDirectoryRollupEnricher.Deepest(new[] { "a/b/c/d/x.cs", "a/b/c/d/y.cs", "a/z.cs" })!.Directory, "deepest at any depth");
                AssertNull(BlackBoxDirectoryRollupEnricher.Deepest(new[] { "a/x.cs", "b/y.cs" }), "no directory holds two");
            }));

            cases.Add(Case("harbor_hosted_dock", "A Harbor-hosted dock is inspected on its Harbor (stand-in host): workspace and target commits come through the Harbor's command seam", TestTags.Positive, async () =>
            {
                using Fixture f = await Fixture.CreateAsync(harborDock: true).ConfigureAwait(false);
                RecordingHostCommandExecutor harbor = new RecordingHostCommandExecutor();
                f.Bb.Inspector.HarborHostOverride = id => id == HarborId ? new DockHost(id, new GitService(f.Logging, harbor), harbor, new LocalDockFileSystem()) : null;
                Mission landed = await f.AddMissionAsync("Remote landing", MissionStatusEnum.Complete).ConfigureAwait(false);
                f.LandOnMain("Remote landing\n\nArmada-Mission-Id: " + landed.Id, "Armada", "armada@example.com", "src/core/a.cs", "theirs\n");
                string entryId = await f.CaptureConflictAsync("src/core/a.cs").ConfigureAwait(false);

                BlackBoxEntry entry = await f.ProcessAsync(entryId).ConfigureAwait(false);
                BlackBoxRecord record = await f.Bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                BlackBoxWorkspace w = record.Workspace!;
                AssertEqual("Harbor:" + HarborId, w.InspectedHost);
                AssertEqual("Connected", w.HarborLinkState);
                AssertEqual(true, w.WorktreeExists);
                AssertEqual(f.MissionCommit, w.BranchTip);
                AssertEqual(landed.Id, record.Conflict!.ConflictingMissions.Single().MissionId, "attribution from the Harbor's repository");
                AssertTrue(harbor.Commands.Any(c => c.StartsWith("git log", StringComparison.Ordinal)), "git ran on the Harbor: " + String.Join(" | ", harbor.Commands.Take(10)));
                AssertTrue(harbor.Commands.Any(c => c.StartsWith("git status", StringComparison.Ordinal)), "status ran on the Harbor");
            }));

            cases.Add(Case("disconnected_harbor_unknown", "A disconnected Harbor leaves the workspace unknown with a note, omits target movement, and never throws; an unresponsive one degrades the same way", TestTags.Negative, async () =>
            {
                using Fixture f = await Fixture.CreateAsync(harborDock: true).ConfigureAwait(false);
                f.Bb.Inspector.HarborHostOverride = id => null;
                string entryId = await f.CaptureConflictAsync("src/core/a.cs").ConfigureAwait(false);
                BlackBoxEntry entry = await f.ProcessAsync(entryId).ConfigureAwait(false);
                AssertEqual(BlackBoxStateEnum.Analyzed, entry.State, "no enrichment failure: " + entry.LastError);
                BlackBoxRecord record = await f.Bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                BlackBoxWorkspace w = record.Workspace!;
                AssertEqual("Disconnected", w.HarborLinkState);
                AssertNull(w.WorktreeExists, "unknown, not invented");
                AssertNull(w.InspectedHost);
                AssertContains("not connected", w.InspectionNote);
                AssertContains("omitted", w.TargetMovementNote);
                AssertEqual(0, record.Conflict!.ConflictingMissions.Count);
                AssertTrue(record.Truncation.Notes.Any(n => n.Contains("not connected", StringComparison.Ordinal)), "note recorded");

                RecordingHostCommandExecutor silent = new RecordingHostCommandExecutor { Unreachable = true };
                f.Bb.Inspector.HarborHostOverride = id => new DockHost(id, new GitService(f.Logging, silent), silent, new LocalDockFileSystem());
                BlackBoxEntry again = await f.ProcessAsync(entryId).ConfigureAwait(false);
                AssertEqual(BlackBoxStateEnum.Analyzed, again.State, "unresponsive Harbor: " + again.LastError);
                BlackBoxRecord second = await f.Bb.Pipeline.LoadRecordAsync(again).ConfigureAwait(false);
                AssertNull(second.Workspace!.BranchTip, "no tip from a Harbor that does not answer");
                AssertEqual(0, second.Conflict!.TargetCommitTotal);
            }));

            cases.Add(Case("start_commit_missing", "Without a start commit target movement is omitted with a reason, and nothing is attributed", TestTags.Negative, async () =>
            {
                using Fixture f = await Fixture.CreateAsync(recordStartCommit: false).ConfigureAwait(false);
                f.LandOnMain("Hotfix", "Dana Human", "dana@example.com", "src/core/a.cs", "theirs\n");
                string entryId = await f.CaptureConflictAsync("src/core/a.cs").ConfigureAwait(false);
                BlackBoxEntry entry = await f.ProcessAsync(entryId).ConfigureAwait(false);
                AssertEqual(BlackBoxStateEnum.Analyzed, entry.State);
                BlackBoxRecord record = await f.Bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertContains("start commit was not recorded", record.Workspace!.TargetMovementNote);
                AssertEqual(0, record.Conflict!.TargetCommitsSinceStart.Count);
                AssertEqual(0, record.Conflict.ConflictingMissions.Count);
                AssertTrue(record.Truncation.Notes.Any(n => n.StartsWith("Target movement omitted", StringComparison.Ordinal)), "reason in the truncation notes");
                AssertNull(await TryReadAsync(f, entry, BlackBoxObjectNames.TargetCommits).ConfigureAwait(false), "no target-commits evidence");
            }));

            cases.Add(Case("branch_deleted", "A deleted branch and worktree are reported as missing, with the mission commit to restore from", TestTags.Negative, async () =>
            {
                using Fixture f = await Fixture.CreateAsync().ConfigureAwait(false);
                DatedGitRepo.Git(f.Bare, null, "worktree", "remove", "--force", f.Worktree);
                DatedGitRepo.Git(f.Bare, null, "branch", "-D", f.Branch);
                string entryId = await f.CaptureConflictAsync("src/core/a.cs").ConfigureAwait(false);
                BlackBoxEntry entry = await f.ProcessAsync(entryId).ConfigureAwait(false);
                AssertEqual(BlackBoxStateEnum.Analyzed, entry.State);
                BlackBoxWorkspace w = (await f.Bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false)).Workspace!;
                AssertEqual(false, w.BranchExists);
                AssertEqual(false, w.WorktreeExists);
                AssertContains("no longer exists", w.InspectionNote);
                AssertContains(f.MissionCommit, w.InspectionNote);
                string brief = await f.ReadTextAsync(entry, BlackBoxObjectNames.Brief).ConfigureAwait(false);
                AssertContains("Branch exists: no", brief);
            }));

            cases.Add(Case("empty_record", "An entry with no record, mission, vessel or dock is enriched without errors and stays honest about what is missing", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                BlackBoxEntry bare = await t.Driver.BlackBoxEntries.CreateAsync(BlackBoxTestKit.Entry(null, "msn_gone")).ConfigureAwait(false);
                BlackBoxEntry? entry = await bb.Pipeline.ProcessAsync(bare.Id).ConfigureAwait(false);
                AssertEqual(BlackBoxStateEnum.Analyzed, entry!.State, "analyzed: " + entry.LastError);
                BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertFalse(record.Truncation.Notes.Any(n => n.Contains("did not complete", StringComparison.Ordinal)), "no enricher failed: " + String.Join(" | ", record.Truncation.Notes));
                AssertEqual(0L, (await t.Driver.BlackBoxFiles.EnumerateAsync(bare.Id, null, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords);
                AssertEqual(0L, (await t.Driver.BlackBoxLinks.EnumerateAsync(bare.Id, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords);
            }));

            cases.Add(Case("large_inputs_truncated", "A large mission log keeps its tail and 60 target commits keep the newest 50, each with a truncation entry", TestTags.Negative, async () =>
            {
                using Fixture f = await Fixture.CreateAsync().ConfigureAwait(false);
                f.LandManyOnMain(60);
                string logPath = Path.Combine(f.Settings.LogDirectory, "missions", f.Mission.Id + ".log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                StringBuilder log = new StringBuilder();
                for (int i = 0; i < 12000; i++) log.Append("line ").Append(i).Append(" of the mission log, padded to make it long enough\n");
                log.Append("FINAL LINE\n");
                File.WriteAllText(logPath, log.ToString());
                string entryId = await f.CaptureConflictAsync("src/core/a.cs").ConfigureAwait(false);

                BlackBoxEntry entry = await f.ProcessAsync(entryId).ConfigureAwait(false);
                BlackBoxRecord record = await f.Bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertEqual(60, record.Conflict!.TargetCommitTotal);
                AssertEqual(50, record.Conflict.TargetCommitsSinceStart.Count);
                BlackBoxTruncatedField commits = record.Truncation.Fields.Single(x => x.Field == "Conflict.TargetCommitsSinceStart");
                AssertEqual(60L, commits.OriginalSize);
                AssertEqual(50L, commits.KeptSize);
                BlackBoxTruncatedField logField = record.Truncation.Fields.Single(x => x.Field == BlackBoxObjectNames.MissionLog);
                AssertTrue(logField.OriginalSize > BlackBoxForensics.TailBytes, "original size recorded");
                AssertTrue(logField.KeptSize <= BlackBoxForensics.TailBytes, "tail kept");
                string kept = await f.ReadTextAsync(entry, BlackBoxObjectNames.MissionLog).ConfigureAwait(false);
                AssertContains("FINAL LINE", kept);
                AssertFalse(kept.Contains("line 0 of", StringComparison.Ordinal), "head dropped");
                AssertTrue(kept.StartsWith("line ", StringComparison.Ordinal), "cut at a line boundary");
            }));

            cases.Add(Case("evidence_redacted", "Mission log, verification, merge-entry, check, diff stat and events evidence is written and every secret in it is redacted", TestTags.Negative, async () =>
            {
                using Fixture f = await Fixture.CreateAsync().ConfigureAwait(false);
                string secret = "ghp_" + new string('a', 36);
                string logPath = Path.Combine(f.Settings.LogDirectory, "missions", f.Mission.Id + ".log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.WriteAllText(logPath, "starting\nexport GITHUB_TOKEN=" + secret + "\napi_key=supersecretvalue1\ndone\n");
                f.Mission.DiffSnapshot = "diff --git a/src/core/a.cs b/src/core/a.cs\n--- a/src/core/a.cs\n+++ b/src/core/a.cs\n@@ -1 +1,2 @@\n-a\n+mine\n+more\n";
                await f.Db.Driver.Missions.UpdateAsync(f.Mission).ConfigureAwait(false);
                await f.Db.Driver.Events.CreateAsync(new ArmadaEvent("mission.landing_verification", "Landing verification Failed at abc")
                {
                    EntityType = "mission", EntityId = f.Mission.Id, MissionId = f.Mission.Id, VesselId = f.Vessel.Id,
                    Payload = "{\"Status\":\"Failed\",\"CommitHash\":\"abc\",\"Command\":\"dotnet test\",\"ExitCode\":1,\"Output\":\"password=hunter2hunter2 failed " + secret + "\"}"
                }).ConfigureAwait(false);
                MergeEntry merge = await f.Db.Driver.MergeEntries.CreateAsync(new MergeEntry { MissionId = f.Mission.Id, VesselId = f.Vessel.Id, BranchName = f.Branch, TargetBranch = "main", Status = MergeStatusEnum.Failed, TestCommand = "make test", TestExitCode = 2, TestOutput = "Authorization: Bearer " + secret }).ConfigureAwait(false);
                string entryId = await f.CaptureConflictAsync(c => { c.MergeEntryId = merge.Id; c.CheckResults.Add(new StageCheckResult { Status = StageCheckStatusEnum.Failed, FailedCommand = "make build", ExitCode = 1, FailureMessage = "token=" + secret }); }, "src/core/a.cs").ConfigureAwait(false);

                BlackBoxEntry entry = await f.ProcessAsync(entryId).ConfigureAwait(false);
                foreach (string name in new[] { BlackBoxObjectNames.MissionLog, BlackBoxObjectNames.VerificationPrefix + "1.txt", BlackBoxObjectNames.MergeEntry, BlackBoxObjectNames.CheckPrefix + "1.txt", BlackBoxObjectNames.DiffStat, BlackBoxObjectNames.Events })
                {
                    string text = await f.ReadTextAsync(entry, name).ConfigureAwait(false);
                    AssertFalse(text.Contains(secret, StringComparison.Ordinal), name + " leaks the token");
                    AssertFalse(text.Contains("supersecretvalue1", StringComparison.Ordinal), name + " leaks the api key");
                    AssertFalse(text.Contains("hunter2hunter2", StringComparison.Ordinal), name + " leaks the password");
                }
                AssertContains("dotnet test", await f.ReadTextAsync(entry, BlackBoxObjectNames.VerificationPrefix + "1.txt").ConfigureAwait(false));
                AssertContains("make test", await f.ReadTextAsync(entry, BlackBoxObjectNames.MergeEntry).ConfigureAwait(false));
                AssertContains("src/core/a.cs", await f.ReadTextAsync(entry, BlackBoxObjectNames.DiffStat).ConfigureAwait(false));
                AssertContains("mission.landing_verification", await f.ReadTextAsync(entry, BlackBoxObjectNames.Events).ConfigureAwait(false));
                BlackBoxRecord record = await f.Bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertTrue(record.Redaction.RedactedCount > 0, "redactions counted");
                AssertEqual(2, record.Evidence!.DiffStat!.Insertions);
                AssertEqual(1, record.Evidence.DiffStat.Deletions);
                AssertTrue(record.Evidence.RelevantEvents.Any(e => e.EventType == "mission.landing_verification"), "relevant events");
            }));

            cases.Add(Case("concurrent_overlap", "Another mission active during the run that touched the same file (diff snapshot, not landed) is listed with an OverlappingFiles link; one outside the window is not", TestTags.Positive, async () =>
            {
                using Fixture f = await Fixture.CreateAsync().ConfigureAwait(false);
                Mission concurrent = await f.AddMissionAsync("Parallel edit of a.cs", MissionStatusEnum.InProgress, m =>
                {
                    m.StartedUtc = f.Mission.StartedUtc!.Value.AddMinutes(10);
                    m.DiffSnapshot = "diff --git a/src/core/a.cs b/src/core/a.cs\n--- a/src/core/a.cs\n+++ b/src/core/a.cs\n@@ -1 +1 @@\n-a\n+parallel\n";
                }).ConfigureAwait(false);
                await f.AddMissionAsync("Old edit of a.cs", MissionStatusEnum.Complete, m =>
                {
                    m.StartedUtc = f.Mission.StartedUtc!.Value.AddDays(-3);
                    m.CompletedUtc = f.Mission.StartedUtc!.Value.AddDays(-2);
                    m.DiffSnapshot = "diff --git a/src/core/a.cs b/src/core/a.cs\n";
                }).ConfigureAwait(false);
                string entryId = await f.CaptureConflictAsync("src/core/a.cs").ConfigureAwait(false);
                BlackBoxEntry entry = await f.ProcessAsync(entryId).ConfigureAwait(false);
                BlackBoxRecord record = await f.Bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                BlackBoxConflictingMission cm = record.Conflict!.ConcurrentMissions!.Single();
                AssertEqual(concurrent.Id, cm.MissionId);
                AssertNull(cm.LandedUtc, "not landed");
                AssertEqual("src/core/a.cs", cm.OverlappingFiles.Single());
                BlackBoxLink link = (await f.LinksAsync(entryId).ConfigureAwait(false)).Single(l => l.Relation == BlackBoxLinkRelationEnum.OverlappingFiles);
                AssertEqual(concurrent.Id, link.MissionId);
                AssertEqual(1, link.OverlapFileCount);
            }));

            cases.Add(Case("attempts_and_rescue_chain", "Stage outcomes and handoffs become attempts; the rescue chain, counters and rescue links are recorded", TestTags.Positive, async () =>
            {
                using Fixture f = await Fixture.CreateAsync().ConfigureAwait(false);
                Mission grandparent = await f.AddMissionAsync("Original", MissionStatusEnum.Failed).ConfigureAwait(false);
                Mission parent = await f.AddMissionAsync("Rescue 1", MissionStatusEnum.Failed, m => m.ParentMissionId = grandparent.Id).ConfigureAwait(false);
                f.Mission.ParentMissionId = parent.Id;
                f.Mission.IsRescue = true;
                f.Mission.RedispatchAttempts = 2;
                await f.Db.Driver.Missions.UpdateAsync(f.Mission).ConfigureAwait(false);
                StageOutcome outcome = await f.Db.Driver.StageOutcomes.CreateAsync(new StageOutcome { MissionId = f.Mission.Id, VesselId = f.Vessel.Id, Attempt = 1, Model = "model-x", Runtime = AgentRuntimeEnum.ClaudeCode, FailureKind = MissionFailureKindEnum.LandingConflict, FailureReason = "conflict in a.cs", DurationMs = 1234, Disposition = StageDispositionEnum.Failed }).ConfigureAwait(false);
                await f.Db.Driver.HandoffArtifacts.CreateAsync(new HandoffArtifact { MissionId = f.Mission.Id, VesselId = f.Vessel.Id, StageOutcomeId = outcome.Id, Attempt = 1, Verdict = "Blocked", Output = "could not merge" }).ConfigureAwait(false);
                await f.Db.Driver.Incidents.CreateAsync(new Incident { Title = "Landing conflict", MissionId = f.Mission.Id, VesselId = f.Vessel.Id, RescueMissionIds = new List<string> { "msn_rescue_child" } }).ConfigureAwait(false);
                string entryId = await f.CaptureConflictAsync("src/core/a.cs").ConfigureAwait(false);

                BlackBoxEntry entry = await f.ProcessAsync(entryId).ConfigureAwait(false);
                BlackBoxRecord record = await f.Bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                BlackBoxAttempt stage = record.Attempts.Single(a => a.Source == "StageOutcome");
                AssertEqual(outcome.Id, stage.StageOutcomeId);
                AssertEqual("model-x", stage.Model);
                AssertEqual(1234L, stage.DurationMs);
                AssertEqual("Blocked", record.Attempts.Single(a => a.Source == "Handoff").Disposition);
                AssertEqual(parent.Id + "," + grandparent.Id, String.Join(",", record.Subject!.RescueChain!), "nearest ancestor first");
                AssertEqual(2, record.Subject.RedispatchAttempts);
                List<BlackBoxLink> links = await f.LinksAsync(entryId).ConfigureAwait(false);
                AssertEqual(parent.Id, links.Single(l => l.Relation == BlackBoxLinkRelationEnum.RescueOf).MissionId);
                AssertEqual("msn_rescue_child", links.Single(l => l.Relation == BlackBoxLinkRelationEnum.ParentOfRescue).MissionId);
            }));

            cases.Add(Case("voyage_cascade", "A voyage entry rolls up its missions, links the cascade root and lists the downstream failures", TestTags.Positive, async () =>
            {
                using Fixture f = await Fixture.CreateAsync().ConfigureAwait(false);
                Voyage voyage = await f.Db.Driver.Voyages.CreateAsync(new Voyage("Cascade voyage") { Status = VoyageStatusEnum.Failed }).ConfigureAwait(false);
                Mission root = await f.AddMissionAsync("Root", MissionStatusEnum.Failed, m => { m.VoyageId = voyage.Id; m.CompletedUtc = DateTime.UtcNow.AddMinutes(-30); }).ConfigureAwait(false);
                Mission cancelled = await f.AddMissionAsync("Downstream", MissionStatusEnum.Cancelled, m => { m.VoyageId = voyage.Id; m.DependsOnMissionId = root.Id; }).ConfigureAwait(false);
                await f.AddMissionAsync("Fine", MissionStatusEnum.Complete, m => m.VoyageId = voyage.Id).ConfigureAwait(false);
                string? entryId = await f.Bb.Capture.RecordVoyageFailureAsync(voyage, VoyageStatusEnum.InProgress, BlackBoxStepEnum.SettlementFailed, "Voyage failed").ConfigureAwait(false);
                BlackBoxEntry entry = await f.ProcessAsync(entryId!).ConfigureAwait(false);
                BlackBoxRecord record = await f.Bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertEqual(3, record.Voyage!.MissionTotal);
                AssertEqual(root.Id, record.Voyage.CascadeRootMissionId);
                AssertTrue(record.Related!.DownstreamMissionIds.Contains(cancelled.Id), "downstream cancelled mission");
                AssertEqual(root.Id, (await f.LinksAsync(entryId!).ConfigureAwait(false)).Single(l => l.Relation == BlackBoxLinkRelationEnum.CascadeRoot).MissionId);
            }));

            cases.Add(Case("reanalyze_keeps_rows_unique", "Running the pipeline twice replaces file and link rows instead of duplicating them", TestTags.Negative, async () =>
            {
                using Fixture f = await Fixture.CreateAsync().ConfigureAwait(false);
                Mission landed = await f.AddMissionAsync("Landed", MissionStatusEnum.Complete).ConfigureAwait(false);
                f.LandOnMain("Landed\n\nArmada-Mission-Id: " + landed.Id, "Armada", "armada@example.com", "src/core/a.cs", "theirs\n");
                string entryId = await f.CaptureConflictAsync("src/core/a.cs", "src/core/b.cs").ConfigureAwait(false);
                await f.ProcessAsync(entryId).ConfigureAwait(false);
                int files = (await f.FilesAsync(entryId).ConfigureAwait(false)).Count;
                int links = (await f.LinksAsync(entryId).ConfigureAwait(false)).Count;
                await f.ProcessAsync(entryId).ConfigureAwait(false);
                AssertEqual(files, (await f.FilesAsync(entryId).ConfigureAwait(false)).Count, "file rows");
                AssertEqual(links, (await f.LinksAsync(entryId).ConfigureAwait(false)).Count, "link rows");
                List<BlackBoxFile> all = await f.FilesAsync(entryId).ConfigureAwait(false);
                AssertEqual(all.Count, all.Select(r => r.Role + "|" + r.Path + "|" + r.ConflictingMissionId).Distinct().Count(), "no duplicate rows");
                AssertEqual(2, all.Count(r => r.Role == BlackBoxFileRoleEnum.Conflicted), "captured conflicted rows re-emitted once");
            }));

            cases.Add(CaseSync("helpers_parse_and_bound", "Diff parsing, finding paths, overlap and tails handle quoted, renamed, empty and multibyte inputs", TestTags.Positive, () =>
            {
                List<BlackBoxDiffFileStat> stats = BlackBoxForensics.DiffFileStats("diff --git a/x.cs b/y.cs\nsimilarity index 90%\n--- a/x.cs\n+++ b/y.cs\n+a\n-b\ndiff --git \"a/sp ace.txt\" \"b/sp ace.txt\"\n+c\n");
                AssertEqual("y.cs", stats[0].Path, "rename keeps the new path");
                AssertEqual(1, stats[0].Insertions);
                AssertEqual(1, stats[0].Deletions);
                AssertEqual("sp ace.txt", stats[1].Path, "quoted path");
                AssertEqual(0, BlackBoxForensics.DiffPaths(null).Count);
                AssertEqual("src/a.cs", BlackBoxFileRowsEnricher.PathOfFinding("Protected path `src/a.cs` was modified"));
                AssertNull(BlackBoxFileRowsEnricher.PathOfFinding("no path here"));
                AssertEqual("a.cs", BlackBoxForensics.Overlap(new[] { "a.cs", "b.cs", "a.cs" }, new HashSet<string> { "a.cs" }, new HashSet<string>()).Single());
                byte[] tail = BlackBoxForensics.TailUtf8(new string('é', 1000), 101, out long original);
                AssertEqual(2000L, original);
                AssertTrue(Encoding.UTF8.GetString(tail).All(ch => ch == 'é'), "no split multibyte character");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box enrichment (git forensics, attribution, evidence)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<string?> TryReadAsync(Fixture f, BlackBoxEntry entry, string name)
        {
            try
            {
                return await f.ReadTextAsync(entry, name).ConfigureAwait(false);
            }
            catch (BlackBoxException)
            {
                return null;
            }
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseSync(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; }, tags: new List<string> { tag });
        }

        #endregion

        #region Private-Classes

        /// <summary>
        /// A vessel with a bare clone, a mission dock worktree on branch armada/m1 (one commit changing src/core/a.cs
        /// after the start commit), a failed mission and a runtime whose enrichers inspect it.
        /// </summary>
        private sealed class Fixture : IDisposable
        {
            public TestDatabase Db { get; private set; } = null!;
            public BlackBoxRuntime Bb { get; private set; } = null!;
            public ArmadaSettings Settings { get; private set; } = null!;
            public LoggingModule Logging { get; private set; } = null!;
            public Vessel Vessel { get; private set; } = null!;
            public Mission Mission { get; private set; } = null!;
            public Dock Dock { get; private set; } = null!;
            public string Bare { get; private set; } = String.Empty;
            public string Worktree { get; private set; } = String.Empty;
            public string Branch { get; private set; } = "armada/m1";
            public string StartCommit { get; private set; } = String.Empty;
            public string MissionCommit { get; private set; } = String.Empty;

            private string _Root = String.Empty;
            private DateTimeOffset _Clock = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

            public static async Task<Fixture> CreateAsync(bool harborDock = false, bool recordStartCommit = true)
            {
                Fixture f = new Fixture();
                f._Root = TestTemp.NewDirectory("bb-enrich");
                f.Db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                f.Logging = BlackBoxTestKit.Logging();
                f.Settings = BlackBoxTestKit.Settings();
                f.Settings.LogDirectory = Path.Combine(f._Root, "logs");

                string work = Path.Combine(f._Root, "work");
                Directory.CreateDirectory(work);
                DatedGitRepo.Git(work, null, "init", "-q", "-b", "main");
                foreach (string file in new[] { "README.md", "src/core/a.cs", "src/core/b.cs", "src/core/c.cs", "docs/readme.md" })
                    DatedGitRepo.Commit(work, "add " + file, f.Tick(), file, "a\n");
                f.StartCommit = DatedGitRepo.Git(work, null, "rev-parse", "HEAD").Trim();
                f.Bare = Path.Combine(f._Root, "project.git");
                DatedGitRepo.Git(f._Root, null, "clone", "-q", "--bare", work, f.Bare);

                f.Worktree = Path.Combine(f._Root, "docks", "m1");
                Directory.CreateDirectory(Path.GetDirectoryName(f.Worktree)!);
                DatedGitRepo.Git(f.Bare, null, "worktree", "add", "-q", "-b", f.Branch, f.Worktree, "main");
                DatedGitRepo.Commit(f.Worktree, "mission work on a.cs", f.Tick(), "src/core/a.cs", "mine\n");
                f.MissionCommit = DatedGitRepo.Git(f.Worktree, null, "rev-parse", "HEAD").Trim();

                f.Vessel = await f.Db.Driver.Vessels.CreateAsync(new Vessel("project", "https://example.invalid/project.git") { LocalPath = f.Bare, DefaultBranch = "main" }).ConfigureAwait(false);
                Dock dock = new Dock(f.Vessel.Id) { WorktreePath = f.Worktree, BranchName = f.Branch, Active = false };
                if (harborDock)
                {
                    dock.HarborId = HarborId;
                    dock.RepositoryPath = f.Bare;
                }
                f.Dock = await f.Db.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);
                Mission mission = new Mission("Failed landing", "Change a.cs")
                {
                    VesselId = f.Vessel.Id,
                    Status = MissionStatusEnum.LandingFailed,
                    FailureKind = MissionFailureKindEnum.LandingConflict,
                    FailureReason = "Merge conflict integrating main",
                    BranchName = f.Branch,
                    DockId = f.Dock.Id,
                    CommitHash = f.MissionCommit,
                    StartCommit = recordStartCommit ? f.StartCommit : null,
                    StartedUtc = DateTime.UtcNow.AddHours(-1),
                    AssignedHarborId = harborDock ? HarborId : null
                };
                f.Mission = await f.Db.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);
                f.Bb = BlackBoxTestKit.Runtime(f.Db.Driver, f.Settings);
                return f;
            }

            public DateTimeOffset Tick()
            {
                _Clock = _Clock.AddMinutes(1);
                return _Clock;
            }

            public string LandOnMain(string message, string author, string email, string path, string content)
            {
                string temp = Path.Combine(_Root, "land-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                DatedGitRepo.Git(Bare, null, "worktree", "add", "-q", temp, "main");
                DatedGitRepo.CommitWith(temp, message, Tick(), author, email, null, path, content);
                string sha = DatedGitRepo.Git(temp, null, "rev-parse", "HEAD").Trim();
                DatedGitRepo.Git(Bare, null, "worktree", "remove", "--force", temp);
                return sha;
            }

            public void LandManyOnMain(int count)
            {
                string temp = Path.Combine(_Root, "land-many");
                DatedGitRepo.Git(Bare, null, "worktree", "add", "-q", temp, "main");
                for (int i = 0; i < count; i++) DatedGitRepo.Commit(temp, "other work " + i, Tick(), "other/file" + i + ".txt", i + "\n");
                DatedGitRepo.Git(Bare, null, "worktree", "remove", "--force", temp);
            }

            public async Task<Mission> AddMissionAsync(string title, MissionStatusEnum status, Action<Mission>? configure = null)
            {
                Mission m = new Mission(title, "other work") { VesselId = Vessel.Id, Status = status };
                configure?.Invoke(m);
                return await Db.Driver.Missions.CreateAsync(m).ConfigureAwait(false);
            }

            public Task<string> CaptureConflictAsync(params string[] conflicted)
            {
                return CaptureConflictAsync(null, conflicted);
            }

            public async Task<string> CaptureConflictAsync(Action<BlackBoxCaptureContext>? configure, params string[] conflicted)
            {
                string? id = await Bb.Capture.RecordMissionFailureAsync(Mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.TargetIntegration, c =>
                {
                    c.Vessel = Vessel;
                    c.Dock = Dock;
                    c.GitFacts = new BlackBoxGitFacts { ConflictedFiles = conflicted.ToList() };
                    configure?.Invoke(c);
                }).ConfigureAwait(false);
                if (id == null) throw new InvalidOperationException("capture failed");
                return id;
            }

            public async Task<BlackBoxEntry> ProcessAsync(string entryId)
            {
                BlackBoxEntry? entry = await Bb.Pipeline.ProcessAsync(entryId).ConfigureAwait(false);
                if (entry == null) throw new InvalidOperationException("entry missing");
                return entry;
            }

            public async Task<List<BlackBoxLink>> LinksAsync(string entryId)
            {
                return (await Db.Driver.BlackBoxLinks.EnumerateAsync(entryId, new EnumerationQuery { PageSize = 100 }).ConfigureAwait(false)).Objects;
            }

            public async Task<List<BlackBoxFile>> FilesAsync(string entryId)
            {
                return (await Db.Driver.BlackBoxFiles.EnumerateAsync(entryId, null, new EnumerationQuery { PageSize = 1000 }).ConfigureAwait(false)).Objects;
            }

            public async Task<string> ReadTextAsync(BlackBoxEntry entry, string name)
            {
                BlackBoxObjectReadResult read = await Bb.Objects.ReadAsync(entry, name).ConfigureAwait(false);
                return Encoding.UTF8.GetString(read.Data);
            }

            public void Dispose()
            {
                Bb?.Dispose();
                Db?.Dispose();
                try { if (Directory.Exists(_Root)) Directory.Delete(_Root, true); } catch (Exception) { }
            }
        }

        #endregion
    }
}
