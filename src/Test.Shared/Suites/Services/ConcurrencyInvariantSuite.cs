namespace Test.Shared.Suites.Services
{
    using System.Diagnostics.Metrics;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Non-preemptive overrun detection and shared voyage occupancy regressions.</summary>
    public sealed class ConcurrencyInvariantSuite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            const string suite = "Services.ConcurrencyInvariant";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, Func<Task> test) => cases.Add(new TestCaseDescriptor(suite, id, id, _ => test()));
            Add("captain_lowering_drains_without_preemption_or_duplicate_events", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                UserMaster owner = await db.Users.CreateAsync(new UserMaster(Constants.DefaultTenantId, "owner@example.invalid", "fixture-only-password"));
                Captain captain = await db.Captains.CreateAsync(new Captain("parallel") { MaxConcurrency = 3, TenantId = owner.TenantId, UserId = owner.Id });
                List<Mission> running = await ClaimAsync(db, captain, 3);
                ConcurrencyInvariantMonitor monitor = Monitor(db);
                await monitor.CheckAsync();
                captain.MaxConcurrency = 1; await db.Captains.UpdateAsync(captain, updateConcurrency: true);
                await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => monitor.CheckAsync()));
                List<ArmadaEvent> events = await Events(db);
                AssertEqual(1, events.Count, "overlapping ticks do not repeat an unchanged warning");
                AssertEqual("limit_lowered", Cause(events[0]));
                AssertEqual(captain.TenantId, events[0].TenantId); AssertEqual(captain.UserId, events[0].UserId);
                AssertEqual(captain.Id, events[0].CaptainId);
                AssertFalse(events[0].Payload!.Contains(running[0].Id), "no other user's mission IDs in a shared-captain event");
                AssertEqual(3, (await db.Captains.ReadAsync(captain.Id))!.ActiveMissionCount);
                foreach (Mission mission in running) AssertEqual(MissionStatusEnum.Assigned, (await db.Missions.ReadAsync(mission.Id))!.Status);
                await FinishAsync(db, captain, running[0]); await monitor.CheckAsync();
                AssertEqual("limit_lowered", Cause((await Events(db))[0]));
                await FinishAsync(db, captain, running[1]); await monitor.CheckAsync();
                AssertEqual(2, (await Events(db)).Count, "at-limit observation is not an overrun");
            });
            Add("initial_overrun_is_unknown_and_restart_does_not_invent_a_cause", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                Captain captain = await db.Captains.CreateAsync(new Captain("restart") { MaxConcurrency = 3 });
                await ClaimAsync(db, captain, 3);
                captain.MaxConcurrency = 1; await db.Captains.UpdateAsync(captain, updateConcurrency: true);
                await Monitor(db).CheckAsync();
                AssertEqual("unknown", Cause((await Events(db))[0]));
                await Monitor(db).CheckAsync();
                AssertTrue((await Events(db)).All(e => Cause(e) == "unknown"));
            });
            Add("new_overrun_and_growth_while_draining_are_unexpected", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                Captain captain = await db.Captains.CreateAsync(new Captain("growth") { MaxConcurrency = 2 });
                await ClaimAsync(db, captain, 2);
                ConcurrencyInvariantMonitor monitor = Monitor(db); await monitor.CheckAsync();
                captain.MaxConcurrency = 1; await db.Captains.UpdateAsync(captain, updateConcurrency: true);
                await monitor.CheckAsync();
                AssertEqual("limit_lowered", Cause((await Events(db))[0]));
                await db.Missions.CreateAsync(new Mission("unexpected ownership") { CaptainId = captain.Id, Status = MissionStatusEnum.InProgress });
                await db.Captains.ReconcileMissionSlotsAsync();
                await monitor.CheckAsync();
                AssertEqual("unexpected", Cause((await Events(db))[0]));
                AssertEqual(3, (await db.Captains.ReadAsync(captain.Id))!.ActiveMissionCount);
            });
            Add("growth_across_a_limit_change_has_an_unknown_order", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                Captain captain = await db.Captains.CreateAsync(new Captain("ambiguous edit") { MaxConcurrency = 4 });
                await ClaimAsync(db, captain, 2);
                ConcurrencyInvariantMonitor monitor = Monitor(db); await monitor.CheckAsync();
                await ClaimAsync(db, captain, 1);
                captain.MaxConcurrency = 1; await db.Captains.UpdateAsync(captain, updateConcurrency: true);
                await monitor.CheckAsync();
                AssertEqual("unknown", Cause((await Events(db))[0]), "claims may have preceded the edit between samples");
                AssertContains("order of capacity and limit changes", (await Events(db))[0].Message);
            });
            Add("overrun_without_a_limit_edit_is_unexpected", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                Captain captain = await db.Captains.CreateAsync(new Captain("unexplained") { MaxConcurrency = 1 });
                ConcurrencyInvariantMonitor monitor = Monitor(db); await monitor.CheckAsync();
                for (int i = 0; i < 2; i++) await db.Missions.CreateAsync(new Mission("external assignment") { CaptainId = captain.Id, Status = MissionStatusEnum.InProgress });
                await db.Captains.ReconcileMissionSlotsAsync(); await monitor.CheckAsync();
                AssertEqual("unexpected", Cause((await Events(db))[0]));
            });
            Add("server_ceiling_reduction_is_observed", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                Captain captain = await db.Captains.CreateAsync(new Captain("server ceiling") { MaxConcurrency = 4 });
                await ClaimAsync(db, captain, 3);
                ArmadaSettings settings = new ArmadaSettings();
                ConcurrencyInvariantMonitor monitor = Monitor(db, settings); await monitor.CheckAsync();
                settings.Concurrency.MaxCaptainConcurrency = 2; await monitor.CheckAsync();
                AssertEqual("limit_lowered", Cause((await Events(db))[0]));
                AssertEqual(4, (await db.Captains.ReadAsync(captain.Id))!.MaxConcurrency);
            });
            Add("unlimited_vessels_and_pending_work_have_no_overrun", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("unlimited", "https://example.invalid/repo") { MaxParallelVoyages = 0, MaxParallelMissionsPerVoyage = 0 });
                for (int i = 0; i < 6; i++) await db.Missions.CreateAsync(new Mission("running") { VesselId = vessel.Id, Status = MissionStatusEnum.InProgress });
                await Monitor(db).CheckAsync(); AssertEqual(0, (await Events(db)).Count);
                Vessel pending = await db.Vessels.CreateAsync(new Vessel("pending", "https://example.invalid/repo") { MaxParallelVoyages = 1 });
                for (int i = 0; i < 6; i++) await db.Missions.CreateAsync(new Mission("pending") { VesselId = pending.Id });
                await Monitor(db).CheckAsync(); AssertEqual(0, (await Events(db)).Count);
            });
            Add("vessel_limit_reductions_share_sticky_admission_and_do_not_preempt", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                UserMaster owner = await db.Users.CreateAsync(new UserMaster(Constants.DefaultTenantId, "owner@example.invalid", "fixture-only-password"));
                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("bounded", "https://example.invalid/repo") { MaxParallelVoyages = 0, MaxParallelMissionsPerVoyage = 0, TenantId = owner.TenantId, UserId = owner.Id });
                Voyage work = await db.Voyages.CreateAsync(new Voyage("work"));
                Voyage review = await db.Voyages.CreateAsync(new Voyage("review"));
                for (int i = 0; i < 3; i++) await db.Missions.CreateAsync(new Mission("parallel " + i) { VesselId = vessel.Id, VoyageId = work.Id, Status = MissionStatusEnum.InProgress });
                await db.Missions.CreateAsync(new Mission("review holds voyage") { VesselId = vessel.Id, VoyageId = review.Id, Status = MissionStatusEnum.Review });
                ConcurrencyInvariantMonitor monitor = Monitor(db); await monitor.CheckAsync();
                vessel.MaxParallelVoyages = 1; vessel.MaxParallelMissionsPerVoyage = 1; await db.Vessels.UpdateAsync(vessel);
                await monitor.CheckAsync(); await monitor.CheckAsync();
                List<ArmadaEvent> events = await Events(db); AssertEqual(2, events.Count);
                AssertTrue(events.All(e => Cause(e) == "limit_lowered" && e.VesselId == vessel.Id && e.TenantId == owner.TenantId && e.UserId == owner.Id));
                AssertTrue(events.All(e => !e.Payload!.Contains(work.Id)), "no private voyage IDs in shared-vessel events");
                AssertEqual(3, (await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress)).Count);
                Mission candidate = new Mission("next") { VesselId = vessel.Id, VoyageId = "vyg_next" };
                AssertEqual(MissionAssignmentBlockerReasonEnum.VesselVoyageLimit,
                    VesselAdmission.EvaluateSnapshots(candidate, vessel, await db.Missions.EnumerateAdmissionSnapshotsAsync(vessel.Id))!.Reason);
            });
            Add("standalone_pipeline_is_one_sticky_group", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("pipeline", "https://example.invalid/repo") { MaxParallelVoyages = 1 });
                Mission worker = await db.Missions.CreateAsync(new Mission("worker") { VesselId = vessel.Id, Status = MissionStatusEnum.WorkProduced });
                await db.Missions.CreateAsync(new Mission("judge") { VesselId = vessel.Id, DependsOnMissionId = worker.Id, Status = MissionStatusEnum.InProgress });
                await Monitor(db).CheckAsync(); AssertEqual(0, (await Events(db)).Count);
            });
            foreach (MissionStatusEnum released in new[] { MissionStatusEnum.Complete, MissionStatusEnum.Failed, MissionStatusEnum.Cancelled, MissionStatusEnum.Blocked, MissionStatusEnum.LandingFailed })
                Add("released_group_" + released, async () =>
                {
                    using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                    var db = fixture.Driver;
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("settled", "https://example.invalid/repo") { MaxParallelVoyages = 1 });
                    await db.Missions.CreateAsync(new Mission("running") { VesselId = vessel.Id, Status = MissionStatusEnum.InProgress });
                    await db.Missions.CreateAsync(new Mission("released") { VesselId = vessel.Id, Status = released });
                    await Monitor(db).CheckAsync(); AssertEqual(0, (await Events(db)).Count);
                });
            Add("terminal_voyages_are_excluded_from_occupancy", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("terminal", "https://example.invalid/repo") { MaxParallelVoyages = 1 });
                foreach (VoyageStatusEnum status in new[] { VoyageStatusEnum.Complete, VoyageStatusEnum.Failed, VoyageStatusEnum.Cancelled })
                {
                    Voyage voyage = await db.Voyages.CreateAsync(new Voyage("terminal") { Status = status });
                    await db.Missions.CreateAsync(new Mission("stale active") { VesselId = vessel.Id, VoyageId = voyage.Id, Status = MissionStatusEnum.InProgress });
                }
                await Monitor(db).CheckAsync(); AssertEqual(0, (await Events(db)).Count);
            });
            Add("metric_dimensions_are_bounded_and_unchanged_ticks_are_deduplicated", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                List<Dictionary<string, object?>> metrics = new List<Dictionary<string, object?>>();
                using MeterListener listener = new MeterListener();
                listener.InstrumentPublished = (instrument, current) => { if (instrument.Name == "armada.concurrency.limit_violations") current.EnableMeasurementEvents(instrument); };
                listener.SetMeasurementEventCallback<long>((_, count, tags, _) =>
                {
                    AssertEqual(1L, count);
                    Dictionary<string, object?> sample = new Dictionary<string, object?>();
                    foreach (var tag in tags) { AssertTrue(tag.Key == "gate" || tag.Key == "cause"); sample.Add(tag.Key, tag.Value); }
                    metrics.Add(sample);
                }); listener.Start();
                Captain captain = await db.Captains.CreateAsync(new Captain("metric") { MaxConcurrency = 2 });
                await ClaimAsync(db, captain, 2);
                ConcurrencyInvariantMonitor monitor = Monitor(db); await monitor.CheckAsync();
                captain.MaxConcurrency = 1; await db.Captains.UpdateAsync(captain, updateConcurrency: true);
                await monitor.CheckAsync(); await monitor.CheckAsync();
                AssertEqual(1, metrics.Count); AssertEqual("captain", metrics[0]["gate"]); AssertEqual("limit_lowered", metrics[0]["cause"]);
            });
            Add("cancelled_tick_preserves_future_checks", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                ConcurrencyInvariantMonitor monitor = Monitor(fixture.Driver);
                using CancellationTokenSource source = new CancellationTokenSource(); source.Cancel();
                bool cancelled = false;
                try { await monitor.CheckAsync(source.Token); } catch (OperationCanceledException) { cancelled = true; }
                AssertTrue(cancelled); await monitor.CheckAsync();
            });
            Add("health_tick_runs_reconciliation_and_invariant_monitor", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                LoggingModule log = new LoggingModule(); log.Settings.EnableConsole = false;
                ArmadaSettings settings = new ArmadaSettings();
                StubGitService git = new StubGitService();
                DockService docks = new DockService(log, db, settings, git);
                CaptainService captains = new CaptainService(log, db, settings, git, docks);
                MissionService missions = new MissionService(log, db, settings, docks, captains);
                AdmiralService admiral = new AdmiralService(log, db, settings, captains, missions, new VoyageService(log, db), docks);
                Captain captain = await db.Captains.CreateAsync(new Captain("health tick") { MaxConcurrency = 2 });
                await ClaimAsync(db, captain, 2);
                await admiral.HealthCheckAsync();
                captain.MaxConcurrency = 1; await db.Captains.UpdateAsync(captain, updateConcurrency: true);
                await admiral.HealthCheckAsync();
                AssertEqual("limit_lowered", Cause((await Events(db)).Single()));
                AssertEqual(2, (await db.Captains.ReadAsync(captain.Id))!.ActiveMissionCount);
            });
            Add("removed_entities_do_not_keep_stale_baselines", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("recreated", "https://example.invalid/repo") { MaxParallelVoyages = 1 });
                for (int i = 0; i < 2; i++) await db.Missions.CreateAsync(new Mission("running") { VesselId = vessel.Id, Status = MissionStatusEnum.InProgress });
                ConcurrencyInvariantMonitor monitor = Monitor(db); await monitor.CheckAsync();
                foreach (Mission mission in await db.Missions.EnumerateByVesselAsync(vessel.Id)) await db.Missions.DeleteAsync(mission.Id);
                await db.Vessels.DeleteAsync(vessel.Id); await monitor.CheckAsync();
                int before = (await Events(db)).Count;
                await db.Vessels.CreateAsync(vessel);
                for (int i = 0; i < 2; i++) await db.Missions.CreateAsync(new Mission("running again") { VesselId = vessel.Id, Status = MissionStatusEnum.InProgress });
                await monitor.CheckAsync(); AssertEqual(before + 1, (await Events(db)).Count);
                AssertEqual("unknown", Cause((await Events(db))[0]));
            });
            return new TestSuiteDescriptor(suite, "Concurrency invariant monitor", cases);
        }

        private static ConcurrencyInvariantMonitor Monitor(DatabaseDriver db, ArmadaSettings? settings = null)
        {
            LoggingModule log = new LoggingModule(); log.Settings.EnableConsole = false;
            return new ConcurrencyInvariantMonitor(db, settings ?? new ArmadaSettings(), log);
        }
        private static Task<List<ArmadaEvent>> Events(DatabaseDriver db) => db.Events.EnumerateByTypeAsync("concurrency.limit_exceeded");
        private static string Cause(ArmadaEvent evt) => JsonDocument.Parse(evt.Payload!).RootElement.GetProperty("Cause").GetString()!;
        private static async Task<List<Mission>> ClaimAsync(DatabaseDriver db, Captain captain, int count)
        {
            List<Mission> result = new List<Mission>();
            for (int i = 0; i < count; i++)
            {
                Mission mission = await db.Missions.CreateAsync(new Mission("slot " + i));
                AssertTrue(await db.Captains.TryClaimAsync(captain.Id, mission.Id, null)); result.Add(mission);
            }
            return result;
        }
        private static async Task FinishAsync(DatabaseDriver db, Captain captain, Mission mission)
        {
            mission = (await db.Missions.ReadAsync(mission.Id))!;
            mission.Status = MissionStatusEnum.Complete; await db.Missions.UpdateAsync(mission);
            await db.Captains.TryReleaseFromMissionAsync(captain.Id, mission.Id);
        }
    }
}
