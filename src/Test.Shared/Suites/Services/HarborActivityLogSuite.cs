namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the Harbor app's activity log (<see cref="HarborActivityLog"/>) and the typed fields its entries
    /// carry from where they are logged (<see cref="HarborLogClassifier"/>, <see cref="HarborLinkClient"/>, and the
    /// expected exit codes callers declare). The summary view collapses routine git and file work into one line per
    /// dock, merges a request with its result, shortens paths, and always shows failures; the detail view shows
    /// everything.
    /// </summary>
    public sealed class HarborActivityLogSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string _SuiteId = "Services.HarborActivityLog";
        private const string _Vessel = "DocConverter";
        private const string _Mission = "msn_mv092791_kEYKdgFrByX";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the HarborActivityLog suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("summary_collapses_dock_work_into_runs", "A mission's dock preparation and landing each collapse into one line that counts its git commands and file writes, between the dock, job, and dock-removed lines", TestTags.Positive, () =>
            {
                Scenario s = new Scenario();
                s.ProvisionDock();
                for (int i = 0; i < 14; i++) s.Git(s.DockPath, 0, null, "rev-parse", "HEAD");
                s.FileWrite(Path.Combine(s.DockPath, "CLAUDE.md"), HarborFileOperationEnum.Write);
                s.FileWrite(Path.Combine(s.DockPath, "CLAUDE.md"), HarborFileOperationEnum.AddGitExclude);
                s.LaunchMission();

                List<string> lines = s.Summary();
                AssertEqual(3, lines.Count, "dock ready, one prepared-dock line, the mission start: " + String.Join(" | ", lines));
                AssertContains("DocConverter msn_mv092791: dock ready from discovered checkout ", lines[0]);
                AssertEnds("DocConverter msn_mv092791: prepared dock (14 git commands, 2 file writes)", lines[1]);
                AssertEnds("Mission msn_mv092791 started in DocConverter (ClaudeCode)", lines[2], "launch and start are one line");

                s.Exit(0, 36700, 36400);
                for (int i = 0; i < 6; i++) s.Git(s.DockPath, 0, null, "diff", "--name-only");
                s.ReclaimDock();

                lines = s.Summary();
                AssertEqual(6, lines.Count, String.Join(" | ", lines));
                AssertEnds("Mission msn_mv092791 exited ok in 36.7s (first output 36.4s)", lines[3]);
                AssertEnds("DocConverter msn_mv092791: landed and cleaned up (6 git commands)", lines[4]);
                AssertEnds("DocConverter msn_mv092791: dock removed", lines[5]);
                foreach (string line in lines)
                {
                    AssertFalse(line.Contains("[req", StringComparison.Ordinal), "no request IDs in the summary: " + line);
                    AssertFalse(line.Contains(s.DockPath, StringComparison.Ordinal), "no full dock path in the summary: " + line);
                    AssertFalse(line.EndsWith(".", StringComparison.Ordinal), "no trailing period: " + line);
                }

                return Task.CompletedTask;
            }));

            cases.Add(Case("failure_breaks_out_of_run", "A failed command gets its own summary line with the command and exit code, even inside a run, and ends the run", TestTags.Negative, () =>
            {
                Scenario s = new Scenario();
                s.ProvisionDock();
                s.Git(s.DockPath, 0, null, "rev-parse", "HEAD");
                s.Git(s.DockPath, 0, null, "log", "-1");
                s.Git(s.DockPath, 0, null, "ls-files");
                HarborLogEntry failed = s.Git(s.DockPath, 128, null, "merge-base", "main", "HEAD");
                s.Git(s.DockPath, 0, null, "status");
                s.Git(s.DockPath, 0, null, "diff");
                HarborLogEntry timedOut = s.Git(s.DockPath, -1, null, true, "fetch", "origin");

                AssertEqual(HarborLogOutcomeEnum.Failed, failed.Outcome);
                AssertEqual(HarborLogLevelEnum.Summary, failed.Level, "a failure is a summary entry");
                AssertEqual(HarborLogCategoryEnum.Git, failed.Category);
                AssertEqual(HarborLogOutcomeEnum.Failed, timedOut.Outcome, "a timeout is a failure");

                List<string> lines = s.Summary();
                AssertEqual(5, lines.Count, String.Join(" | ", lines));
                AssertEnds("DocConverter msn_mv092791: prepared dock (3 git commands)", lines[1]);
                AssertEnds("DocConverter msn_mv092791: git merge-base main HEAD -> failed (exit 128)", lines[2]);
                AssertEnds("DocConverter msn_mv092791: prepared dock (2 git commands)", lines[3], "work after the failure starts a new run");
                AssertEnds("DocConverter msn_mv092791: git fetch origin -> failed (timed out)", lines[4]);
                return Task.CompletedTask;
            }));

            cases.Add(Case("expected_exit_is_not_a_failure", "A non-zero exit the caller declared expected is ok and stays in the run; the same exit undeclared is a failure", TestTags.Positive, () =>
            {
                Scenario s = new Scenario();
                s.ProvisionDock();
                HarborLogEntry grep = s.Git(s.DockPath, 1, new List<int> { 1 }, "grep", "-l", "-F", "-e", "term");
                HarborLogEntry showRef = s.Git(s.DockPath, 1, new List<int> { 1 }, "show-ref", "--verify", "--quiet", "refs/heads/x");
                HarborLogEntry other = s.Git(s.DockPath, 2, new List<int> { 1 }, "grep", "-l", "-F", "-e", "(");

                AssertEqual(HarborLogOutcomeEnum.Ok, grep.Outcome, "exit 1 declared expected");
                AssertEqual(HarborLogLevelEnum.Detail, grep.Level);
                AssertEnds("git grep -l -F -e term -> exit 1 (expected)", grep.Summary);
                AssertEqual(HarborLogOutcomeEnum.Ok, showRef.Outcome);
                AssertEqual(HarborLogOutcomeEnum.Failed, other.Outcome, "an exit code that was not declared is a failure");

                List<string> lines = s.Summary();
                AssertEqual(3, lines.Count, String.Join(" | ", lines));
                AssertEnds("DocConverter msn_mv092791: prepared dock (2 git commands)", lines[1]);
                AssertEnds("-> failed (exit 2)", lines[2]);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("link_client_classifies_expected_exits", "The link client marks a declared non-zero exit ok and an undeclared one failed, and the declaration crosses the wire", TestTags.Positive, async () =>
            {
                HarborGitRequest declared = new HarborGitRequest { RequestId = "r-ok", WorkingDirectory = "/repo", Arguments = new List<string> { "grep", "-l", "x" }, ExpectedExitCodes = new List<int> { 1 } };
                HarborGitRequest undeclared = new HarborGitRequest { RequestId = "r-bad", WorkingDirectory = "/repo2", Arguments = new List<string> { "grep", "-l", "x" } };
                HarborGitRequest roundTrip = (HarborGitRequest)HarborProtocol.Deserialize(HarborProtocol.Serialize(declared));
                AssertNotNull(roundTrip.ExpectedExitCodes, "expected exit codes are serialized");
                AssertEqual(1, roundTrip.ExpectedExitCodes!.Count);
                AssertEqual(1, roundTrip.ExpectedExitCodes[0]);
                AssertTrue(HarborProtocol.Serialize(undeclared).IndexOf("expectedExitCodes", StringComparison.Ordinal) < 0, "absent when not declared");

                FakeTransport transport = new FakeTransport();
                transport.Enqueue(HarborProtocol.Serialize(declared));
                transport.Enqueue(HarborProtocol.Serialize(undeclared));
                List<HarborLogEntry> entries = new List<HarborLogEntry>();
                HarborLinkClient client = new HarborLinkClient("hbr_al1", "Rig", new List<HarborCapability>(), 4,
                    new StubExecutor(new HostCommandResult { ExitCode = 1 }), CreateLogging(), 0, entry => { lock (entries) entries.Add(entry); });
                await client.RunSessionAsync(transport, CancellationToken.None).ConfigureAwait(false);

                HarborLogEntry? ok = FindResult(entries, "r-ok");
                HarborLogEntry? bad = FindResult(entries, "r-bad");
                AssertNotNull(ok, "result logged");
                AssertNotNull(bad, "result logged");
                AssertEqual(HarborLogOutcomeEnum.Ok, ok!.Outcome);
                AssertEqual(HarborLogLevelEnum.Detail, ok.Level);
                AssertEqual(HarborLogOutcomeEnum.Failed, bad!.Outcome);
                AssertEqual(HarborLogLevelEnum.Summary, bad.Level);
                AssertEqual("Result: exit 1 [req r-ok]", ok.Message, "the detail line is unchanged");
            }));

            cases.Add(CaseAsync("git_service_declares_probe_exit_codes", "GitService declares the exit codes its probes read as answers (1 for not an ancestor, 128 for not a repository) on the commands it sends to a Harbor", TestTags.Positive, async () =>
            {
                // Refs resolve (so the ancestry check runs); everything else answers 1.
                RecordingExecutor executor = new RecordingExecutor(request => request.Arguments.Contains("--verify") ? 0 : 1);
                GitService git = new GitService(CreateLogging(), executor);
                await git.IsAncestorAsync("/repo", "a", "b").ConfigureAwait(false);
                await git.IsRepositoryAsync("/repo").ConfigureAwait(false);

                HostCommandRequest? ancestor = executor.Find("merge-base");
                HostCommandRequest? repository = executor.Find("rev-parse", "--git-dir");
                HostCommandRequest? verify = executor.Find("rev-parse", "--verify");
                AssertNotNull(ancestor, "merge-base --is-ancestor ran");
                AssertNotNull(repository, "rev-parse --git-dir ran");
                AssertTrue(ancestor!.ExpectedExitCodes.Contains(1), "merge-base --is-ancestor declares 1");
                AssertTrue(repository!.ExpectedExitCodes.Contains(128), "rev-parse --git-dir declares 128");
                AssertNotNull(verify, "rev-parse --verify ran");
                AssertTrue(verify!.ExpectedExitCodes.Contains(1), "rev-parse --verify --quiet declares 1");
                AssertEqual(HostCommandKindEnum.Routine, ancestor.Kind);
            }));

            cases.Add(Case("request_and_result_merge", "A request's summary line becomes the request and its result, without a request ID; a launch and its start share one line", TestTags.Positive, () =>
            {
                Scenario s = new Scenario();
                s.ResolveCheckout();
                HarborGitRequest check = new HarborGitRequest
                {
                    RequestId = "req-check-1",
                    Executable = "/bin/sh",
                    WorkingDirectory = s.CheckoutPath,
                    Arguments = new List<string> { "-lc", "dotnet build" },
                    Kind = HostCommandKindEnum.CheckRun,
                    Label = "Build"
                };
                HarborLogEntry request = s.Classifier.GitRequest(check);
                s.Log.Add(request);
                AssertEqual(HarborLogCategoryEnum.CheckRun, request.Category);
                AssertEqual(HarborLogLevelEnum.Summary, request.Level, "a check run is shown");
                List<string> lines = s.Summary();
                AssertEqual(1, lines.Count, String.Join(" | ", lines));
                AssertEnds("Check run Build in DocConverter: /bin/sh -lc dotnet build (running)", lines[0]);

                s.Log.Add(s.Classifier.GitResult(check, new HostCommandResult { ExitCode = 0 }));
                lines = s.Summary();
                AssertEqual(1, lines.Count, "the result replaced the request's line");
                AssertEnds("Check run Build in DocConverter: /bin/sh -lc dotnet build -> exit 0", lines[0]);
                AssertFalse(lines[0].Contains("req-check-1", StringComparison.Ordinal));

                HarborLaunchRequest ask = new HarborLaunchRequest { JobId = "job-ask-1", Runtime = "ClaudeCode", JobKind = "AskTurn", ScratchWorkingDirectory = true };
                s.Log.Add(s.Classifier.Launch(ask, "/tmp/armada-harbor/scratch/job-ask-1", DateTime.UtcNow));
                AssertEnds("Ask turn launching in scratch (ClaudeCode)", s.Summary()[1]);
                s.Log.Add(s.Classifier.Started(ask, 4242));
                lines = s.Summary();
                AssertEqual(2, lines.Count, String.Join(" | ", lines));
                AssertEnds("Ask turn started (ClaudeCode)", lines[1]);
                s.Log.Add(s.Classifier.Exited(ask, 1, 2500, null));
                lines = s.Summary();
                AssertEnds("Ask turn exited with code 1 in 2.5s", lines[2]);
                return Task.CompletedTask;
            }));

            cases.Add(Case("paths_are_shortened", "The summary shows a dock as vessel/short mission, a scratch directory as scratch, a checkout as its vessel, and the home folder as ~", TestTags.Positive, () =>
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                AssertEqual("DocConverter/msn_mv092791...", HarborLogFormat.ShortPath("/x/docks/DocConverter/" + _Mission, HarborLogPathKindEnum.Dock, _Vessel, _Mission));
                AssertEqual("scratch", HarborLogFormat.ShortPath("/var/folders/1z/T/armada-harbor/scratch/a3b1", HarborLogPathKindEnum.Scratch, null, null));
                AssertEqual("DocConverter", HarborLogFormat.ShortPath(Path.Combine(home, "Code", "DocConverter"), HarborLogPathKindEnum.Checkout, _Vessel, null));
                AssertEqual("~" + Path.DirectorySeparatorChar + "Code" + Path.DirectorySeparatorChar + "Other", HarborLogFormat.ShortPath(Path.Combine(home, "Code", "Other"), HarborLogPathKindEnum.Other, null, null));
                AssertEqual("/opt/elsewhere", HarborLogFormat.HomeRelative("/opt/elsewhere"));
                AssertEqual("msn_mv092791", HarborLogFormat.ShortId(_Mission));
                AssertEqual("msn_short", HarborLogFormat.ShortId("msn_short"));
                AssertEqual("DocConverter msn_mv092791", HarborLogFormat.SubjectLabel(_Vessel, _Mission));

                Scenario s = new Scenario();
                s.ProvisionDock();
                HarborLaunchRequest launch = new HarborLaunchRequest { JobId = "job-m", Runtime = "ClaudeCode", JobKind = "Mission", MissionId = _Mission, WorkingDirectory = s.DockPath };
                HarborLogEntry entry = s.Classifier.Launch(launch, s.DockPath, DateTime.UtcNow);
                AssertEqual(HarborLogPathKindEnum.Dock, entry.PathKind);
                AssertEqual(s.DockPath, entry.Path, "the full path is kept on the entry");
                AssertEqual("Mission msn_mv092791 launching in DocConverter/msn_mv092791... (ClaudeCode)", entry.Summary);
                AssertContains(s.DockPath, entry.Message, "the detail line keeps the full path");

                HarborLogEntry fileFailed = s.Classifier.FileResult(
                    new HarborFileRequest { RequestId = "f1", Operation = HarborFileOperationEnum.Write, Path = Path.Combine(s.DockPath, "a.txt") },
                    new HarborFileResult { RequestId = "f1", Success = false, Message = "disk full" });
                AssertEqual(_Mission, fileFailed.MissionId, "a file in the dock belongs to its mission");
                AssertEqual(HarborLogOutcomeEnum.Failed, fileFailed.Outcome);
                return Task.CompletedTask;
            }));

            cases.Add(Case("detail_view_shows_everything", "The detail view shows every entry as logged, with request IDs and full paths, and switching views changes what Count, Recent, and ToText return", TestTags.Positive, () =>
            {
                Scenario s = new Scenario();
                s.ProvisionDock();
                s.Git(s.DockPath, 0, null, "rev-parse", "HEAD");
                s.Git(s.DockPath, 1, new List<int> { 1 }, "grep", "x");

                s.Log.ShowDetails = true;
                List<string> lines = s.Log.Recent(100);
                AssertEqual(6, lines.Count, "every entry: " + String.Join(" | ", lines));
                AssertEqual(6, s.Log.Count);
                AssertContains("<- Dock Provision for vessel DocConverter branch armada/msn [req dock-1]", lines[0]);
                AssertContains("-> Dock ready at " + s.DockPath, lines[1]);
                AssertContains("<- Work: git rev-parse HEAD (in " + s.DockPath + ") [req ", lines[2]);
                AssertContains("-> Result: exit 0 [req ", lines[3]);
                AssertContains("-> Result: exit 1 [req ", lines[5]);
                AssertContains("[req dock-1]", s.Log.ToText());

                s.Log.ShowDetails = false;
                AssertEqual(2, s.Log.Count, "the summary view");
                AssertFalse(s.Log.ToText().Contains("[req", StringComparison.Ordinal));
                AssertEqual(6, s.Log.Recent(100, true).Count, "the detail view is still there");
                return Task.CompletedTask;
            }));

            cases.Add(Case("heartbeats_collapse_in_both_views", "Consecutive heartbeats collapse into one line with the count and the run's first time, in both views; dock work counted in place does not break the run", TestTags.Positive, () =>
            {
                DateTime start = new DateTime(2026, 10, 8, 16, 35, 31, DateTimeKind.Utc);
                string first = start.ToLocalTime().ToString("HH:mm:ss");
                HarborLogClassifier classifier = new HarborLogClassifier();
                HarborActivityLog log = new HarborActivityLog(100);
                log.Add(new HarborLogEntry(HarborLogDirection.In, "Handshake accepted") { TimestampUtc = start.AddSeconds(-5) });
                log.Add(Heartbeat(classifier, start));
                AssertEqual(2, log.Count, "a single heartbeat is its own line");
                AssertEqual(first + "  Heartbeat", log.Recent(1)[0], "a single heartbeat has no count");
                AssertEqual(first + " -> Heartbeat", log.Recent(1, true)[0]);

                log.Add(Heartbeat(classifier, start.AddSeconds(15)));
                log.Add(Heartbeat(classifier, start.AddSeconds(30)));
                AssertEqual(2, log.Count, "the run stays on one line");
                string last = start.AddSeconds(30).ToLocalTime().ToString("HH:mm:ss");
                AssertEqual(last + "  Heartbeat (3 since " + first + ")", log.Recent(1)[0]);
                AssertEqual(last + " -> Heartbeat (3 since " + first + ")", log.Recent(1, true)[0]);

                log.Add(new HarborLogEntry(HarborLogDirection.In, "Launch job j1") { TimestampUtc = start.AddSeconds(31) });
                log.Add(Heartbeat(classifier, start.AddSeconds(45)));
                AssertEqual(4, log.Count, "other activity ends the run; the next heartbeat starts a new line");
                AssertEqual(start.AddSeconds(45).ToLocalTime().ToString("HH:mm:ss") + "  Heartbeat", log.Recent(1)[0]);

                log.Clear();
                AssertEqual(0, log.Count, "cleared");
                AssertEqual("", log.ToText());
                AssertEqual(0, log.Recent(10, true).Count, "both views cleared");
                return Task.CompletedTask;
            }));

            cases.Add(Case("line_count_is_bounded", "Each view keeps at most its line limit, dropping the oldest; a request or run whose line was dropped starts a new line", TestTags.Positive, () =>
            {
                HarborActivityLog small = new HarborActivityLog(2);
                small.Add(new HarborLogEntry(HarborLogDirection.Info, "a"));
                small.Add(new HarborLogEntry(HarborLogDirection.Info, "b"));
                small.Add(new HarborLogEntry(HarborLogDirection.Info, "c"));
                AssertEqual(2, small.Count, "bounded");
                AssertEnds("  b", small.Recent(2)[0], "oldest dropped");
                AssertEqual(2, small.Recent(10, true).Count, "the detail view is bounded too");

                small.Add(new HarborLogEntry(HarborLogDirection.In, "Launch") { Phase = HarborLogPhaseEnum.Request, RequestId = "j1", Summary = "Job launching" });
                small.Add(new HarborLogEntry(HarborLogDirection.Info, "d"));
                small.Add(new HarborLogEntry(HarborLogDirection.Info, "e"));
                small.Add(new HarborLogEntry(HarborLogDirection.Out, "Started") { Phase = HarborLogPhaseEnum.Result, RequestId = "j1", Summary = "Job started" });
                List<string> lines = small.Recent(2);
                AssertEqual(2, lines.Count);
                AssertEnds("  e", lines[0]);
                AssertEnds("  Job started", lines[1], "a result whose request line was dropped gets its own line");

                Scenario s = new Scenario(3);
                s.ProvisionDock();
                s.Git(s.DockPath, 0, null, "status");
                for (int i = 0; i < 3; i++) s.Log.Add(new HarborLogEntry(HarborLogDirection.Info, "filler " + i));
                s.Git(s.DockPath, 0, null, "status");
                lines = s.Summary();
                AssertEqual(3, lines.Count);
                AssertEnds("DocConverter msn_mv092791: prepared dock (1 git command)", lines[2], "the dropped run's work starts a new run line");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: _SuiteId,
                displayName: "Harbor Activity Log",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static HarborLogEntry Heartbeat(HarborLogClassifier classifier, DateTime timestampUtc)
        {
            HarborLogEntry entry = classifier.Heartbeat();
            entry.TimestampUtc = timestampUtc;
            return entry;
        }

        private static HarborLogEntry? FindResult(List<HarborLogEntry> entries, string requestId)
        {
            lock (entries)
            {
                foreach (HarborLogEntry entry in entries)
                    if (entry.Phase == HarborLogPhaseEnum.Result && entry.RequestId == requestId) return entry;
            }

            return null;
        }

        private static void AssertEnds(string expectedSuffix, string actual, string? message = null)
        {
            AssertTrue(actual.EndsWith(expectedSuffix, StringComparison.Ordinal),
                (message != null ? message + ": " : "") + "expected a line ending with \"" + expectedSuffix + "\" but got \"" + actual + "\"");
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return CaseAsync(caseId, displayName, tag, body);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: _SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Private-Classes

        /// <summary>
        /// A mission's life on a Harbor, fed through a classifier into an activity log the way the link client does.
        /// </summary>
        private sealed class Scenario
        {
            private int _Sequence = 0;
            private HarborLaunchRequest? _Launch = null;

            public Scenario(int maxLines = 500)
            {
                Log = new HarborActivityLog(maxLines);
                string root = Path.Combine(Path.GetTempPath(), "armada_activity_" + Guid.NewGuid().ToString("N"));
                CheckoutPath = Path.Combine(root, "Code", _Vessel);
                DockPath = Path.Combine(root, ".armada-harbor", "docks", _Vessel, _Mission);
            }

            public HarborLogClassifier Classifier { get; } = new HarborLogClassifier();

            public HarborActivityLog Log { get; }

            public string CheckoutPath { get; }

            public string DockPath { get; }

            public List<string> Summary() => Log.Recent(1000, false);

            public void ResolveCheckout()
            {
                HarborDockRequest request = new HarborDockRequest { RequestId = "resolve-1", Operation = HarborDockOperationEnum.Resolve, VesselName = _Vessel };
                Log.Add(Classifier.DockRequest(request));
                Log.Add(Classifier.DockResult(request, new HarborDockResult { RequestId = "resolve-1", Success = true, Source = HarborRepositorySourceEnum.Discovered, CheckoutPath = CheckoutPath, RepositoryPath = CheckoutPath }));
            }

            public void ProvisionDock()
            {
                HarborDockRequest request = new HarborDockRequest { RequestId = "dock-1", Operation = HarborDockOperationEnum.Provision, VesselName = _Vessel, BranchName = "armada/msn", DockName = _Mission };
                Log.Add(Classifier.DockRequest(request));
                Log.Add(Classifier.DockResult(request, new HarborDockResult { RequestId = "dock-1", Success = true, Source = HarborRepositorySourceEnum.Discovered, CheckoutPath = CheckoutPath, RepositoryPath = CheckoutPath, WorktreePath = DockPath }));
            }

            public void ReclaimDock()
            {
                HarborDockRequest request = new HarborDockRequest { RequestId = "dock-2", Operation = HarborDockOperationEnum.Reclaim, VesselName = _Vessel, WorktreePath = DockPath, RepositoryPath = CheckoutPath };
                Log.Add(Classifier.DockRequest(request));
                Log.Add(Classifier.DockResult(request, new HarborDockResult { RequestId = "dock-2", Success = true, WorktreePath = DockPath, RepositoryPath = CheckoutPath }));
            }

            public HarborLogEntry Git(string directory, int exitCode, List<int>? expected, params string[] args)
            {
                return Git(directory, exitCode, expected, false, args);
            }

            public HarborLogEntry Git(string directory, int exitCode, List<int>? expected, bool timedOut, params string[] args)
            {
                _Sequence++;
                HarborGitRequest request = new HarborGitRequest
                {
                    RequestId = "req-" + _Sequence,
                    WorkingDirectory = directory,
                    Arguments = new List<string>(args),
                    ExpectedExitCodes = expected
                };
                HarborLogEntry start = Classifier.GitRequest(request);
                AssertEqual(HarborLogLevelEnum.Detail, start.Level, "a routine command's request is detail");
                Log.Add(start);
                HarborLogEntry result = Classifier.GitResult(request, new HostCommandResult { ExitCode = exitCode, TimedOut = timedOut });
                Log.Add(result);
                return result;
            }

            public void FileWrite(string path, HarborFileOperationEnum operation)
            {
                _Sequence++;
                HarborFileRequest request = new HarborFileRequest { RequestId = "file-" + _Sequence, Operation = operation, Path = path };
                Log.Add(Classifier.FileResult(request, new HarborFileResult { RequestId = request.RequestId, Success = true }));
            }

            public void LaunchMission()
            {
                _Launch = new HarborLaunchRequest { JobId = "aa5a0235job", Runtime = "ClaudeCode", JobKind = "Mission", MissionId = _Mission, WorkingDirectory = DockPath };
                Log.Add(Classifier.Launch(_Launch, DockPath, DateTime.UtcNow));
                Log.Add(Classifier.Started(_Launch, 72495));
            }

            public void Exit(int exitCode, long durationMs, long? firstOutputMs)
            {
                Log.Add(Classifier.Exited(_Launch!, exitCode, durationMs, firstOutputMs));
            }
        }

        private sealed class FakeTransport : IHarborTransport
        {
            private readonly Queue<string> _Inbound = new Queue<string>();

            public List<string> Sent { get; } = new List<string>();

            public void Enqueue(string message) => _Inbound.Enqueue(message);

            public Task ConnectAsync(CancellationToken token) => Task.CompletedTask;

            public Task SendAsync(string text, CancellationToken token)
            {
                lock (Sent) Sent.Add(text);
                return Task.CompletedTask;
            }

            public Task<string?> ReceiveAsync(CancellationToken token)
            {
                if (_Inbound.Count == 0) return Task.FromResult<string?>(null);
                return Task.FromResult<string?>(_Inbound.Dequeue());
            }

            public Task CloseAsync(CancellationToken token) => Task.CompletedTask;
        }

        private sealed class StubExecutor : IHostCommandExecutor
        {
            private readonly HostCommandResult _Result;

            public StubExecutor(HostCommandResult result) => _Result = result;

            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default) => Task.FromResult(_Result);
        }

        private sealed class RecordingExecutor : IHostCommandExecutor
        {
            private readonly Func<HostCommandRequest, int> _ExitCode;
            private readonly List<HostCommandRequest> _Requests = new List<HostCommandRequest>();

            public RecordingExecutor(Func<HostCommandRequest, int> exitCode) => _ExitCode = exitCode;

            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
            {
                lock (_Requests) _Requests.Add(request);
                return Task.FromResult(new HostCommandResult { ExitCode = _ExitCode(request) });
            }

            public HostCommandRequest? Find(string firstArgument, string? alsoArgument = null)
            {
                lock (_Requests)
                {
                    foreach (HostCommandRequest request in _Requests)
                        if (request.Arguments.Count > 0 && request.Arguments[0] == firstArgument
                            && (alsoArgument == null || request.Arguments.Contains(alsoArgument))) return request;
                }

                return null;
            }
        }

        #endregion
    }
}
