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
    /// BlackBoxBriefRenderer: golden-ish content per audience, audiences differ, size target and cap with trimming, no em-dashes, no secrets, and rendering from an almost empty record.
    /// </summary>
    public sealed class BlackBoxBriefRendererSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxBriefRenderer";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("agent_brief_content", "The agent brief carries the preamble, triage instruction, site, reason, conflict, recovery and the answer instruction", TestTags.Positive, () =>
            {
                BlackBoxBriefRenderer renderer = new BlackBoxBriefRenderer();
                string brief = renderer.Render(SampleEntry(), SampleRecord(), BlackBoxAudienceEnum.Agent);
                AssertStartsWith("# FIX THIS: Add the parser (mission msn_brief)", brief);
                foreach (string expected in new[] { "You are asked to FIX THIS", "**ArmadaDefect**", "**UserConfig**", "## Where it failed", "src/Armada.Core/Services/MissionService.cs:1582",
                    "## How it failed", "Mission modified files", "Armada stack frames", "## Conflict", "`src/core/a.cs`", "## Why", "BBX-LAND-003", "## Recovery", "retry_landing",
                    "## Your answer", "## Configuration in effect", "LandingMode", "## Evidence", "evidence/agent-output.txt", "1.2.0+abc1234" })
                    AssertContains(expected, brief, "agent brief has " + expected);
                return Task.CompletedTask;
            }));

            cases.Add(Case("audiences_differ", "User has no stack frames or source pointers; maintainer has improvements and the build; all three differ", TestTags.Positive, () =>
            {
                BlackBoxBriefRenderer renderer = new BlackBoxBriefRenderer();
                string agent = renderer.Render(SampleEntry(), SampleRecord(), BlackBoxAudienceEnum.Agent);
                string user = renderer.Render(SampleEntry(), SampleRecord(), BlackBoxAudienceEnum.User);
                string maintainer = renderer.Render(SampleEntry(), SampleRecord(), BlackBoxAudienceEnum.Maintainer);
                AssertNotEqual(agent, user); AssertNotEqual(agent, maintainer); AssertNotEqual(user, maintainer);
                AssertStartsWith("# What went wrong", user);
                AssertFalse(user.Contains("Armada stack frames", StringComparison.Ordinal), "user: no stack frames");
                AssertFalse(user.Contains("MissionService.cs:1582", StringComparison.Ordinal), "user: no source pointer");
                AssertFalse(user.Contains("## What Armada could change", StringComparison.Ordinal), "user: no improvement hypotheses");
                AssertContains("## What Armada could change", maintainer, "maintainer: improvements");
                AssertContains("Armada maintainer brief", maintainer);
                AssertContains("commit abc1234", maintainer, "maintainer: build");
                AssertEqual("brief.md", BlackBoxBriefRenderer.ObjectName(BlackBoxAudienceEnum.Agent));
                AssertEqual("brief.user.md", BlackBoxBriefRenderer.ObjectName(BlackBoxAudienceEnum.User));
                AssertEqual("brief.maintainer.md", BlackBoxBriefRenderer.ObjectName(BlackBoxAudienceEnum.Maintainer));
                return Task.CompletedTask;
            }));

            cases.Add(Case("size_target_and_cap", "A typical brief fits the 12 KB target; a huge record is trimmed under the 40 KB cap with list and text trimming", TestTags.Negative, () =>
            {
                BlackBoxBriefRenderer renderer = new BlackBoxBriefRenderer();
                string typical = renderer.Render(SampleEntry(), SampleRecord(), BlackBoxAudienceEnum.Agent);
                AssertTrue(Encoding.UTF8.GetByteCount(typical) <= BlackBoxLimits.BriefTargetBytes, "typical under 12 KB: " + Encoding.UTF8.GetByteCount(typical));
                BlackBoxRecord huge = SampleRecord();
                huge.Conflict!.ConflictedFiles = Enumerable.Range(0, 500).Select(i => "src/very/long/directory/name/for/file/number/" + i + "/" + new string('f', 60) + ".cs").ToList();
                huge.Conflict.ConflictedFileTotal = 10000;
                huge.Classification!.RawReason = new string('r', 8000);
                huge.Mechanism!.CommandOutputTail = new string('o', 8000);
                huge.Mechanism.GitStderrTail = new string('g', 4000);
                for (int i = 0; i < 64; i++) huge.Configuration!.Values.Add(new BlackBoxConfigValue { Setting = "S" + i, Value = new string('v', 300) });
                foreach (BlackBoxAudienceEnum audience in new[] { BlackBoxAudienceEnum.Agent, BlackBoxAudienceEnum.User, BlackBoxAudienceEnum.Maintainer })
                {
                    string brief = renderer.Render(SampleEntry(), huge, audience);
                    int bytes = Encoding.UTF8.GetByteCount(brief);
                    AssertTrue(bytes <= BlackBoxLimits.BriefMaxBytes, audience + " under the 40 KB cap: " + bytes);
                    AssertContains("more (see record.json)", brief, audience + " says lists were cut");
                }
                renderer.MaxBytes = 4096;
                renderer.TargetBytes = 2048;
                string cut = renderer.Render(SampleEntry(), huge, BlackBoxAudienceEnum.Agent);
                AssertTrue(Encoding.UTF8.GetByteCount(cut) <= 4096, "hard cap honored: " + Encoding.UTF8.GetByteCount(cut));
                AssertContains(BlackBoxBriefRenderer.CutMarker, cut, "cut marker");
                return Task.CompletedTask;
            }));

            cases.Add(Case("no_em_dashes_no_secrets", "Briefs never contain em-dashes or secrets, even when the inputs do", TestTags.Negative, () =>
            {
                BlackBoxBriefRenderer renderer = new BlackBoxBriefRenderer();
                BlackBoxEntry entry = SampleEntry();
                entry.MissionTitle = "Title — with dash";
                BlackBoxRecord record = SampleRecord();
                record.Subject!.Title = "Title — with dash";
                record.Classification!.RawReason = "push to https://ci:s3cr3tpass@git.example.com failed — token=ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                foreach (BlackBoxAudienceEnum audience in new[] { BlackBoxAudienceEnum.Agent, BlackBoxAudienceEnum.User, BlackBoxAudienceEnum.Maintainer })
                {
                    string brief = renderer.Render(entry, record, audience);
                    AssertFalse(brief.Contains('—'), audience + " has no em-dash");
                    AssertFalse(brief.Contains('–'), audience + " has no en-dash");
                    AssertFalse(brief.Contains("s3cr3tpass", StringComparison.Ordinal), audience + " has no password");
                    AssertFalse(brief.Contains("ghp_ABCDEF", StringComparison.Ordinal), audience + " has no token");
                }
                string hosts = renderer.Render(entry, record, BlackBoxAudienceEnum.Maintainer, true);
                AssertFalse(hosts.Contains("/Users/dev", StringComparison.Ordinal), "redactHosts strips absolute paths");
                return Task.CompletedTask;
            }));

            cases.Add(Case("renders_sparse_records", "An entry with no record (sweeper, nothing known) still renders and says what is missing", TestTags.Negative, () =>
            {
                BlackBoxBriefRenderer renderer = new BlackBoxBriefRenderer();
                BlackBoxEntry entry = new BlackBoxEntry { MissionId = "msn_sparse", CaptureMode = BlackBoxCaptureModeEnum.Sweeper };
                string brief = renderer.Render(entry, null, BlackBoxAudienceEnum.Agent);
                AssertContains("(mission msn_sparse)", brief);
                AssertContains("No analysis findings yet", brief);
                AssertContains("No recovery paths recorded yet", brief);
                BlackBoxRecord sweeper = new BlackBoxRecord { CaptureMode = BlackBoxCaptureModeEnum.Sweeper };
                AssertContains("lower fidelity", renderer.Render(entry, sweeper, BlackBoxAudienceEnum.User), "sweeper fidelity note");
                AssertContains("Enrichment is pending", renderer.Render(entry, sweeper, BlackBoxAudienceEnum.Agent), "pending enrichment note");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box brief renderer", cases: cases);
        }

        private static BlackBoxEntry SampleEntry()
        {
            return new BlackBoxEntry { MissionId = "msn_brief", MissionTitle = "Add the parser", VesselName = "Armada", Phase = BlackBoxPhaseEnum.Completion, Step = BlackBoxStepEnum.ScopeViolation, FailureKind = MissionFailureKindEnum.ScopeViolation, ArmadaVersion = "1.2.0+abc1234", ArmadaCommit = "abc1234" };
        }

        private static BlackBoxRecord SampleRecord()
        {
            BlackBoxRecord r = new BlackBoxRecord { State = BlackBoxStateEnum.Analyzed };
            r.Subject = new BlackBoxSubject { MissionId = "msn_brief", Title = "Add the parser", VesselName = "Armada", VesselId = "vsl_a", CaptainId = "cpt_a", CaptainName = "claude-1", CaptainRuntime = "ClaudeCode", VesselDefaultBranch = "main", BranchName = "armada/msn_brief" };
            r.Status = new BlackBoxStatusChange { Before = "InProgress", After = "Failed", ChangedUtc = new DateTime(2026, 10, 29, 2, 0, 0, DateTimeKind.Utc) };
            r.Status.Timeline.Add(new BlackBoxStatusTimelineEntry { Status = "Failed", Utc = r.Status.ChangedUtc.Value, Source = "failure write" });
            r.Site = new BlackBoxSite { Phase = BlackBoxPhaseEnum.Completion, Step = BlackBoxStepEnum.ScopeViolation, Host = "Admiral", Component = "MissionService", Method = "TryFailMissionForScopeViolationAsync", SourceFile = "src/Armada.Core/Services/MissionService.cs", SourceLine = 1582, ArmadaVersion = "1.2.0+abc1234", ArmadaCommit = "abc1234" };
            r.Mechanism = new BlackBoxMechanism { ExceptionType = "System.InvalidOperationException", ExceptionMessage = "boom", FailingCommand = "dotnet test", CommandExitCode = 1, CommandOutputTail = "1 test failed" };
            r.Mechanism.StackFrames.Add("Armada.Core.Services.MissionService.TryFailMissionForScopeViolationAsync() in /Users/dev/Armada/src/Armada.Core/Services/MissionService.cs:line 1582");
            r.Classification = new BlackBoxClassification { Kind = MissionFailureKindEnum.ScopeViolation, RawReason = "Mission modified files outside its scoped file list: src/b.cs" };
            r.Workspace = new BlackBoxWorkspace { DockId = "dck_a", WorktreePath = "/Users/dev/.armada/docks/dck_a", WorktreeExists = true, BranchName = "armada/msn_brief", TargetBranch = "main" };
            r.Conflict = new BlackBoxConflict { IsConflict = true, ConflictedFileTotal = 2 };
            r.Conflict.ConflictedFiles.AddRange(new[] { "src/core/a.cs", "src/core/b.cs" });
            r.Conflict.ConflictedDirectories.Add(new BlackBoxDirectoryCount { Directory = "src/core", Depth = 2, FileCount = 2 });
            r.Conflict.ConflictingMissions.Add(new BlackBoxConflictingMission { MissionId = "msn_other", Title = "Other work", CommitHash = "def5678", OverlappingFileTotal = 2 });
            r.Configuration = new BlackBoxConfiguration();
            r.Configuration.Values.Add(new BlackBoxConfigValue { Setting = "LandingMode", Value = "MergeAndPush", Source = BlackBoxConfigSourceEnum.Global });
            r.Evidence = new BlackBoxEvidenceIndex();
            r.Evidence.Objects.Add(new BlackBoxEvidenceItem { Name = "evidence/agent-output.txt", UncompressedBytes = 1234, Excerpt = "done" });
            r.Analysis = new BlackBoxAnalysis { Summary = "The mission collided with msn_other." };
            BlackBoxFinding finding = new BlackBoxFinding { RuleId = "BBX-LAND-003", Statement = "msn_other landed def5678 on main, changing 2 of the same files.", Confidence = BlackBoxConfidenceEnum.High };
            r.Analysis.Why.Add(finding);
            r.Analysis.ArmadaImprovements.Add(new BlackBoxImprovement { Area = "Dispatch", Hypothesis = "Serialize overlapping missions.", RuleId = "BBX-LAND-003" });
            BlackBoxRecoveryPath path = new BlackBoxRecoveryPath { Id = "rp1", Rank = 1, Title = "Retry the landing", Action = new BlackBoxAction { Tool = "retry_landing" } };
            path.Action.Arguments["missionId"] = "msn_brief";
            path.Steps.Add(new BlackBoxRecoveryStep { Order = 1, Description = "Retry the landing", McpCommand = "retry_landing {\"missionId\": \"msn_brief\"}", CliCommand = "armada mission retry-landing msn_brief" });
            r.Analysis.RecoveryPaths.Add(path);
            r.Analysis.Triage = new BlackBoxTriageVerdict { Verdict = BlackBoxTriageEnum.MissionBrief, Confidence = BlackBoxConfidenceEnum.Medium };
            return r;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
