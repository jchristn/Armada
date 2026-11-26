namespace Test.Shared.Suites.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>A dispatch pass reads shared admission state once and re-reads only what a claim changed (RS-4.7).</summary>
    public sealed class AdmissionSnapshotSuite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            const string suite = "Services.AdmissionSnapshot";
            List<TestCaseDescriptor> cases = new();
            void Add(string id, Func<Task> test) => cases.Add(new TestCaseDescriptor(suite, id, id, _ => test()));

            Add("running_on_vessel_returns_only_running_missions_of_that_vessel", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("a", "https://example.invalid/a"));
                Vessel other = await db.Vessels.CreateAsync(new Vessel("b", "https://example.invalid/b"));
                List<string> expected = new();
                foreach (MissionStatusEnum status in new[] { MissionStatusEnum.Pending, MissionStatusEnum.Assigned, MissionStatusEnum.InProgress, MissionStatusEnum.Verifying, MissionStatusEnum.Complete })
                {
                    Mission created = await db.Missions.CreateAsync(new Mission("m " + status, "described") { VesselId = vessel.Id, BranchName = "armada/" + status });
                    created.Status = status; await db.Missions.UpdateAsync(created);
                    if (status == MissionStatusEnum.Assigned || status == MissionStatusEnum.InProgress || status == MissionStatusEnum.Verifying) expected.Add(created.Id);
                }
                Mission elsewhere = await db.Missions.CreateAsync(new Mission("elsewhere") { VesselId = other.Id });
                elsewhere.Status = MissionStatusEnum.InProgress; await db.Missions.UpdateAsync(elsewhere);

                List<Mission> running = await db.Missions.EnumerateRunningOnVesselAsync(vessel.Id);
                AssertEqual(String.Join(",", expected.OrderBy(i => i)), String.Join(",", running.Select(m => m.Id).OrderBy(i => i)));
                Mission inProgress = running.Single(m => m.Status == MissionStatusEnum.InProgress);
                AssertEqual("m InProgress", inProgress.Title); AssertEqual("described", inProgress.Description);
                AssertEqual("armada/InProgress", inProgress.BranchName); AssertEqual(vessel.Id, inProgress.VesselId);
            });

            Add("a_pass_reads_shared_state_once_for_every_pending_mission", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                LoggingModule log = new(); log.Settings.EnableConsole = false; ArmadaSettings settings = new() { MaxConcurrentMissions = 5 };
                StubGitService git = new(); DockService docks = new(log, db, settings, git); CaptainService captains = new(log, db, settings, git, docks);
                MissionService service = new(log, db, settings, docks, captains);
                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("reads", "https://example.invalid/repo"));
                Voyage voyage = await db.Voyages.CreateAsync(new Voyage("reads"));
                await db.Captains.CreateAsync(new Captain("inactive") { Active = false });
                List<Mission> missions = new();
                for (int i = 0; i < 6; i++) missions.Add(await db.Missions.CreateAsync(new Mission("reads " + i) { VesselId = vessel.Id, VoyageId = voyage.Id }));

                AssertEqual(-1, service.DispatchPassReads, "outside a pass nothing is cached");
                using (service.BeginDispatchPass())
                {
                    AssertFalse(await service.TryAssignAsync(missions[0], vessel));
                    int first = service.DispatchPassReads;
                    AssertTrue(first > 0, "the first mission fills the pass cache");
                    foreach (Mission mission in missions.Skip(1)) AssertFalse(await service.TryAssignAsync(mission, vessel));
                    AssertEqual(first, service.DispatchPassReads, "five more missions add no shared reads");
                }
                AssertEqual(-1, service.DispatchPassReads, "the pass scope ends with its using block");
                AssertEqual(service.GetAdmissionResult(missions[0].Id)!.Blocker.Reason, service.GetAdmissionResult(missions[5].Id)!.Blocker.Reason, "cached reads give the same answer");
            });

            Add("a_claim_in_the_pass_is_seen_by_the_next_mission", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                LoggingModule log = new(); log.Settings.EnableConsole = false;
                ArmadaSettings settings = new() { MaxConcurrentMissions = 1, DocksDirectory = TestTemp.NewDirectory("snapshot_docks"), ReposDirectory = TestTemp.NewDirectory("snapshot_repos") };
                try
                {
                    DirCreatingGitService git = new(); DockService docks = new(log, db, settings, git); CaptainService captains = new(log, db, settings, git, docks);
                    MissionService service = new(log, db, settings, docks, captains, git: git);
                    captains.OnLaunchAgent = (_, _, _) => Task.FromResult(8001);
                    await db.Captains.CreateAsync(new Captain("one")); await db.Captains.CreateAsync(new Captain("two"));
                    Vessel first = await db.Vessels.CreateAsync(new Vessel("first", "https://example.invalid/first"));
                    Vessel second = await db.Vessels.CreateAsync(new Vessel("second", "https://example.invalid/second"));
                    Mission a = await db.Missions.CreateAsync(new Mission("a") { VesselId = first.Id, Mode = MissionModeEnum.Research });
                    Mission b = await db.Missions.CreateAsync(new Mission("b") { VesselId = second.Id, Mode = MissionModeEnum.Research });
                    using (service.BeginDispatchPass())
                    {
                        AssertTrue(await service.TryAssignAsync(a, first), "the first mission takes the only global slot");
                        AssertFalse(await service.TryAssignAsync(b, second), "the slot count is read again after the claim");
                    }
                    AssertEqual(MissionAssignmentBlockerReasonEnum.GlobalMissionLimit, service.GetAdmissionResult(b.Id)!.Blocker.Reason);
                    AssertEqual(MissionStatusEnum.Pending, (await db.Missions.ReadAsync(b.Id))!.Status);
                }
                finally { TestTemp.TryDelete(settings.DocksDirectory); TestTemp.TryDelete(settings.ReposDirectory); }
            });

            return new TestSuiteDescriptor(suite, "Admission snapshot per dispatch pass", cases);
        }
    }
}
