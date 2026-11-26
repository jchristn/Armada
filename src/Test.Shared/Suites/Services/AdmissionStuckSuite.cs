namespace Test.Shared.Suites.Services
{
    using System.Diagnostics.Metrics;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>The stuck-admission monitor reads dispatch results (RS-4.6) and never evaluates admission itself.</summary>
    public sealed class AdmissionStuckSuite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            const string suite = "Services.AdmissionStuck";
            List<TestCaseDescriptor> cases = new();
            void Add(string id, Func<Task> test) => cases.Add(new TestCaseDescriptor(suite, id, id, _ => test()));
            Add("three_open_passes_emit_once_with_blocker_age", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                Mission mission = await db.Missions.CreateAsync(new Mission("stuck") { TenantId = Constants.DefaultTenantId, UserId = Constants.DefaultUserId });
                AdmissionStuckMonitor monitor = new(db); DateTime since = DateTime.UtcNow.AddMinutes(-5);
                for (int i = 0; i < 2; i++) { await Tick(monitor, mission, since); AssertEqual(0, (await db.Events.EnumerateByTypeAsync("mission.admission_stuck")).Count); }
                await Tick(monitor, mission, since); await Tick(monitor, mission, since);
                ArmadaEvent evt = (await db.Events.EnumerateByTypeAsync("mission.admission_stuck")).Single();
                AssertEqual(mission.Id, evt.MissionId); AssertEqual(mission.TenantId, evt.TenantId); AssertEqual(mission.UserId, evt.UserId);
                AssertContains("BlockedSinceUtc", evt.Payload ?? ""); AssertContains("claim lost", evt.Payload ?? "");
                AssertEqual(MissionStatusEnum.Pending, (await db.Missions.ReadAsync(mission.Id))!.Status);
            });
            foreach (string reset in new[] { "blocked", "not_attempted", "pass_not_completed", "new_version", "assigned", "deleted" })
                Add("reset_" + reset, async () =>
                {
                    using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                    Mission mission = await db.Missions.CreateAsync(new Mission("reset")); AdmissionStuckMonitor monitor = new(db);
                    await Tick(monitor, mission); await Tick(monitor, mission);
                    if (reset == "blocked") await monitor.ObserveAsync(new[] { Result(mission, MissionAssignmentBlockerReasonEnum.PlacementWaiting) }, true);
                    if (reset == "not_attempted") await monitor.ObserveAsync(Array.Empty<MissionAdmissionResult>(), true);
                    if (reset == "pass_not_completed") await monitor.ObserveAsync(new[] { Result(mission) }, false);
                    if (reset == "new_version") { mission.Description = "changed"; await db.Missions.UpdateAsync(mission); mission = (await db.Missions.ReadAsync(mission.Id))!; await Tick(monitor, mission); }
                    if (reset == "assigned") { Mission row = (await db.Missions.ReadAsync(mission.Id))!; row.Status = MissionStatusEnum.Assigned; await db.Missions.UpdateAsync(row); await Tick(monitor, mission); }
                    if (reset == "deleted") { await db.Missions.DeleteAsync(mission.Id); await Tick(monitor, mission); }
                    AssertEqual(0, (await db.Events.EnumerateByTypeAsync("mission.admission_stuck")).Count);
                    if (reset != "assigned" && reset != "deleted") { await Tick(monitor, mission); AssertEqual(0, (await db.Events.EnumerateByTypeAsync("mission.admission_stuck")).Count); }
                });
            Add("a_reported_mission_is_not_reported_again_at_the_same_version", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                Mission mission = await db.Missions.CreateAsync(new Mission("once")); AdmissionStuckMonitor monitor = new(db);
                for (int i = 0; i < 8; i++) await Tick(monitor, mission);
                AssertEqual(1, (await db.Events.EnumerateByTypeAsync("mission.admission_stuck")).Count);
            });
            Add("stuck_counter_has_no_identifying_labels", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                Mission mission = await db.Missions.CreateAsync(new Mission("counter")); long count = 0;
                using MeterListener listener = new();
                listener.InstrumentPublished = (instrument, current) => { if (instrument.Name == "armada.admission.stuck") current.EnableMeasurementEvents(instrument); };
                listener.SetMeasurementEventCallback<long>((_, value, tags, _) => { AssertEqual(0, tags.Length); count += value; }); listener.Start();
                AdmissionStuckMonitor monitor = new(db); for (int i = 0; i < 4; i++) await Tick(monitor, mission); AssertEqual(1L, count);
            });
            foreach (string reason in new[] { "policy", "placement", "dependency", "inactive", "terminal_voyage" })
                Add("dispatch_records_the_blocker_reason_" + reason, async () =>
                {
                    using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                    LoggingModule log = new(); log.Settings.EnableConsole = false; ArmadaSettings settings = new();
                    StubGitService git = new(); DockService docks = new(log, db, settings, git); CaptainService captains = new(log, db, settings, git, docks);
                    MissionService service = new(log, db, settings, docks, captains);
                    int launches = 0;
                    captains.OnLaunchAgent = (_, _, _) => { launches++; return Task.FromResult(8001); };
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("probe", "https://example.invalid/repo"));
                    Captain captain = await db.Captains.CreateAsync(new Captain("probe") { Active = reason != "inactive" });
                    Mission mission = await db.Missions.CreateAsync(new Mission("probe") { VesselId = vessel.Id });
                    if (reason == "policy") service.CanAssignMissionAsync = (_, _) => Task.FromResult(false);
                    if (reason == "placement") service.ResolveDockPlacementAsync = (_, _, _) => Task.FromResult(DockPlacement.WaitFor("Harbor unavailable"));
                    if (reason == "dependency") { mission.DependsOnMissionId = "msn_missing"; await db.Missions.UpdateAsync(mission); }
                    if (reason == "terminal_voyage") { Voyage voyage = await db.Voyages.CreateAsync(new Voyage("terminal") { Status = VoyageStatusEnum.Cancelled }); mission.VoyageId = voyage.Id; await db.Missions.UpdateAsync(mission); }
                    mission = (await db.Missions.ReadAsync(mission.Id))!;
                    AssertFalse(await service.TryAssignAsync(mission, vessel));
                    AssertEqual(0, launches); AssertEqual(0, (await db.Docks.EnumerateAsync()).Count);
                    MissionAdmissionResult? recorded = service.GetAdmissionResult(mission.Id);
                    if (reason == "terminal_voyage") { AssertNull(recorded, "a cancelled mission keeps no admission result"); return; }
                    MissionAssignmentBlocker shown = (await service.GetAssignmentBlockerAsync((await db.Missions.ReadAsync(mission.Id))!))!;
                    AssertEqual(shown.Reason, recorded!.Blocker.Reason, "dispatch and the blocker read one result");
                    AssertTrue(recorded.Blocker.Reason != MissionAssignmentBlockerReasonEnum.AwaitingDispatch, "a closed gate is never reported as open");
                });
            Add("healthy_health_ticks_dispatch_without_stuck_events", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                LoggingModule log = new(); log.Settings.EnableConsole = false;
                ArmadaSettings settings = new() { DocksDirectory = TestTemp.NewDirectory("stuck_docks"), ReposDirectory = TestTemp.NewDirectory("stuck_repos") };
                try
                {
                    DirCreatingGitService git = new(); DockService docks = new(log, db, settings, git); CaptainService captains = new(log, db, settings, git, docks);
                    MissionService service = new(log, db, settings, docks, captains, git: git);
                    captains.OnLaunchAgent = (_, _, _) => Task.FromResult(8001);
                    AdmiralService admiral = new(log, db, settings, captains, service, new VoyageService(log, db), docks) { OnIsProcessTracked = _ => true };
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("healthy", "https://example.invalid/repo"));
                    await db.Captains.CreateAsync(new Captain("healthy"));
                    Mission mission = await db.Missions.CreateAsync(new Mission("healthy") { VesselId = vessel.Id, Mode = MissionModeEnum.Research });
                    for (int i = 0; i < 4; i++) await admiral.HealthCheckAsync();
                    AssertEqual(MissionStatusEnum.InProgress, (await db.Missions.ReadAsync(mission.Id))!.Status);
                    AssertEqual(0, (await db.Events.EnumerateByTypeAsync("mission.admission_stuck")).Count);
                }
                finally { TestTemp.TryDelete(settings.DocksDirectory); TestTemp.TryDelete(settings.ReposDirectory); }
            });
            Add("routing_override_blocker_does_not_persist_or_mutate_preferences", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                LoggingModule log = new(); log.Settings.EnableConsole = false; ArmadaSettings settings = new();
                StubGitService git = new(); DockService docks = new(log, db, settings, git); CaptainService captains = new(log, db, settings, git, docks);
                captains.OnLaunchAgent = (_, _, _) => Task.FromResult(8001); MissionService service = new(log, db, settings, docks, captains);
                Captain captain = await db.Captains.CreateAsync(new Captain("override"));
                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("override", "https://example.invalid/repo"));
                Voyage voyage = await db.Voyages.CreateAsync(new Voyage("override") { CaptainOverridesJson = JsonSerializer.Serialize(new[] { new CaptainAssignmentOverride { Persona = "*", CaptainId = captain.Id } }) });
                Mission mission = await db.Missions.CreateAsync(new Mission("probe") { VesselId = vessel.Id, VoyageId = voyage.Id });
                AssertEqual(MissionAssignmentBlockerReasonEnum.AwaitingDispatch, (await service.GetAssignmentBlockerAsync(mission))!.Reason);
                AssertNull(mission.RequestedCaptainId); AssertNull((await db.Missions.ReadAsync(mission.Id))!.RequestedCaptainId);
            });
            return new TestSuiteDescriptor(suite, "Stuck admission observations", cases);
        }
        private static MissionAdmissionResult Result(Mission mission, MissionAssignmentBlockerReasonEnum reason = MissionAssignmentBlockerReasonEnum.AwaitingDispatch, DateTime? since = null)
            => new MissionAdmissionResult { Mission = mission, Blocker = new MissionAssignmentBlocker { Reason = reason, Summary = "claim lost" }, FirstObservedUtc = since ?? DateTime.UtcNow };
        private static Task Tick(AdmissionStuckMonitor monitor, Mission mission, DateTime? since = null)
            => monitor.ObserveAsync(new[] { Result(mission, since: since) }, true);
    }
}
