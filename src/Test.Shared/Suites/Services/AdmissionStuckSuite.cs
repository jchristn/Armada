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

    /// <summary>Consecutive dispatch opportunities, bounded sampling and read-only full preflight.</summary>
    public sealed class AdmissionStuckSuite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            const string suite = "Services.AdmissionStuck";
            List<TestCaseDescriptor> cases = new();
            void Add(string id, Func<Task> test) => cases.Add(new TestCaseDescriptor(suite, id, id, _ => test()));
            Add("three_complete_opportunities_emit_once", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                Mission mission = await db.Missions.CreateAsync(new Mission("stuck") { TenantId = Constants.DefaultTenantId, UserId = Constants.DefaultUserId });
                AdmissionStuckMonitor monitor = new(db);
                for (int i = 0; i < 2; i++) { await Tick(monitor); AssertEqual(0, (await db.Events.EnumerateByTypeAsync("mission.admission_stuck")).Count); }
                await Tick(monitor); await Tick(monitor);
                ArmadaEvent evt = (await db.Events.EnumerateByTypeAsync("mission.admission_stuck")).Single();
                AssertEqual(mission.Id, evt.MissionId); AssertEqual(mission.TenantId, evt.TenantId); AssertEqual(mission.UserId, evt.UserId);
                AssertEqual(MissionStatusEnum.Pending, (await db.Missions.ReadAsync(mission.Id))!.Status);
            });
            foreach (string reset in new[] { "blocked_before", "blocked_after", "memory_before", "memory_after", "dispatch_failed", "new_version", "assigned", "deleted" })
                Add("reset_" + reset, async () =>
                {
                    using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                    Mission mission = await db.Missions.CreateAsync(new Mission("reset")); AdmissionStuckMonitor monitor = new(db);
                    await Tick(monitor); await Tick(monitor);
                    var window = await monitor.CaptureAsync(() => reset != "memory_before", (_, _) => Task.FromResult(reset != "blocked_before"), default);
                    if (reset == "new_version") { mission.Description = "changed"; await db.Missions.UpdateAsync(mission); }
                    if (reset == "assigned") { mission.Status = MissionStatusEnum.Assigned; await db.Missions.UpdateAsync(mission); }
                    if (reset == "deleted") await db.Missions.DeleteAsync(mission.Id);
                    await monitor.CompleteAsync(window, reset != "dispatch_failed", () => reset != "memory_after", (_, _) => Task.FromResult(reset != "blocked_after"), default);
                    AssertEqual(0, (await db.Events.EnumerateByTypeAsync("mission.admission_stuck")).Count);
                    if (reset != "assigned" && reset != "deleted") { await Tick(monitor); AssertEqual(0, (await db.Events.EnumerateByTypeAsync("mission.admission_stuck")).Count); }
                });
            Add("out_of_order_windows_do_not_add_false_ticks", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                await db.Missions.CreateAsync(new Mission("overlap")); AdmissionStuckMonitor monitor = new(db);
                var first = await monitor.CaptureAsync(() => true, Ready, default); var second = await monitor.CaptureAsync(() => true, Ready, default);
                await monitor.CompleteAsync(second, true, () => true, Ready, default);
                await monitor.CompleteAsync(first, true, () => true, Ready, default);
                await Tick(monitor); AssertEqual(0, (await db.Events.EnumerateByTypeAsync("mission.admission_stuck")).Count);
            });
            Add("large_queues_are_sampled_in_bounded_rotating_windows", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                for (int i = 0; i < 70; i++) await db.Missions.CreateAsync(new Mission("queued " + i));
                AdmissionStuckMonitor monitor = new(db); HashSet<string> observed = new(); int probes = 0;
                Task<bool> Probe(Mission m, CancellationToken _) { probes++; observed.Add(m.Id); return Task.FromResult(true); }
                for (int i = 0; i < 6; i++)
                { int before = probes; var window = await monitor.CaptureAsync(() => true, Probe, default); await monitor.CompleteAsync(window, true, () => true, Probe, default); AssertTrue(probes - before <= 128); }
                AssertEqual(70, observed.Count);
            });
            Add("stuck_counter_has_no_identifying_labels", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                await db.Missions.CreateAsync(new Mission("counter")); long count = 0;
                using MeterListener listener = new();
                listener.InstrumentPublished = (instrument, current) => { if (instrument.Name == "armada.admission.stuck") current.EnableMeasurementEvents(instrument); };
                listener.SetMeasurementEventCallback<long>((_, value, tags, _) => { AssertEqual(0, tags.Length); count += value; }); listener.Start();
                AdmissionStuckMonitor monitor = new(db); await Tick(monitor); await Tick(monitor); await Tick(monitor); await Tick(monitor); AssertEqual(1L, count);
            });
            foreach (string reason in new[] { "ready", "policy", "placement", "dependency", "terminal_voyage", "inactive", "missing_launcher" })
                Add("full_preflight_" + reason, async () =>
                {
                    using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                    LoggingModule log = new(); log.Settings.EnableConsole = false; ArmadaSettings settings = new();
                    StubGitService git = new(); DockService docks = new(log, db, settings, git); CaptainService captains = new(log, db, settings, git, docks);
                    MissionService service = new(log, db, settings, docks, captains);
                    int launches = 0;
                    if (reason != "missing_launcher") captains.OnLaunchAgent = (_, _, _) => { launches++; return Task.FromResult(8001); };
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("probe", "https://example.invalid/repo"));
                    Captain captain = await db.Captains.CreateAsync(new Captain("probe") { Active = reason != "inactive" });
                    Mission mission = await db.Missions.CreateAsync(new Mission("probe") { VesselId = vessel.Id });
                    if (reason == "policy") service.CanAssignMissionAsync = (_, _) => Task.FromResult(false);
                    if (reason == "placement") service.ResolveDockPlacementAsync = (_, _, _) => Task.FromResult(DockPlacement.WaitFor("Harbor unavailable"));
                    if (reason == "dependency") { mission.DependsOnMissionId = "msn_missing"; await db.Missions.UpdateAsync(mission); }
                    if (reason == "terminal_voyage") { Voyage voyage = await db.Voyages.CreateAsync(new Voyage("terminal") { Status = VoyageStatusEnum.Cancelled }); mission.VoyageId = voyage.Id; await db.Missions.UpdateAsync(mission); }
                    mission = (await db.Missions.ReadAsync(mission.Id))!; DateTime version = mission.LastUpdateUtc;
                    AssertEqual(reason == "ready", await service.IsAdmissionReadyForMonitoringAsync(mission));
                    AssertEqual(version, (await db.Missions.ReadAsync(mission.Id))!.LastUpdateUtc);
                    AssertEqual(0, (await db.Captains.ReadAsync(captain.Id))!.ActiveMissionCount);
                    AssertEqual(0, (await db.Docks.EnumerateAsync()).Count); AssertEqual(0, launches);
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
            Add("routing_override_probe_does_not_persist_or_mutate_preferences", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                LoggingModule log = new(); log.Settings.EnableConsole = false; ArmadaSettings settings = new();
                StubGitService git = new(); DockService docks = new(log, db, settings, git); CaptainService captains = new(log, db, settings, git, docks);
                captains.OnLaunchAgent = (_, _, _) => Task.FromResult(8001); MissionService service = new(log, db, settings, docks, captains);
                Captain captain = await db.Captains.CreateAsync(new Captain("override"));
                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("override", "https://example.invalid/repo"));
                Voyage voyage = await db.Voyages.CreateAsync(new Voyage("override") { CaptainOverridesJson = JsonSerializer.Serialize(new[] { new CaptainAssignmentOverride { Persona = "*", CaptainId = captain.Id } }) });
                Mission mission = await db.Missions.CreateAsync(new Mission("probe") { VesselId = vessel.Id, VoyageId = voyage.Id });
                bool resolved = false; service.CanAssignMissionAsync = (view, _) => { resolved = view.RequestedCaptainId == captain.Id; return Task.FromResult(true); };
                AssertTrue(await service.IsAdmissionReadyForMonitoringAsync(mission)); AssertTrue(resolved);
                AssertNull(mission.RequestedCaptainId); AssertNull((await db.Missions.ReadAsync(mission.Id))!.RequestedCaptainId);
            });
            return new TestSuiteDescriptor(suite, "Stuck admission observations", cases);
        }
        private static Task<bool> Ready(Mission _, CancellationToken token) => Task.FromResult(true);
        private static async Task Tick(AdmissionStuckMonitor monitor)
        { var window = await monitor.CaptureAsync(() => true, Ready, default); await monitor.CompleteAsync(window, true, () => true, Ready, default); }
    }
}
