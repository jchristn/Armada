namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// FIX_LANDING_FAILURES A3: a stale LandingFailed mission moves to Complete (with a mission.landing_reconciled event)
    /// only when its commit, or a commit carrying its Armada-Mission-Id trailer, is on origin/&lt;default branch&gt;. A
    /// mission without that proof, or whose repository cannot be reached, is left alone, and each sweep is bounded.
    /// </summary>
    public sealed class LandingReconcilerSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.LandingReconciler";

        private const string OriginMain = "refs/remotes/origin/main";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("commit_on_main_completes_with_event", "A LandingFailed mission whose commit is on origin/main becomes Complete with a landing_reconciled event", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string repo = TestTemp.NewDirectory("landing-reconcile");
                StubGitService git = new StubGitService();
                git.IsAncestorHandler = (ancestor, descendant) => ancestor == "8321ac09c" && descendant == OriginMain;
                Vessel vessel = await CreateVesselAsync(testDb, repo).ConfigureAwait(false);
                Mission mission = await CreateLandingFailedAsync(testDb, vessel, "8321ac09c").ConfigureAwait(false);

                LandingReconciler reconciler = CreateReconciler(testDb, git);
                int callbacks = 0;
                reconciler.OnMissionReconciled = (m, evt) => { callbacks++; return Task.CompletedTask; };

                AssertEqual(1, await reconciler.ReconcileAsync().ConfigureAwait(false));
                Mission after = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.Complete, after.Status);
                AssertNotNull(after.CompletedUtc, "completion time set");
                AssertNull(after.FailureReason, "the stale landing failure reason is cleared");
                AssertNull(after.FailureKind, "the stale landing failure kind is cleared");
                AssertEqual(1, callbacks, "callback raised once");

                LandingReconciliationEvidence evidence = await ReadEvidenceAsync(testDb, mission.Id).ConfigureAwait(false);
                AssertEqual("8321ac09c", evidence.EvidenceCommit);
                AssertEqual(LandingEvidenceKindEnum.CommitAncestry, evidence.EvidenceKind);
                AssertEqual(OriginMain, evidence.TargetRef);
                AssertContains("Error merging locally", evidence.PreviousFailureReason ?? "", "the previous reason is kept as the note");
                AssertEqual(0, git.TrailerSearchCalls.Count, "no trailer search once the commit itself is proven");

                AssertEqual(0, await reconciler.ReconcileAsync().ConfigureAwait(false), "idempotent");
            }));

            cases.Add(CaseAsync("trailer_match_completes", "A mission whose own commit never landed is completed by a commit on origin/main carrying its Armada-Mission-Id trailer", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string repo = TestTemp.NewDirectory("landing-reconcile");
                StubGitService git = new StubGitService();
                git.IsAncestorHandler = (ancestor, descendant) => false;
                Vessel vessel = await CreateVesselAsync(testDb, repo).ConfigureAwait(false);
                Mission mission = await CreateLandingFailedAsync(testDb, vessel, "6add5c4f0").ConfigureAwait(false);
                git.TrailerCommitsHandler = value => value == mission.Id ? new List<string> { "8ef44a098" } : new List<string>();

                AssertEqual(1, await CreateReconciler(testDb, git).ReconcileAsync().ConfigureAwait(false));
                Mission after = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.Complete, after.Status);
                LandingReconciliationEvidence evidence = await ReadEvidenceAsync(testDb, mission.Id).ConfigureAwait(false);
                AssertEqual("8ef44a098", evidence.EvidenceCommit);
                AssertEqual(LandingEvidenceKindEnum.MissionTrailer, evidence.EvidenceKind);
                AssertTrue(git.TrailerSearchCalls.Single().StartsWith(OriginMain + "|Armada-Mission-Id: ", StringComparison.Ordinal), git.TrailerSearchCalls.Single());
            }));

            cases.Add(CaseAsync("not_on_main_is_unchanged", "Without commit ancestry or a trailer match the mission stays LandingFailed and no event is recorded", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string repo = TestTemp.NewDirectory("landing-reconcile");
                StubGitService git = new StubGitService();
                git.IsAncestorHandler = (ancestor, descendant) => false;
                Vessel vessel = await CreateVesselAsync(testDb, repo).ConfigureAwait(false);
                Mission mission = await CreateLandingFailedAsync(testDb, vessel, "6add5c4f0").ConfigureAwait(false);
                Mission unresolved = await CreateLandingFailedAsync(testDb, vessel, null).ConfigureAwait(false);

                AssertEqual(0, await CreateReconciler(testDb, git).ReconcileAsync().ConfigureAwait(false));
                AssertEqual(MissionStatusEnum.LandingFailed, (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.Status);
                AssertEqual(MissionStatusEnum.LandingFailed, (await testDb.Driver.Missions.ReadAsync(unresolved.Id).ConfigureAwait(false))!.Status);
                AssertEqual(0, await CountReconciledEventsAsync(testDb, mission.Id).ConfigureAwait(false));
                AssertEqual(2, git.TrailerSearchCalls.Count, "both missions were searched by trailer");
            }));

            cases.Add(CaseAsync("unresolvable_commit_is_not_proof", "A commit that does not resolve on the host (ancestry unknown) is not treated as landed", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string repo = TestTemp.NewDirectory("landing-reconcile");
                StubGitService git = new StubGitService();
                git.IsAncestorHandler = (ancestor, descendant) => null;
                Vessel vessel = await CreateVesselAsync(testDb, repo).ConfigureAwait(false);
                Mission mission = await CreateLandingFailedAsync(testDb, vessel, "deadbeef").ConfigureAwait(false);

                AssertEqual(0, await CreateReconciler(testDb, git).ReconcileAsync().ConfigureAwait(false));
                AssertEqual(MissionStatusEnum.LandingFailed, (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.Status);
            }));

            cases.Add(CaseAsync("host_unreachable_is_unchanged", "A vessel whose repository is not on the Admiral and has no reachable Harbor is skipped without git calls", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                StubGitService git = new StubGitService();
                git.IsAncestorHandler = (ancestor, descendant) => true;
                Vessel vessel = await CreateVesselAsync(testDb, Path.Combine(Path.GetTempPath(), "armada_missing_" + Guid.NewGuid().ToString("N"))).ConfigureAwait(false);
                Mission mission = await CreateLandingFailedAsync(testDb, vessel, "8321ac09c").ConfigureAwait(false);

                AssertEqual(0, await CreateReconciler(testDb, git).ReconcileAsync().ConfigureAwait(false));
                AssertEqual(MissionStatusEnum.LandingFailed, (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.Status);
                AssertEqual(0, git.FetchCalls.Count, "nothing fetched");
                AssertEqual(0, await CountReconciledEventsAsync(testDb, mission.Id).ConfigureAwait(false));
            }));

            cases.Add(CaseAsync("fetch_failure_is_unchanged", "A repository that cannot be fetched is skipped rather than judged on stale refs", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string repo = TestTemp.NewDirectory("landing-reconcile");
                StubGitService git = new StubGitService();
                git.IsAncestorHandler = (ancestor, descendant) => true;
                git.ShouldThrowOnFetch = true;
                Vessel vessel = await CreateVesselAsync(testDb, repo).ConfigureAwait(false);
                Mission first = await CreateLandingFailedAsync(testDb, vessel, "8321ac09c").ConfigureAwait(false);
                Mission second = await CreateLandingFailedAsync(testDb, vessel, "8ef44a098").ConfigureAwait(false);

                AssertEqual(0, await CreateReconciler(testDb, git).ReconcileAsync().ConfigureAwait(false));
                AssertEqual(MissionStatusEnum.LandingFailed, (await testDb.Driver.Missions.ReadAsync(first.Id).ConfigureAwait(false))!.Status);
                AssertEqual(MissionStatusEnum.LandingFailed, (await testDb.Driver.Missions.ReadAsync(second.Id).ConfigureAwait(false))!.Status);
                AssertEqual(1, git.FetchCalls.Count, "a failed fetch is not retried for the vessel's other missions in the same sweep");
            }));

            cases.Add(CaseAsync("batch_bound_and_single_fetch", "Each sweep examines at most MaxPerCycle missions, fetches each vessel once, and later sweeps reach the rest", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string repo = TestTemp.NewDirectory("landing-reconcile");
                StubGitService git = new StubGitService();
                git.IsAncestorHandler = (ancestor, descendant) => descendant == OriginMain;
                Vessel vessel = await CreateVesselAsync(testDb, repo).ConfigureAwait(false);
                List<string> ids = new List<string>();
                for (int i = 0; i < 5; i++)
                    ids.Add((await CreateLandingFailedAsync(testDb, vessel, "c0ffee" + i).ConfigureAwait(false)).Id);

                LandingReconciler reconciler = CreateReconciler(testDb, git);
                reconciler.MaxPerCycle = 2;
                AssertEqual(2, await reconciler.ReconcileAsync().ConfigureAwait(false), "first sweep is bounded");
                AssertEqual(1, git.FetchCalls.Count, "the vessel is fetched once per sweep");

                int total = 2;
                for (int sweep = 0; sweep < 5 && total < 5; sweep++)
                {
                    int done = await reconciler.ReconcileAsync().ConfigureAwait(false);
                    AssertTrue(done <= 2, "sweep reconciled " + done);
                    total += done;
                }

                AssertEqual(5, total, "later sweeps reach every mission");
                foreach (string id in ids)
                    AssertEqual(MissionStatusEnum.Complete, (await testDb.Driver.Missions.ReadAsync(id).ConfigureAwait(false))!.Status, id);
            }));

            cases.Add(CaseAsync("other_statuses_are_ignored", "Failed and WorkProduced missions are never reconciled, even with a commit on origin/main", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string repo = TestTemp.NewDirectory("landing-reconcile");
                StubGitService git = new StubGitService();
                git.IsAncestorHandler = (ancestor, descendant) => true;
                Vessel vessel = await CreateVesselAsync(testDb, repo).ConfigureAwait(false);
                Mission failed = await CreateLandingFailedAsync(testDb, vessel, "8321ac09c").ConfigureAwait(false);
                failed.Status = MissionStatusEnum.Failed;
                await testDb.Driver.Missions.UpdateAsync(failed).ConfigureAwait(false);

                AssertEqual(0, await CreateReconciler(testDb, git).ReconcileAsync().ConfigureAwait(false));
                AssertEqual(MissionStatusEnum.Failed, (await testDb.Driver.Missions.ReadAsync(failed.Id).ConfigureAwait(false))!.Status);
            }));

            cases.Add(CaseAsync("git_trailer_search_matches_exact_trailer_only", "GitService finds a commit by its exact Armada-Mission-Id trailer on the remote ref and ignores prose mentions", TestTags.Positive, async () =>
            {
                string repo = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    RunGit(repo, "commit", "--allow-empty", "-m", "Rescue of msn_target_abc\n\nMentions msn_target_abc in prose only.");
                    RunGit(repo, "commit", "--allow-empty", "-m", "Land the feature\n\nArmada-Mission-Id: msn_target_abc");
                    string trailered = RunGit(repo, "rev-parse", "HEAD").Trim();
                    RunGit(repo, "commit", "--allow-empty", "-m", "Other work\n\nArmada-Mission-Id: msn_target_abcd");
                    RunGit(repo, "update-ref", OriginMain, "HEAD");

                    LoggingModule logging = CreateLogging();
                    GitService service = new GitService(logging);
                    IReadOnlyList<string> found = await service.FindCommitsByTrailerAsync(repo, OriginMain, "Armada-Mission-Id", "msn_target_abc", DateTime.UtcNow.AddDays(-1), 5).ConfigureAwait(false);
                    AssertEqual(1, found.Count, String.Join(",", found));
                    AssertEqual(trailered, found[0]);

                    IReadOnlyList<string> none = await service.FindCommitsByTrailerAsync(repo, OriginMain, "Armada-Mission-Id", "msn_missing", null, 5).ConfigureAwait(false);
                    AssertEqual(0, none.Count);
                    IReadOnlyList<string> missingRef = await service.FindCommitsByTrailerAsync(repo, "refs/remotes/origin/absent", "Armada-Mission-Id", "msn_target_abc", null, 5).ConfigureAwait(false);
                    AssertEqual(0, missingRef.Count, "an unresolvable ref is no match, not an error");
                }
                finally
                {
                    TestTemp.TryDelete(repo);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Landing reconciler (stale LandingFailed to Complete)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LandingReconciler CreateReconciler(TestDatabase testDb, StubGitService git)
        {
            return new LandingReconciler(testDb.Driver, git, CreateLogging());
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static async Task<Vessel> CreateVesselAsync(TestDatabase testDb, string localPath)
        {
            Vessel vessel = new Vessel("reconcile-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://github.com/test/repo.git");
            vessel.LocalPath = localPath;
            vessel.DefaultBranch = "main";
            return await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
        }

        private static async Task<Mission> CreateLandingFailedAsync(TestDatabase testDb, Vessel vessel, string? commitHash)
        {
            Mission mission = new Mission("landing failed", "work");
            mission.VesselId = vessel.Id;
            mission.BranchName = "armada/landing-failed";
            mission.CommitHash = commitHash;
            mission.Status = MissionStatusEnum.LandingFailed;
            mission.FailureKind = MissionFailureKindEnum.Infra;
            mission.FailureReason = "Error merging locally: Git checkout contains tracked modifications";
            return await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);
        }

        private static async Task<LandingReconciliationEvidence> ReadEvidenceAsync(TestDatabase testDb, string missionId)
        {
            List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(missionId, 100).ConfigureAwait(false);
            ArmadaEvent evt = events.Single(e => e.EventType == LandingReconciler.EventType);
            AssertEqual("mission", evt.EntityType);
            AssertEqual(missionId, evt.MissionId);
            return JsonSerializer.Deserialize<LandingReconciliationEvidence>(evt.Payload!)!;
        }

        private static async Task<int> CountReconciledEventsAsync(TestDatabase testDb, string missionId)
        {
            List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(missionId, 100).ConfigureAwait(false);
            return events.Count(e => e.EventType == LandingReconciler.EventType);
        }

        private static string RunGit(string workingDirectory, params string[] arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);

            using (Process process = new Process { StartInfo = startInfo })
            {
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("git " + String.Join(" ", arguments) + " failed (exit " + process.ExitCode + "): " + standardError.Trim());
                return output;
            }
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
