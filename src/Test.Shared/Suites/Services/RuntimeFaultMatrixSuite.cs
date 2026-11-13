namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Fault-injection matrix (T1 in archive/HANDOFF_EXAMINATION_RESULTS.md). Each case runs real missions through the real
    /// lifecycle handler, Admiral, mission service, docks and git, with a scripted captain runtime that reproduces one way
    /// heterogeneous runtimes and local models misbehave: a millisecond exit, a lost stream, overload, an unclassified
    /// crash, and a Judge whose output format drifts. The assertions are about outcomes, not runtime identity, so a
    /// regression in dispatch or handoff shows up here before it shows up in a mixed fleet.
    /// </summary>
    public sealed class RuntimeFaultMatrixSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.RuntimeFaultMatrix";

        private const string ValidPass =
            "## Correctness\nThe change adds the requested file, the build passes and every existing test still passes. I read the diff line by line and ran the suite locally.\n"
            + "## Blast Radius\nOnly one new file is added; no existing behavior, configuration, or public interface changes, so nothing downstream is affected.\n"
            + "## Source Fidelity\nThe implementation matches the mission description exactly and does not invent requirements or skip any acceptance criterion.\n"
            + "## Verdict\n[ARMADA:VERDICT] PASS";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("millisecond_exit_completes_and_releases_captain", "A runtime that exits before its launch is recorded still completes its mission and frees its captain", TestTags.Reliability, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                StubCaptainBehavior behavior = new StubCaptainBehavior
                {
                    OnMission = launch => new StubMissionScript { ExitImmediately = true, FinalMessage = "Added the file.\n[ARMADA:RESULT] COMPLETE" }
                };
                using Scenario s = await Scenario.CreateAsync(git, behavior).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync("instant").ConfigureAwait(false);
                Mission mission = await s.CreateMissionAsync(vessel, null, null).ConfigureAwait(false);

                AssertTrue(await s.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false));
                Mission done = await WaitForSettledAsync(s, mission.Id).ConfigureAwait(false);
                AssertTrue(done.Status == MissionStatusEnum.Complete || done.Status == MissionStatusEnum.WorkProduced, "completed: " + done.Status + " " + done.FailureReason);
                await WaitForCaptainIdleAsync(s, captain.Id).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("lost_stream_resumes_in_place_and_completes", "A model stream that drops mid-run is transient: the captain resumes in the same dock and the mission completes", TestTags.Reliability, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                StubCaptainBehavior behavior = new StubCaptainBehavior
                {
                    OnMission = launch => launch.LaunchNumber == 1
                        ? new StubMissionScript { ExitCode = 1, Commit = false, ProviderError = new RuntimeProviderError { ErrorType = "llm_stream_error", Reason = "mux lost the model's response stream" } }
                        : new StubMissionScript { FinalMessage = "Finished after the retry.\n[ARMADA:RESULT] COMPLETE" }
                };
                using Scenario s = await Scenario.CreateAsync(git, behavior).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync("flaky-local").ConfigureAwait(false);
                Mission mission = await s.CreateMissionAsync(vessel, null, null).ConfigureAwait(false);

                AssertTrue(await s.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false));
                Mission done = await WaitForSettledAsync(s, mission.Id).ConfigureAwait(false);
                AssertTrue(done.Status == MissionStatusEnum.Complete || done.Status == MissionStatusEnum.WorkProduced, "completed: " + done.Status + " " + done.FailureReason);
                AssertEqual(2, behavior.MissionLaunches, "relaunched exactly once");
                Captain? after = await s.Db.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                AssertTrue(after!.State != CaptainStateEnum.Quarantined, "a lost stream never quarantines the captain");
            }));

            cases.Add(CaseAsync("overload_is_retried_not_quarantined", "Provider overload (529) is retried instead of failing the mission and quarantining the captain", TestTags.Reliability, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                StubCaptainBehavior behavior = new StubCaptainBehavior
                {
                    OnMission = launch => launch.LaunchNumber == 1
                        ? new StubMissionScript { ExitCode = 1, Commit = false, ProviderError = new RuntimeProviderError { HttpStatusCode = 529, ErrorType = "overloaded_error", Reason = "Overloaded" } }
                        : new StubMissionScript { FinalMessage = "Done.\n[ARMADA:RESULT] COMPLETE" }
                };
                using Scenario s = await Scenario.CreateAsync(git, behavior).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync("frontier").ConfigureAwait(false);
                Mission mission = await s.CreateMissionAsync(vessel, null, null).ConfigureAwait(false);

                AssertTrue(await s.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false));
                Mission done = await WaitForSettledAsync(s, mission.Id).ConfigureAwait(false);
                AssertTrue(done.Status == MissionStatusEnum.Complete || done.Status == MissionStatusEnum.WorkProduced, "completed: " + done.Status + " " + done.FailureReason);
                Captain? after = await s.Db.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                AssertTrue(after!.State != CaptainStateEnum.Quarantined, "overload is backpressure, not an exhausted quota");
            }));

            cases.Add(CaseAsync("unclassified_crash_fails_once_with_reason", "An exit 1 with no structured error is a crash: the mission fails once with a reason and is not relaunched", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                StubCaptainBehavior behavior = new StubCaptainBehavior
                {
                    OnMission = launch => new StubMissionScript { ExitCode = 1, Commit = false, Lines = new List<string> { "segmentation fault" } }
                };
                using Scenario s = await Scenario.CreateAsync(git, behavior).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync("crashy").ConfigureAwait(false);
                Mission mission = await s.CreateMissionAsync(vessel, null, null).ConfigureAwait(false);

                AssertTrue(await s.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false));
                Mission done = await WaitForSettledAsync(s, mission.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Failed, done.Status);
                AssertFalse(String.IsNullOrWhiteSpace(done.FailureReason), "the failure says why");
                AssertEqual(1, behavior.MissionLaunches, "a crash is not relaunched");
                await WaitForCaptainIdleAsync(s, captain.Id).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("judge_format_drift_is_repaired_and_lands", "A Judge whose first answer has no verdict line gets one repair turn; its corrected PASS lets the work land", TestTags.Reliability, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                StubCaptainBehavior behavior = new StubCaptainBehavior
                {
                    OnMission = launch => launch.LaunchNumber switch
                    {
                        1 => new StubMissionScript { FinalMessage = "Added the file.\n[ARMADA:RESULT] COMPLETE" },
                        2 => new StubMissionScript { Commit = false, FinalMessage = "I reviewed it and it looks good to me overall." },
                        _ => new StubMissionScript { Commit = false, FinalMessage = ValidPass }
                    }
                };
                using Scenario s = await Scenario.CreateAsync(git, behavior).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin).ConfigureAwait(false);
                await s.CreateCaptainAsync("worker").ConfigureAwait(false);
                await s.CreateCaptainAsync("judge").ConfigureAwait(false); // a Judge never reviews its own captain's work
                Voyage voyage = await s.Db.Driver.Voyages.CreateAsync(new Voyage("drift") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                Mission worker = await s.CreateMissionAsync(vessel, voyage.Id, "Worker").ConfigureAwait(false);
                Mission judge = await s.CreateMissionAsync(vessel, voyage.Id, "Judge", worker.Id).ConfigureAwait(false);

                AssertTrue(await s.Missions.TryAssignAsync(worker, vessel).ConfigureAwait(false));
                Mission judged = await WaitForSettledAsync(s, judge.Id, TimeSpan.FromSeconds(120)).ConfigureAwait(false);
                AssertTrue(judged.Status == MissionStatusEnum.Complete || judged.Status == MissionStatusEnum.WorkProduced, "the repaired Judge passed: " + judged.Status + " " + judged.FailureReason);
                AssertEqual(1, judged.OutputRepairAttempts, "exactly one repair turn (launches " + behavior.MissionLaunches + ", output: " + judged.AgentOutput + ")");
                AssertEqual(3, behavior.MissionLaunches, "Worker, unusable Judge answer, repaired Judge");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Runtime fault-injection matrix (end to end)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Mission> WaitForSettledAsync(Scenario s, string missionId, TimeSpan? timeout = null)
        {
            Mission? current = null;
            MonotonicDeadline deadline = MonotonicDeadline.After(timeout ?? TimeSpan.FromSeconds(60));
            while (!deadline.Passed)
            {
                // Drive the health loop the Admiral runs on its timer, so recovery and dispatch happen as in production.
                await s.Admiral.HealthCheckAsync().ConfigureAwait(false);
                current = await s.Db.Driver.Missions.ReadAsync(missionId).ConfigureAwait(false);
                // WorkProduced with collected output means the completion flow ran; landing into this fixture's origin is
                // not part of what these cases exercise (the Mux end-to-end suite settles the same way).
                if (current != null && (current.Status == MissionStatusEnum.Complete
                    || (current.Status == MissionStatusEnum.WorkProduced && current.AgentOutput != null && !s.Missions.IsCompletionInFlight(current.Id)
                        && !await HasPendingDependentsAsync(s, current).ConfigureAwait(false))
                    || current.Status == MissionStatusEnum.Failed
                    || current.Status == MissionStatusEnum.LandingFailed
                    || current.Status == MissionStatusEnum.Cancelled))
                    return current;
                await Task.Delay(100).ConfigureAwait(false);
            }

            List<Mission> all = current?.VoyageId != null ? await s.Db.Driver.Missions.EnumerateByVoyageAsync(current.VoyageId!).ConfigureAwait(false) : new List<Mission>();
            throw new AssertionException("mission " + missionId + " did not settle: " + current?.Status + " " + current?.FailureReason
                + " | voyage: " + String.Join("; ", all.Select(m => m.Persona + "=" + m.Status + (m.FailureReason != null ? " (" + m.FailureReason + ")" : "") + " branch=" + m.BranchName + " desc=" + (m.Description ?? "").Length)));
        }

        private static async Task<bool> HasPendingDependentsAsync(Scenario s, Mission mission)
        {
            if (String.IsNullOrEmpty(mission.VoyageId)) return false;
            List<Mission> voyage = await s.Db.Driver.Missions.EnumerateByVoyageAsync(mission.VoyageId!).ConfigureAwait(false);
            return voyage.Any(m => m.DependsOnMissionId == mission.Id && m.Status == MissionStatusEnum.Pending);
        }

        private static async Task WaitForCaptainIdleAsync(Scenario s, string captainId)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(30));
            Captain? captain = null;
            while (!deadline.Passed)
            {
                captain = await s.Db.Driver.Captains.ReadAsync(captainId).ConfigureAwait(false);
                if (captain != null && captain.State == CaptainStateEnum.Idle && captain.ActiveMissionIds.Count == 0) return;
                await Task.Delay(50).ConfigureAwait(false);
            }

            throw new AssertionException("captain " + captainId + " was not released: " + captain?.State + " holding " + captain?.ActiveMissionIds.SingleOrDefault());
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

        #region Private-Types

        private sealed class Scenario : IDisposable
        {
            public TestDatabase Db { get; }

            public ArmadaSettings Settings { get; }

            public MissionService Missions { get; }

            public AdmiralService Admiral { get; }

            public AgentLifecycleHandler Handler { get; }

            private readonly LoggingModule _Logging;

            private Scenario(TestDatabase db, HarborDockGitFixture git, StubCaptainBehavior behavior)
            {
                Db = db;
                _Logging = new LoggingModule();
                _Logging.Settings.EnableConsole = false;
                Settings = new ArmadaSettings();
                Settings.LogDirectory = Path.Combine(git.AdmiralData, "logs");
                Settings.DocksDirectory = Path.Combine(git.AdmiralData, "docks");
                Settings.ReposDirectory = Path.Combine(git.AdmiralData, "repos");
                Settings.TransientResumeDelaySeconds = 0;
                Settings.MaxRecoveryAttempts = 2;

                AgentRuntimeFactory runtimes = new AgentRuntimeFactory(_Logging);
                LoggingModule logging = _Logging;
                runtimes.Override(AgentRuntimeEnum.ClaudeCode, () => new StubCaptainRuntime(logging, behavior));

                GitService gitService = new GitService(_Logging);
                DockService docks = new DockService(_Logging, db.Driver, Settings, gitService);
                CaptainService captains = new CaptainService(_Logging, db.Driver, Settings, gitService, docks);
                Missions = new MissionService(_Logging, db.Driver, Settings, docks, captains, git: gitService);
                Admiral = new AdmiralService(_Logging, db.Driver, Settings, captains, Missions, new VoyageService(_Logging, db.Driver), docks);

                Handler = new AgentLifecycleHandler(
                    _Logging, db.Driver, Settings, runtimes, Admiral, new MessageTemplateService(_Logging), null, null,
                    (eventType, message, entityType, entityId, captainId, missionId, vesselId, voyageId) => Task.CompletedTask);

                MissionLandingHandler landing = new MissionLandingHandler(_Logging, db.Driver, Settings, gitService, new MergeQueueService(_Logging, db.Driver, Settings, gitService),
                    new MessageTemplateService(_Logging), null, docks, null);

                Missions.OnGetMissionOutput = missionId => Handler.GetAndClearMissionOutput(missionId);
                Admiral.OnLaunchAgent = Handler.HandleLaunchAgentAsync;
                Admiral.OnCaptureDiff = landing.HandleCaptureDiffAsync;
                Admiral.OnMissionComplete = landing.HandleMissionCompleteAsync;
                Admiral.OnIsProcessTracked = Handler.IsProcessTracked;
                Admiral.OnIsProcessExitHandled = Handler.IsProcessExitHandled;
            }

            public static async Task<Scenario> CreateAsync(HarborDockGitFixture git, StubCaptainBehavior behavior)
            {
                TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                return new Scenario(db, git, behavior);
            }

            public async Task<Vessel> CreateVesselAsync(string repoUrl)
            {
                Vessel vessel = new Vessel("app", repoUrl);
                vessel.DefaultBranch = "main";
                vessel.LandingMode = LandingModeEnum.LocalMerge;
                return await Db.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            }

            public async Task<Captain> CreateCaptainAsync(string name)
            {
                Captain captain = new Captain(name + "-" + Guid.NewGuid().ToString("N").Substring(0, 6), AgentRuntimeEnum.ClaudeCode);
                captain.State = CaptainStateEnum.Idle;
                return await CaptainAssignmentFixture.CreateAsync(Db.Driver, captain).ConfigureAwait(false);
            }

            public async Task<Mission> CreateMissionAsync(Vessel vessel, string? voyageId, string? persona, string? dependsOn = null)
            {
                Mission mission = new Mission((persona != null ? "[" + persona + "] " : "") + "Add a file", "Add a file to the repository");
                mission.VesselId = vessel.Id;
                mission.VoyageId = voyageId;
                mission.Persona = persona;
                mission.DependsOnMissionId = dependsOn;
                mission.Status = MissionStatusEnum.Pending;
                return await Db.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);
            }

            public void Dispose()
            {
                Db.Dispose();
            }
        }

        #endregion
    }
}
