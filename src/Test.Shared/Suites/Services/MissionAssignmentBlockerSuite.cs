namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Regression coverage for F11: a Pending mission explains why it is waiting (typed reason plus detail) instead of
    /// sitting silently. Each case builds the gate that holds the mission and checks the typed blocker, never its text.
    /// </summary>
    public sealed class MissionAssignmentBlockerSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.MissionAssignmentBlocker";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("not_pending_has_no_blocker", "A mission that is not Pending has no assignment blocker", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "done", MissionStatusEnum.Complete);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertNull(blocker, "no blocker for a Complete mission");
                }
            }));

            cases.Add(CaseAsync("captain_refining_backlog_item", "The only captain refining a backlog item is named, with the session and backlog item", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    Captain captain = await AddCaptainAsync(testDb.Driver, "Setup Captain", CaptainStateEnum.Refining);
                    Objective objective = await testDb.Driver.Objectives.CreateAsync(new Objective { Title = "Add retries", Status = ObjectiveStatusEnum.Draft, BacklogState = ObjectiveBacklogStateEnum.Inbox });
                    ObjectiveRefinementSession session = await testDb.Driver.ObjectiveRefinementSessions.CreateAsync(new ObjectiveRefinementSession
                    {
                        ObjectiveId = objective.Id,
                        CaptainId = captain.Id,
                        Status = ObjectiveRefinementSessionStatusEnum.Active
                    });

                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "waiting", MissionStatusEnum.Pending);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertNotNull(blocker, "a Pending mission has a blocker");
                    AssertEqual(MissionAssignmentBlockerReasonEnum.NoIdleCaptain, blocker!.Reason, "reason");
                    AssertEqual(1, blocker.Captains.Count, "the busy captain is listed");
                    AssertEqual(captain.Id, blocker.Captains[0].CaptainId, "captain id");
                    AssertEqual(CaptainStateEnum.Refining, blocker.Captains[0].State, "captain state");
                    AssertEqual(session.Id, blocker.Captains[0].RefinementSessionId, "refinement session");
                    AssertEqual(objective.Id, blocker.Captains[0].ObjectiveId, "backlog item");
                    AssertFalse(String.IsNullOrEmpty(blocker.Summary), "summary text");
                }
            }));

            cases.Add(CaseAsync("captain_quarantined_until", "A quarantined captain reports when the quarantine ends", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    DateTime until = DateTime.UtcNow.AddMinutes(15);
                    Captain captain = new Captain("Crashy");
                    captain.State = CaptainStateEnum.Quarantined;
                    captain.QuarantineUntilUtc = until;
                    captain.QuarantineReason = "crash loop";
                    captain = await testDb.Driver.Captains.CreateAsync(captain);

                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "waiting", MissionStatusEnum.Pending);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertNotNull(blocker, "blocker");
                    AssertEqual(MissionAssignmentBlockerReasonEnum.NoIdleCaptain, blocker!.Reason, "reason");
                    AssertEqual(CaptainStateEnum.Quarantined, blocker.Captains[0].State, "state");
                    AssertNotNull(blocker.Captains[0].QuarantineUntilUtc, "quarantine end on the captain");
                    AssertNotNull(blocker.UntilUtc, "blocker clears when the quarantine ends");
                    AssertTrue(Math.Abs((blocker.UntilUtc!.Value - until).TotalSeconds) < 2, "until matches the quarantine end");
                }
            }));

            cases.Add(CaseAsync("vessel_concurrency_limit", "A vessel that runs one mission at a time names the mission holding it", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    await AddCaptainAsync(testDb.Driver, "Idle One", CaptainStateEnum.Idle);
                    Mission running = await AddMissionAsync(testDb.Driver, h.Vessel, "running", MissionStatusEnum.InProgress);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "waiting", MissionStatusEnum.Pending);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertNotNull(blocker, "blocker");
                    AssertEqual(MissionAssignmentBlockerReasonEnum.VesselConcurrencyLimit, blocker!.Reason, "reason");
                    AssertTrue(blocker.BlockingMissionIds.Contains(running.Id), "the running mission is named");
                }
            }));

            cases.Add(CaseAsync("dependency_not_finished", "A pipeline stage waits for its dependency", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    await AddCaptainAsync(testDb.Driver, "Idle One", CaptainStateEnum.Idle);
                    Mission upstream = await AddMissionAsync(testDb.Driver, h.Vessel, "upstream", MissionStatusEnum.Pending);
                    Mission mission = new Mission("downstream", "do it");
                    mission.VesselId = h.Vessel.Id;
                    mission.DependsOnMissionId = upstream.Id;
                    mission = await testDb.Driver.Missions.CreateAsync(mission);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertNotNull(blocker, "blocker");
                    AssertEqual(MissionAssignmentBlockerReasonEnum.DependencyNotFinished, blocker!.Reason, "reason");
                    AssertEqual(upstream.Id, blocker.DependsOnMissionId, "dependency id");
                }
            }));

            cases.Add(CaseAsync("no_captains", "No captains at all is its own reason", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "waiting", MissionStatusEnum.Pending);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.NoCaptains, blocker!.Reason, "reason");
                }
            }));

            cases.Add(CaseAsync("persona_fence", "Idle captains that may not take the persona give NoEligibleCaptain", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    Captain judgeOnly = new Captain("Judge Only");
                    judgeOnly.State = CaptainStateEnum.Idle;
                    judgeOnly.AllowedPersonas = "[\"Judge\"]";
                    await testDb.Driver.Captains.CreateAsync(judgeOnly);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "worker", MissionStatusEnum.Pending);
                    mission.Persona = "Worker";
                    await testDb.Driver.Missions.UpdateAsync(mission);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker!.Reason, "reason");
                }
            }));

            cases.Add(CaseAsync("idle_captain_awaiting_dispatch", "With an idle eligible captain the mission is only awaiting the next dispatch", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    await AddCaptainAsync(testDb.Driver, "Idle One", CaptainStateEnum.Idle);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "waiting", MissionStatusEnum.Pending);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.AwaitingDispatch, blocker!.Reason, "reason");
                    AssertEqual(0, blocker.Captains.Count, "no captain detail needed");
                }
            }));

            return new TestSuiteDescriptor(SuiteId, "Mission assignment blocker", cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private static async Task<Harness> BuildHarnessAsync(DatabaseDriver db)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_blocker_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_blocker_repos_" + Guid.NewGuid().ToString("N"));

            DirCreatingGitService git = new DirCreatingGitService();
            IDockService dockService = new DockService(logging, db, settings, git);
            CaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            MissionService missionService = new MissionService(logging, db, settings, dockService, captainService, git: git);

            Vessel vessel = new Vessel("blocker-vessel", "https://github.com/test/repo.git");
            vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_blocker_bare_" + Guid.NewGuid().ToString("N"));
            vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_blocker_work_" + Guid.NewGuid().ToString("N"));
            vessel.DefaultBranch = "main";
            vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            return new Harness { Missions = missionService, Vessel = vessel };
        }

        private static async Task<Captain> AddCaptainAsync(DatabaseDriver db, string name, CaptainStateEnum state)
        {
            Captain captain = new Captain(name);
            captain.State = state;
            return await db.Captains.CreateAsync(captain).ConfigureAwait(false);
        }

        private static async Task<Mission> AddMissionAsync(DatabaseDriver db, Vessel vessel, string title, MissionStatusEnum status)
        {
            Mission mission = new Mission(title, "do the work");
            mission.VesselId = vessel.Id;
            mission.Status = status;
            return await db.Missions.CreateAsync(mission).ConfigureAwait(false);
        }

        #endregion

        #region Private-Types

        private sealed class Harness
        {
            public MissionService Missions { get; set; } = null!;
            public Vessel Vessel { get; set; } = null!;
        }

        #endregion
    }
}
