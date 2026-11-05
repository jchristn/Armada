namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
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
    /// Descriptors for sequential mission dispatch to the same captain. Validates the scenario
    /// where more missions than captains are dispatched and missions must be processed sequentially
    /// via HandleCompletionAsync re-dispatch, covering parallel-then-sequential capacity, dock reuse,
    /// completion-handler fault tolerance, event emission, orphan completion, and eligibility rules.
    /// </summary>
    public sealed class SequentialDispatchSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.SequentialDispatch";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Sequential Dispatch suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("admission_lock_isolation_cleanup", "Different vessels proceed independently; timeout and cancellation release registry entries", TestTags.Positive, async () =>
            {
                VesselAdmissionLockRegistry registry = new VesselAdmissionLockRegistry();
                using IDisposable first = (await registry.TryEnterAsync("first", TimeSpan.FromSeconds(1)))!;
                using (IDisposable? second = await registry.TryEnterAsync("second", TimeSpan.FromSeconds(1))) AssertNotNull(second);
                AssertEqual(1, registry.Count);
                AssertNull(await registry.TryEnterAsync("first", TimeSpan.Zero));
                using CancellationTokenSource cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                bool observed = false;
                try { await registry.TryEnterAsync("first", TimeSpan.FromSeconds(1), cancelled.Token); }
                catch (OperationCanceledException) { observed = true; }
                AssertTrue(observed);
                first.Dispose(); first.Dispose();
                AssertEqual(0, registry.Count, "no stale vessel lock entries after a timeout, cancellation or double disposal");
                await Task.WhenAll(Enumerable.Range(0, 200).Select(async i =>
                {
                    using IDisposable lease = (await registry.TryEnterAsync("vessel-" + i % 5, TimeSpan.FromSeconds(5)))!;
                    await Task.Yield();
                }));
                AssertEqual(0, registry.Count);
            }));

            cases.Add(CaseAsync("balanced_captain_slots", "Equivalent captains share load while explicit selection still wins", TestTags.Positive, async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                ServiceSet services = CreateServices(CreateLogging(), db, CreateSettings(), new StubGitService());
                TestFixture work = await SetupFixtureAsync(db, captainCount: 2, missionCount: 3);
                work.Vessel.MaxParallelMissionsPerVoyage = 4;
                foreach (Captain captain in work.Captains)
                { captain.MaxConcurrency = 3; await CaptainAssignmentFixture.UpdateAsync(db, captain, updateConcurrency: true); }
                AssertTrue(await services.Missions.TryAssignAsync(work.Missions[0], work.Vessel));
                AssertTrue(await services.Missions.TryAssignAsync(work.Missions[1], work.Vessel));
                List<Captain> busy = await db.Captains.EnumerateAsync();
                AssertTrue(busy.All(c => c.ActiveMissionCount == 1), "fill the idle peer before stacking work on one captain");
                work.Missions[2].RequestedCaptainId = busy[0].Id;
                await db.Missions.UpdateAsync(work.Missions[2]);
                AssertTrue(await services.Missions.TryAssignAsync(work.Missions[2], work.Vessel));
                AssertEqual(busy[0].Id, (await db.Missions.ReadAsync(work.Missions[2].Id))!.CaptainId);
            }));

            cases.Add(CaseAsync("background_launch_returns_after_admission", "A background launch returns once the mission is Assigned; a launch failure rolls back to Pending", TestTags.Positive, async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                DatabaseDriver db = fixture.Driver;
                ServiceSet services = CreateServices(CreateLogging(), db, CreateSettings(), new StubGitService());
                TestFixture work = await SetupFixtureAsync(db, captainCount: 1, missionCount: 2);
                TaskCompletionSource<int> release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                services.Captains.OnLaunchAgent = (_, _, _) => release.Task;
                AssertTrue(await services.Missions.TryAssignAsync(work.Missions[0], work.Vessel, true), "admitted");
                Mission admitted = (await db.Missions.ReadAsync(work.Missions[0].Id))!;
                AssertEqual(MissionStatusEnum.Assigned, admitted.Status, "returned before the launch finished");
                AssertEqual(work.Captains[0].Id, admitted.CaptainId);
                AssertFalse(await services.Missions.TryAssignAsync(work.Missions[0], work.Vessel, true), "an in-flight launch is not admitted twice");
                release.SetResult(4242);
                for (int i = 0; i < 100 && (await db.Missions.ReadAsync(admitted.Id))!.Status != MissionStatusEnum.InProgress; i++) await Task.Delay(20);
                Mission launched = (await db.Missions.ReadAsync(admitted.Id))!;
                AssertEqual(MissionStatusEnum.InProgress, launched.Status); AssertEqual(4242, launched.ProcessId);

                launched.Status = MissionStatusEnum.Complete; launched.ProcessId = null; await db.Missions.UpdateAsync(launched);
                AssertTrue(await db.Captains.TryReleaseFromMissionAsync(work.Captains[0].Id, launched.Id));
                services.Captains.OnLaunchAgent = (_, _, _) => throw new InvalidOperationException("launch failed");
                AssertTrue(await services.Missions.TryAssignAsync(work.Missions[1], work.Vessel, true), "admitted before the failing launch");
                for (int i = 0; i < 100 && (await db.Missions.ReadAsync(work.Missions[1].Id))!.Status != MissionStatusEnum.Pending; i++) await Task.Delay(20);
                AssertEqual(MissionStatusEnum.Pending, (await db.Missions.ReadAsync(work.Missions[1].Id))!.Status, "a failed background launch rolls back");
                AssertEqual(0, (await db.Captains.ReadAsync(work.Captains[0].Id))!.ActiveMissionCount, "and releases the captain");
            }));

            // Section 15 scheduler matrix rows not covered elsewhere.
            cases.Add(CaseAsync("matrix_vessel_one_by_one", "Vessel 1x1 runs a voyage of three one at a time and reports VoyageMissionLimit", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                DatabaseDriver db = testDb.Driver;
                ServiceSet services = CreateServices(CreateLogging(), db, CreateSettings(), new StubGitService());
                TestFixture work = await SetupFixtureAsync(db, captainCount: 3, missionCount: 3);
                work.Vessel.MaxParallelVoyages = 1; work.Vessel.MaxParallelMissionsPerVoyage = 1; await db.Vessels.UpdateAsync(work.Vessel);
                await Task.WhenAll(work.Missions.Select(m => Task.Run(() => services.Missions.TryAssignAsync(m, work.Vessel))));
                AssertEqual(1, (await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress)).Count, "one at a time");
                Mission waiting = (await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending)).First();
                AssertEqual(MissionAssignmentBlockerReasonEnum.VoyageMissionLimit, (await services.Missions.GetAssignmentBlockerAsync(waiting))!.Reason);
                Mission running = (await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress)).Single();
                await CompleteAsync(db, running);
                AssertTrue(await services.Missions.TryAssignAsync(waiting, work.Vessel), "the next mission takes the freed slot");
            }));

            cases.Add(CaseAsync("matrix_vessel_two_by_two_three_voyages", "Vessel 2x2 runs two voyages with at most two missions each; the third voyage waits and is admitted first-in-first-out", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                DatabaseDriver db = testDb.Driver;
                ServiceSet services = CreateServices(CreateLogging(), db, CreateSettings(), new StubGitService());
                TestFixture work = await SetupFixtureAsync(db, captainCount: 9, missionCount: 3);
                work.Vessel.MaxParallelVoyages = 2; work.Vessel.MaxParallelMissionsPerVoyage = 2; await db.Vessels.UpdateAsync(work.Vessel);
                List<Voyage> voyages = new List<Voyage> { work.Voyage };
                List<Mission> missions = new List<Mission>(work.Missions);
                for (int v = 1; v < 3; v++)
                {
                    Voyage voyage = await db.Voyages.CreateAsync(new Voyage("voyage-" + v) { Status = VoyageStatusEnum.InProgress, CreatedUtc = DateTime.UtcNow.AddSeconds(v) });
                    voyages.Add(voyage);
                    for (int i = 0; i < 3; i++) missions.Add(await db.Missions.CreateAsync(new Mission("v" + v + "-m" + i) { VesselId = work.Vessel.Id, VoyageId = voyage.Id }));
                }
                foreach (Mission mission in missions) await services.Missions.TryAssignAsync(mission, work.Vessel);
                List<Mission> running = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress);
                AssertEqual(4, running.Count, "two voyages, two missions each");
                AssertEqual(2, running.Select(m => m.VoyageId).Distinct().Count());
                AssertTrue(running.All(m => m.VoyageId != voyages[2].Id), "the third voyage waits");
                Mission third = missions.First(m => m.VoyageId == voyages[2].Id);
                AssertEqual(MissionAssignmentBlockerReasonEnum.VesselVoyageLimit, (await services.Missions.GetAssignmentBlockerAsync(third))!.Reason);
                foreach (Mission mission in missions.Where(m => m.VoyageId == voyages[0].Id))
                {
                    Mission current = (await db.Missions.ReadAsync(mission.Id))!;
                    if (current.Status == MissionStatusEnum.InProgress) await CompleteAsync(db, current);
                    else { current.Status = MissionStatusEnum.Cancelled; await db.Missions.UpdateAsync(current); }
                }
                voyages[0].Status = VoyageStatusEnum.Complete; await db.Voyages.UpdateAsync(voyages[0]);
                AssertTrue(await services.Missions.TryAssignAsync(third, work.Vessel), "the waiting voyage is admitted once a slot frees");
            }));

            cases.Add(CaseAsync("matrix_unlimited_vessel_uses_captain_and_global_caps", "Vessel 0x0 is limited only by captain and global caps", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                DatabaseDriver db = testDb.Driver;
                ArmadaSettings settings = CreateSettings(); settings.MaxConcurrentMissions = 3;
                ServiceSet services = CreateServices(CreateLogging(), db, settings, new StubGitService());
                TestFixture work = await SetupFixtureAsync(db, captainCount: 5, missionCount: 8);
                work.Vessel.MaxParallelVoyages = 0; work.Vessel.MaxParallelMissionsPerVoyage = 0; await db.Vessels.UpdateAsync(work.Vessel);
                foreach (Mission mission in work.Missions) await services.Missions.TryAssignAsync(mission, work.Vessel);
                AssertEqual(3, (await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress)).Count, "the global cap binds, not the vessel");
                Mission waiting = (await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending)).First();
                AssertEqual(MissionAssignmentBlockerReasonEnum.GlobalMissionLimit, (await services.Missions.GetAssignmentBlockerAsync(waiting))!.Reason);
            }));

            cases.Add(CaseAsync("matrix_captain_concurrency_two", "One captain with MaxConcurrency 2 on a 1x4 vessel runs two of four and reports CaptainConcurrencyLimit", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                DatabaseDriver db = testDb.Driver;
                ServiceSet services = CreateServices(CreateLogging(), db, CreateSettings(), new StubGitService());
                TestFixture work = await SetupFixtureAsync(db, captainCount: 1, missionCount: 4);
                work.Vessel.MaxParallelVoyages = 1; work.Vessel.MaxParallelMissionsPerVoyage = 4; await db.Vessels.UpdateAsync(work.Vessel);
                work.Captains[0].MaxConcurrency = 2; await CaptainAssignmentFixture.UpdateAsync(db, work.Captains[0], updateConcurrency: true);
                await Task.WhenAll(work.Missions.Select(m => Task.Run(() => services.Missions.TryAssignAsync(m, work.Vessel))));
                AssertEqual(2, (await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress)).Count);
                Mission waiting = (await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending)).First();
                AssertEqual(MissionAssignmentBlockerReasonEnum.CaptainConcurrencyLimit, (await services.Missions.GetAssignmentBlockerAsync(waiting))!.Reason);
            }));

            cases.Add(CaseAsync("matrix_endpoint_cap_shared_by_two_captains", "An endpoint cap of 3 shared by two captains at concurrency 4 admits at most 3 across both", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                DatabaseDriver db = testDb.Driver;
                ServiceSet services = CreateServices(CreateLogging(), db, CreateSettings(), new StubGitService());
                TestFixture work = await SetupFixtureAsync(db, captainCount: 2, missionCount: 8);
                work.Vessel.MaxParallelVoyages = 0; work.Vessel.MaxParallelMissionsPerVoyage = 0; await db.Vessels.UpdateAsync(work.Vessel);
                ModelEndpoint endpoint = await db.ModelEndpoints.CreateAsync(new ModelEndpoint { Name = "Shared", BaseUrl = "http://127.0.0.1:19898/v1", Kind = ModelEndpointKindEnum.Inference,
                    InferenceOptions = new InferenceOptions { MaxConcurrentMissions = 3 } });
                foreach (Captain captain in work.Captains)
                { captain.Runtime = AgentRuntimeEnum.ApiEndpoint; captain.ModelEndpointId = endpoint.Id; captain.MaxConcurrency = 4; await CaptainAssignmentFixture.UpdateAsync(db, captain, updateConcurrency: true); }
                await Task.WhenAll(work.Missions.Select(m => Task.Run(() => services.Missions.TryAssignAsync(m, work.Vessel))));
                AssertEqual(3, (await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress)).Count, "the endpoint cap binds across both captains");
                Mission waiting = (await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending)).First();
                AssertEqual(MissionAssignmentBlockerReasonEnum.EndpointMissionLimit, (await services.Missions.GetAssignmentBlockerAsync(waiting))!.Reason);
            }));

            cases.Add(CaseAsync("matrix_landing_failed_releases_voyage_slot", "A voyage in Review holds its slot; LandingFailed releases it", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                DatabaseDriver db = testDb.Driver;
                ServiceSet services = CreateServices(CreateLogging(), db, CreateSettings(), new StubGitService());
                TestFixture work = await SetupFixtureAsync(db, captainCount: 2, missionCount: 1);
                work.Vessel.MaxParallelVoyages = 1; work.Vessel.MaxParallelMissionsPerVoyage = 1; await db.Vessels.UpdateAsync(work.Vessel);
                Voyage other = await db.Voyages.CreateAsync(new Voyage("other") { Status = VoyageStatusEnum.InProgress });
                Mission waiting = await db.Missions.CreateAsync(new Mission("other work") { VesselId = work.Vessel.Id, VoyageId = other.Id });
                AssertTrue(await services.Missions.TryAssignAsync(work.Missions[0], work.Vessel));
                Mission held = (await db.Missions.ReadAsync(work.Missions[0].Id))!;
                held.Status = MissionStatusEnum.Review; held.ProcessId = null; await db.Missions.UpdateAsync(held);
                await db.Captains.TryReleaseFromMissionAsync(held.CaptainId!, held.Id);
                AssertFalse(await services.Missions.TryAssignAsync(waiting, work.Vessel), "Review holds the voyage slot");
                MissionAssignmentBlocker blocker = (await services.Missions.GetAssignmentBlockerAsync(waiting))!;
                AssertEqual(MissionAssignmentBlockerReasonEnum.VesselVoyageLimit, blocker.Reason);
                held.Status = MissionStatusEnum.LandingFailed; await db.Missions.UpdateAsync(held);
                AssertTrue(await services.Missions.TryAssignAsync(waiting, work.Vessel), "LandingFailed releases the slot");
            }));

            cases.Add(CaseAsync("matrix_quarantined_captain_keeps_running_work", "A quarantined captain's two running missions continue and it takes no new work", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                DatabaseDriver db = testDb.Driver;
                ServiceSet services = CreateServices(CreateLogging(), db, CreateSettings(), new StubGitService());
                TestFixture work = await SetupFixtureAsync(db, captainCount: 1, missionCount: 3);
                work.Vessel.MaxParallelVoyages = 0; work.Vessel.MaxParallelMissionsPerVoyage = 0; await db.Vessels.UpdateAsync(work.Vessel);
                work.Captains[0].MaxConcurrency = 3; await CaptainAssignmentFixture.UpdateAsync(db, work.Captains[0], updateConcurrency: true);
                AssertTrue(await services.Missions.TryAssignAsync(work.Missions[0], work.Vessel));
                AssertTrue(await services.Missions.TryAssignAsync(work.Missions[1], work.Vessel));
                Captain captain = (await db.Captains.ReadAsync(work.Captains[0].Id))!;
                captain.QuarantineUntilUtc = DateTime.UtcNow.AddHours(1); captain.QuarantineReason = "test"; await CaptainAssignmentFixture.UpdateAsync(db, captain);
                AssertFalse(await services.Missions.TryAssignAsync(work.Missions[2], work.Vessel), "no new admission while quarantined");
                List<Mission> running = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress);
                AssertEqual(2, running.Count, "running work is not preempted");
                AssertEqual(2, (await db.Captains.ReadAsync(captain.Id))!.ActiveMissionCount);
            }));

            cases.Add(CaseAsync("race_claim_without_status_update_is_reconciled", "A claim that landed without its mission update (Admiral killed mid-assignment) is reconciled and the mission is assignable", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                DatabaseDriver db = testDb.Driver;
                ServiceSet services = CreateServices(CreateLogging(), db, CreateSettings(), new StubGitService());
                TestFixture work = await SetupFixtureAsync(db, captainCount: 1, missionCount: 1);
                // The claim commits the slot and Assigned together; the crash lands before any dock or process exists.
                AssertTrue(await db.Captains.TryClaimAsync(work.Captains[0].Id, work.Missions[0].Id, null), "the slot claim succeeded");
                Mission orphan = (await db.Missions.ReadAsync(work.Missions[0].Id))!;
                AssertNull(orphan.ProcessId); AssertNull(orphan.DockId);
                AdmiralService restarted = new AdmiralService(CreateLogging(), db, CreateSettings(), services.Captains, services.Missions, new VoyageService(CreateLogging(), db), services.Docks);
                restarted.OnIsProcessTracked = _ => false;
                await restarted.CleanupStaleCaptainsAsync();
                await db.Captains.ReconcileMissionSlotsAsync();
                Mission recovered = (await db.Missions.ReadAsync(orphan.Id))!;
                Captain captain = (await db.Captains.ReadAsync(work.Captains[0].Id))!;
                AssertTrue(recovered.Status == MissionStatusEnum.Pending || recovered.Status == MissionStatusEnum.InProgress,
                    "restart recovery returns the orphan to Pending or relaunches it, never leaves it Assigned without a process: " + recovered.Status);
                if (recovered.Status == MissionStatusEnum.Pending)
                {
                    AssertEqual(0, captain.ActiveMissionCount, "the orphan's slot is released");
                    AssertTrue(await services.Missions.TryAssignAsync(recovered, work.Vessel), "the mission is assignable again");
                }
                else AssertEqual(1, captain.ActiveMissionCount, "a relaunched orphan holds exactly one slot");
            }));

            cases.Add(CaseAsync("race_admission_lock_timeout_counts_and_returns", "A slow admission holding the vessel lock times out the next dispatch, which returns and counts the timeout", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                DatabaseDriver db = testDb.Driver;
                ArmadaSettings settings = CreateSettings(); settings.Concurrency.AdmissionLockTimeoutSeconds = 1;
                ServiceSet services = CreateServices(CreateLogging(), db, settings, new StubGitService());
                TestFixture work = await SetupFixtureAsync(db, captainCount: 2, missionCount: 2);
                work.Vessel.MaxParallelVoyages = 0; work.Vessel.MaxParallelMissionsPerVoyage = 0; await db.Vessels.UpdateAsync(work.Vessel);
                long timeouts = 0;
                using System.Diagnostics.Metrics.MeterListener listener = new System.Diagnostics.Metrics.MeterListener();
                listener.InstrumentPublished = (instrument, l) => { if (instrument.Name == "armada.admission.lock_timeouts") l.EnableMeasurementEvents(instrument); };
                listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => Interlocked.Add(ref timeouts, value));
                listener.Start();
                TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource<bool> entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                // The launch-policy hook runs inside admission, so blocking it holds the vessel and capacity locks like a slow database.
                services.Missions.CanAssignMissionAsync = async (_, _) => { entered.TrySetResult(true); await release.Task; return true; };
                Task<bool> slow = Task.Run(() => services.Missions.TryAssignAsync(work.Missions[0], work.Vessel));
                await entered.Task;
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                AssertFalse(await services.Missions.TryAssignAsync(work.Missions[1], work.Vessel), "the contended dispatch gives up");
                AssertTrue(watch.Elapsed < TimeSpan.FromSeconds(10), "it returns after the lock timeout, not after the slow admission");
                listener.RecordObservableInstruments();
                AssertTrue(Interlocked.Read(ref timeouts) >= 1, "the timeout is counted");
                release.SetResult(true);
                AssertTrue(await slow, "the slow admission still completes");
                services.Missions.CanAssignMissionAsync = null;
                AssertTrue(await services.Missions.TryAssignAsync(work.Missions[1], work.Vessel), "the timed-out mission is assigned on the next pass");
            }));

            cases.Add(CaseAsync("two_hundred_missions_mixed_limits", "200 missions across five vessels and ten captains make progress without exceeding mixed limits", TestTags.Positive, async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                ArmadaSettings settings = CreateSettings();
                settings.MaxConcurrentMissions = 7;
                ServiceSet services = CreateServices(CreateLogging(), db, settings, new StubGitService());
                TestFixture work = await SetupFixtureAsync(db, captainCount: 10, missionCount: 200);
                int nextPid = 2000;
                services.Captains.OnLaunchAgent = (_, _, _) => Task.FromResult(Interlocked.Increment(ref nextPid));
                List<Vessel> vessels = new List<Vessel> { work.Vessel };
                for (int i = 1; i < 5; i++) vessels.Add(await db.Vessels.CreateAsync(new Vessel("vessel-" + i, "https://github.com/test/repo")));
                foreach (Vessel vessel in vessels)
                { vessel.MaxParallelVoyages = 2; vessel.MaxParallelMissionsPerVoyage = 2; await db.Vessels.UpdateAsync(vessel); }
                List<Voyage> voyages = new List<Voyage>();
                for (int i = 0; i < 10; i++) voyages.Add(await db.Voyages.CreateAsync(new Voyage("voyage-" + i) { Status = VoyageStatusEnum.InProgress }));
                for (int i = 0; i < work.Captains.Count; i++)
                { work.Captains[i].MaxConcurrency = 2 + i % 2; await CaptainAssignmentFixture.UpdateAsync(db, work.Captains[i], updateConcurrency: true); }
                for (int i = 0; i < work.Missions.Count; i++)
                { work.Missions[i].VesselId = vessels[i % 5].Id; work.Missions[i].VoyageId = voyages[i % 10].Id; await db.Missions.UpdateAsync(work.Missions[i]); }
                ConcurrencyInvariantMonitor monitor = new ConcurrencyInvariantMonitor(db, settings, CreateLogging());
                int completed = 0;
                // Eight concurrent dispatcher requests plus the scheduler's next request per wave.
                for (int wave = 0; completed < 200 && wave < 200; wave++)
                {
                    List<Mission> pending = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending);
                    await Task.WhenAll(pending.Take(9).Select(m => Task.Run(() => services.Missions.TryAssignAsync(m, vessels.First(v => v.Id == m.VesselId)))));
                    List<Mission> running = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress);
                    AssertTrue(running.Count > 0, "open capacity must make progress");
                    AssertTrue(running.Count <= 7, "global cap");
                    foreach (IGrouping<string?, Mission> group in running.GroupBy(m => m.VoyageId)) AssertTrue(group.Count() <= 2, "voyage mission cap");
                    foreach (Captain captain in await db.Captains.EnumerateAsync()) AssertTrue(captain.ActiveMissionCount <= captain.MaxConcurrency, "captain cap");
                    await monitor.CheckAsync();
                    AssertEqual(0, (await db.Events.EnumerateByTypeAsync("concurrency.limit_exceeded")).Count, "healthy mixed-limit waves have no invariant violations");
                    foreach (Mission mission in running)
                    {
                        mission.Status = MissionStatusEnum.Complete;
                        mission.ProcessId = null;
                        await db.Missions.UpdateAsync(mission);
                        AssertTrue(await db.Captains.TryReleaseFromMissionAsync(mission.CaptainId!, mission.Id));
                        completed++;
                    }
                }
                AssertEqual(200, completed);
                AssertTrue((await db.Captains.EnumerateAsync()).All(c => c.ActiveMissionCount == 0));
            }));

            cases.Add(CaseAsync("captain_slots_complete_fail_recover", "One captain isolates completion, failure and recovery of its three missions", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                DatabaseDriver db = testDb.Driver;
                ArmadaSettings settings = CreateSettings();
                settings.MaxRecoveryAttempts = 2;
                ServiceSet services = CreateServices(CreateLogging(), db, settings, new StubGitService());
                TestFixture fixture = await SetupFixtureAsync(db, captainCount: 1, missionCount: 3);
                Captain captain = fixture.Captains[0];
                captain.MaxConcurrency = 3;
                await CaptainAssignmentFixture.UpdateAsync(db, captain, updateConcurrency: true);
                foreach (Mission mission in fixture.Missions) AssertTrue(await services.Missions.TryAssignAsync(mission, fixture.Vessel));
                List<Mission> running = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress);
                AssertEqual(3, running.Count);
                AssertEqual(3, (await db.Captains.ReadAsync(captain.Id))!.ActiveMissionCount);
                // Pass the aggregate record intentionally: completion must scope itself to the requested mission.
                await services.Missions.HandleCompletionAsync((await db.Captains.ReadAsync(captain.Id))!, running[1].Id);
                Captain after = (await db.Captains.ReadAsync(captain.Id))!;
                AssertEqual(2, after.ActiveMissionCount);
                AssertEqual(CaptainStateEnum.Working, after.State);
                AssertEqual(MissionStatusEnum.WorkProduced, (await db.Missions.ReadAsync(running[1].Id))!.Status);
                AssertEqual(running[0].ProcessId, (await db.Missions.ReadAsync(running[0].Id))!.ProcessId);
                AssertEqual(running[2].ProcessId, (await db.Missions.ReadAsync(running[2].Id))!.ProcessId);
                await services.Missions.HandleCompletionAsync(after, running[1].Id);
                AssertEqual(2, (await db.Captains.ReadAsync(captain.Id))!.ActiveMissionCount, "duplicate completion does not release a sibling");
                // Recover a non-primary assignment; its dock and PID belong to that mission only.
                Mission recovery = (await db.Missions.ReadAsync(running[2].Id))!;
                Dock dock = (await db.Docks.ReadAsync(recovery.DockId!))!;
                Directory.CreateDirectory(dock.WorktreePath);
                int? siblingPid = (await db.Missions.ReadAsync(running[0].Id))!.ProcessId;
                await services.Captains.TryRecoverAsync(after.ForMission(recovery));
                recovery = (await db.Missions.ReadAsync(recovery.Id))!;
                AssertTrue(recovery.ProcessId != running[2].ProcessId, "recovered mission receives its own new PID");
                AssertEqual(1, recovery.StallRecoveryAttempts);
                AssertEqual(siblingPid, (await db.Missions.ReadAsync(running[0].Id))!.ProcessId);
                AssertEqual(2, (await db.Captains.ReadAsync(captain.Id))!.ActiveMissionCount);
                // A new service instance must recognize every durable slot when checking startup liveness.
                AdmiralService admiral = new AdmiralService(CreateLogging(), db, settings, services.Captains, services.Missions,
                    new VoyageService(CreateLogging(), db), services.Docks);
                List<int> checkedPids = new List<int>();
                admiral.OnIsProcessTracked = pid => { checkedPids.Add(pid); return true; };
                await admiral.CleanupStaleCaptainsAsync();
                AssertTrue(checkedPids.Contains(siblingPid!.Value));
                AssertTrue(checkedPids.Contains(recovery.ProcessId!.Value));
                AssertEqual(2, (await db.Captains.ReadAsync(captain.Id))!.ActiveMissionCount);
            }));

            cases.Add(CaseAsync("concurrent_dispatch_respects_voyage_limit", "Concurrent dispatch admits exactly the configured number then reuses slots", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ServiceSet services = CreateServices(CreateLogging(), db, CreateSettings(), new StubGitService());
                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 8, missionCount: 50);
                    fixture.Vessel.MaxParallelVoyages = 1;
                    fixture.Vessel.MaxParallelMissionsPerVoyage = 3;
                    await db.Vessels.UpdateAsync(fixture.Vessel);
                    TaskCompletionSource<bool> launchesReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    TaskCompletionSource<bool> releaseLaunches = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    int launchCount = 0;
                    services.Captains.OnLaunchAgent = async (_, _, _) =>
                    {
                        int pid = Interlocked.Increment(ref launchCount);
                        if (pid == 3) launchesReady.TrySetResult(true);
                        await releaseLaunches.Task;
                        return 1000 + pid;
                    };
                    Task<bool>[] assignments = fixture.Missions.Select(m => Task.Run(() => services.Missions.TryAssignAsync(m, fixture.Vessel))).ToArray();
                    try { await launchesReady.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                    finally { releaseLaunches.TrySetResult(true); }
                    await Task.WhenAll(assignments);
                    List<Mission> running = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress);
                    AssertEqual(3, running.Count, "50 simultaneous assignment requests must admit exactly three");
                    Mission waiting = (await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending)).First();
                    MissionAssignmentBlocker? blocker = await services.Missions.GetAssignmentBlockerAsync(waiting);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.VoyageMissionLimit, blocker!.Reason);
                    AssertEqual(3, blocker.BlockingMissionCount);
                    Captain captain = (await db.Captains.ReadAsync(running[0].CaptainId!))!;
                    await services.Missions.HandleCompletionAsync(captain, running[0].Id);
                    running = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress);
                    AssertEqual(3, running.Count, "Completion dispatch must fill the freed slot");
                    AssertEqual(1, (await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.WorkProduced)).Count);
                }
            }));

            cases.Add(CaseAsync("global_admission_across_vessels", "Global mission admission stays bounded across simultaneous assignments to different vessels", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxConcurrentMissions = 3;
                    ServiceSet services = CreateServices(CreateLogging(), db, settings, new StubGitService());
                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 8, missionCount: 20);
                    fixture.Vessel.MaxParallelVoyages = 0;
                    fixture.Vessel.MaxParallelMissionsPerVoyage = 0;
                    await db.Vessels.UpdateAsync(fixture.Vessel);
                    Vessel second = new Vessel("Second vessel", "https://github.com/test/second") { MaxParallelVoyages = 0, MaxParallelMissionsPerVoyage = 0 };
                    await db.Vessels.CreateAsync(second);
                    for (int i = 1; i < fixture.Missions.Count; i += 2)
                    {
                        fixture.Missions[i].VesselId = second.Id;
                        fixture.Missions[i].VoyageId = null;
                        await db.Missions.UpdateAsync(fixture.Missions[i]);
                    }
                    await Task.WhenAll(fixture.Missions.Select(m => Task.Run(() => services.Missions.TryAssignAsync(m, m.VesselId == second.Id ? second : fixture.Vessel))));
                    AssertEqual(3, (await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress)).Count);
                    Mission waiting = (await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending)).First();
                    AssertEqual(MissionAssignmentBlockerReasonEnum.GlobalMissionLimit, (await services.Missions.GetAssignmentBlockerAsync(waiting))!.Reason);
                }
            }));

            cases.Add(CaseAsync("sticky_voyage_admission", "Voyage admission survives review, releases at terminal status, and standalone missions count separately", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ServiceSet services = CreateServices(CreateLogging(), db, CreateSettings(), new StubGitService());
                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 3, missionCount: 2);
                    fixture.Vessel.MaxParallelVoyages = 1;
                    fixture.Vessel.MaxParallelMissionsPerVoyage = 2;
                    await db.Vessels.UpdateAsync(fixture.Vessel);
                    Mission standalone = new Mission("Separate work") { VesselId = fixture.Vessel.Id };
                    await db.Missions.CreateAsync(standalone);
                    AssertTrue(await services.Missions.TryAssignAsync(fixture.Missions[0], fixture.Vessel));
                    Mission first = (await db.Missions.ReadAsync(fixture.Missions[0].Id))!;
                    first.Status = MissionStatusEnum.Review;
                    await db.Missions.UpdateAsync(first);
                    await db.Captains.TryReleaseFromMissionAsync(first.CaptainId!, first.Id);
                    AssertFalse(await services.Missions.TryAssignAsync(standalone, fixture.Vessel), "Review must keep the voyage slot");
                    AssertEqual(MissionAssignmentBlockerReasonEnum.VesselVoyageLimit, (await services.Missions.GetAssignmentBlockerAsync(standalone))!.Reason);
                    AssertTrue(await services.Missions.TryAssignAsync(fixture.Missions[1], fixture.Vessel), "An admitted voyage can fill its mission slots");
                    fixture.Voyage.Status = VoyageStatusEnum.Complete;
                    await db.Voyages.UpdateAsync(fixture.Voyage);
                    AssertTrue(await services.Missions.TryAssignAsync(standalone, fixture.Vessel), "Terminal voyage releases admission");
                }
            }));


            cases.Add(CaseAsync("one_captain_three_missions_sequential_dispatch", "OneCaptain_ThreeMissions_SequentialDispatch", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    ServiceSet services = CreateServices(logging, db, settings, git);
                    MissionService missionService = services.Missions;

                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 1, missionCount: 3);
                    Captain captain = fixture.Captains[0];
                    Vessel vessel = fixture.Vessel;
                    List<Mission> missions = fixture.Missions;

                    // Step 1: Assign mission 1 — captain is idle, should get assigned
                    await missionService.TryAssignAsync(missions[0], vessel);

                    Mission? m1 = await db.Missions.ReadAsync(missions[0].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m1!.Status, "Mission 1 should be InProgress");
                    AssertEqual(captain.Id, m1.CaptainId, "Mission 1 should be assigned to the captain");

                    Captain? captainAfterM1 = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Working, captainAfterM1!.State, "Captain should be Working");
                    AssertEqual(m1.Id, captainAfterM1.ActiveMissionIds.SingleOrDefault(), "Captain should track mission 1");

                    // Step 2: Try to assign mission 2 — captain is working, should NOT be assigned
                    await missionService.TryAssignAsync(missions[1], vessel);

                    Mission? m2 = await db.Missions.ReadAsync(missions[1].Id);
                    AssertEqual(MissionStatusEnum.Pending, m2!.Status, "Mission 2 should still be Pending");

                    // Step 3: Try to assign mission 3 — same, should NOT be assigned
                    await missionService.TryAssignAsync(missions[2], vessel);

                    Mission? m3 = await db.Missions.ReadAsync(missions[2].Id);
                    AssertEqual(MissionStatusEnum.Pending, m3!.Status, "Mission 3 should still be Pending");

                    // Step 4: Complete mission 1 — should release captain and pick up mission 2
                    Captain? captainForCompletion = await db.Captains.ReadAsync(captain.Id);
                    await missionService.HandleCompletionAsync(captainForCompletion!, captainForCompletion!.ActiveMissionIds.SingleOrDefault()!);

                    m1 = await db.Missions.ReadAsync(missions[0].Id);
                    AssertEqual(MissionStatusEnum.WorkProduced, m1!.Status, "Mission 1 should be WorkProduced (landing happens in ArmadaServer)");

                    m2 = await db.Missions.ReadAsync(missions[1].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m2!.Status, "Mission 2 should be InProgress after mission 1 completion");
                    AssertEqual(captain.Id, m2.CaptainId, "Mission 2 should be assigned to the same captain");

                    Captain? captainAfterM2Assign = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Working, captainAfterM2Assign!.State, "Captain should be Working with mission 2");

                    // Step 5: Complete mission 2 — should pick up mission 3
                    captainForCompletion = await db.Captains.ReadAsync(captain.Id);
                    await missionService.HandleCompletionAsync(captainForCompletion!, m2.Id);

                    m2 = await db.Missions.ReadAsync(missions[1].Id);
                    AssertEqual(MissionStatusEnum.WorkProduced, m2!.Status, "Mission 2 should be WorkProduced (landing happens in ArmadaServer)");

                    m3 = await db.Missions.ReadAsync(missions[2].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m3!.Status, "Mission 3 should be InProgress after mission 2 completion");
                    AssertEqual(captain.Id, m3.CaptainId, "Mission 3 should be assigned to the same captain");

                    // Step 6: Complete mission 3 — captain should be released to Idle
                    captainForCompletion = await db.Captains.ReadAsync(captain.Id);
                    await missionService.HandleCompletionAsync(captainForCompletion!, m3.Id);

                    m3 = await db.Missions.ReadAsync(missions[2].Id);
                    AssertEqual(MissionStatusEnum.WorkProduced, m3!.Status, "Mission 3 should be WorkProduced (landing happens in ArmadaServer)");

                    Captain? captainFinal = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Idle, captainFinal!.State, "Captain should be Idle after all missions complete");
                    AssertNull(captainFinal.ActiveMissionIds.SingleOrDefault(), "Captain CurrentMissionId should be null");
                    AssertNull(captainFinal.Assignment?.DockId, "Captain CurrentDockId should be null");

                    // Verify all 3 missions reached WorkProduced (landing to Complete happens in ArmadaServer)
                    List<Mission> allMissions = await db.Missions.EnumerateByVoyageAsync(fixture.Voyage.Id);
                    int workProducedCount = allMissions.Count(m => m.Status == MissionStatusEnum.WorkProduced);
                    AssertEqual(3, workProducedCount, "All 3 missions should be WorkProduced");
                }
            }));

            cases.Add(CaseAsync("two_captains_five_missions_parallel_then_sequential", "TwoCaptains_FiveMissions_ParallelThenSequential", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    ServiceSet services = CreateServices(logging, db, settings, git);
                    MissionService missionService = services.Missions;

                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 2, missionCount: 5);
                    List<Captain> captains = fixture.Captains;
                    Vessel vessel = fixture.Vessel;
                    vessel.MaxParallelVoyages = 0;
                    vessel.MaxParallelMissionsPerVoyage = 0;
                    await db.Vessels.UpdateAsync(vessel);
                    List<Mission> missions = fixture.Missions;

                    // Assign first 2 missions — each captain should get one
                    await missionService.TryAssignAsync(missions[0], vessel);
                    await missionService.TryAssignAsync(missions[1], vessel);

                    Mission? m1 = await db.Missions.ReadAsync(missions[0].Id);
                    Mission? m2 = await db.Missions.ReadAsync(missions[1].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m1!.Status, "Mission 1 should be InProgress");
                    AssertEqual(MissionStatusEnum.InProgress, m2!.Status, "Mission 2 should be InProgress");

                    string? m1CaptainId = m1.CaptainId;
                    string? m2CaptainId = m2.CaptainId;
                    AssertNotNull(m1CaptainId, "Mission 1 should have a captain");
                    AssertNotNull(m2CaptainId, "Mission 2 should have a captain");
                    AssertNotEqual(m1CaptainId!, m2CaptainId!, "Missions should be on different captains");

                    // Missions 3, 4, 5 should remain Pending (no capacity)
                    await missionService.TryAssignAsync(missions[2], vessel);
                    await missionService.TryAssignAsync(missions[3], vessel);
                    await missionService.TryAssignAsync(missions[4], vessel);

                    Mission? m3 = await db.Missions.ReadAsync(missions[2].Id);
                    Mission? m4 = await db.Missions.ReadAsync(missions[3].Id);
                    Mission? m5 = await db.Missions.ReadAsync(missions[4].Id);
                    AssertEqual(MissionStatusEnum.Pending, m3!.Status, "Mission 3 should be Pending");
                    AssertEqual(MissionStatusEnum.Pending, m4!.Status, "Mission 4 should be Pending");
                    AssertEqual(MissionStatusEnum.Pending, m5!.Status, "Mission 5 should be Pending");

                    // Complete mission 1 — the captain should pick up mission 3
                    Captain? captain1 = await db.Captains.ReadAsync(m1CaptainId!);
                    await missionService.HandleCompletionAsync(captain1!, m1.Id);

                    m1 = await db.Missions.ReadAsync(missions[0].Id);
                    AssertEqual(MissionStatusEnum.WorkProduced, m1!.Status, "Mission 1 should be WorkProduced");

                    m3 = await db.Missions.ReadAsync(missions[2].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m3!.Status, "Mission 3 should be InProgress after mission 1 completion");

                    // Complete mission 2 — the other captain should pick up mission 4
                    Captain? captain2 = await db.Captains.ReadAsync(m2CaptainId!);
                    await missionService.HandleCompletionAsync(captain2!, m2.Id);

                    m2 = await db.Missions.ReadAsync(missions[1].Id);
                    AssertEqual(MissionStatusEnum.WorkProduced, m2!.Status, "Mission 2 should be WorkProduced");

                    m4 = await db.Missions.ReadAsync(missions[3].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m4!.Status, "Mission 4 should be InProgress after mission 2 completion");

                    // Complete mission 3 — should pick up mission 5
                    captain1 = await db.Captains.ReadAsync(m3.CaptainId!);
                    await missionService.HandleCompletionAsync(captain1!, m3.Id);

                    m3 = await db.Missions.ReadAsync(missions[2].Id);
                    AssertEqual(MissionStatusEnum.WorkProduced, m3!.Status, "Mission 3 should be WorkProduced");

                    m5 = await db.Missions.ReadAsync(missions[4].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m5!.Status, "Mission 5 should be InProgress after mission 3 completion");

                    // Complete mission 4 — no more pending, captain should go Idle
                    captain2 = await db.Captains.ReadAsync(m4.CaptainId!);
                    await missionService.HandleCompletionAsync(captain2!, m4.Id);

                    m4 = await db.Missions.ReadAsync(missions[3].Id);
                    AssertEqual(MissionStatusEnum.WorkProduced, m4!.Status, "Mission 4 should be WorkProduced");

                    captain2 = await db.Captains.ReadAsync(captain2!.Id);
                    AssertEqual(CaptainStateEnum.Idle, captain2!.State, "Captain 2 should be Idle");

                    // Complete mission 5 — last captain should go Idle
                    captain1 = await db.Captains.ReadAsync(m5.CaptainId!);
                    await missionService.HandleCompletionAsync(captain1!, m5.Id);

                    m5 = await db.Missions.ReadAsync(missions[4].Id);
                    AssertEqual(MissionStatusEnum.WorkProduced, m5!.Status, "Mission 5 should be WorkProduced");

                    // Verify all 5 missions reached WorkProduced (landing to Complete happens in ArmadaServer)
                    List<Mission> allMissions = await db.Missions.EnumerateByVoyageAsync(fixture.Voyage.Id);
                    int workProducedCount = allMissions.Count(m => m.Status == MissionStatusEnum.WorkProduced);
                    AssertEqual(5, workProducedCount, "All 5 missions should be WorkProduced");

                    // Verify both captains are Idle
                    foreach (Captain c in captains)
                    {
                        Captain? finalCaptain = await db.Captains.ReadAsync(c.Id);
                        AssertEqual(CaptainStateEnum.Idle, finalCaptain!.State, "Captain " + c.Name + " should be Idle");
                    }
                }
            }));

            cases.Add(CaseAsync("dock_cleanup_worktree_path_reusable_between_assignments", "DockCleanup_WorktreePathReusable_BetweenAssignments", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    ServiceSet services = CreateServices(logging, db, settings, git);
                    MissionService missionService = services.Missions;

                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 1, missionCount: 2);
                    Captain captain = fixture.Captains[0];
                    Vessel vessel = fixture.Vessel;

                    // Assign mission 1
                    await missionService.TryAssignAsync(fixture.Missions[0], vessel);

                    Mission? m1 = await db.Missions.ReadAsync(fixture.Missions[0].Id);
                    AssertNotNull(m1!.DockId, "Mission 1 should have a dock");

                    Dock? dock1 = await db.Docks.ReadAsync(m1.DockId!);
                    AssertNotNull(dock1, "Dock for mission 1 should exist");
                    string? worktreePath1 = dock1!.WorktreePath;
                    AssertNotNull(worktreePath1, "Dock should have a worktree path");

                    // Complete mission 1 — should auto-assign mission 2
                    Captain? captainForCompletion = await db.Captains.ReadAsync(captain.Id);
                    await missionService.HandleCompletionAsync(captainForCompletion!, m1.Id);

                    // Mission 2 should now be assigned with its own dock
                    Mission? m2 = await db.Missions.ReadAsync(fixture.Missions[1].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m2!.Status, "Mission 2 should be InProgress");
                    AssertNotNull(m2.DockId, "Mission 2 should have a dock");

                    Dock? dock2 = await db.Docks.ReadAsync(m2.DockId!);
                    AssertNotNull(dock2, "Dock for mission 2 should exist");

                    // Dock paths are mission-scoped to eliminate reuse races between sequential assignments.
                    AssertNotEqual(worktreePath1!, dock2!.WorktreePath!, "Worktree path should be unique per mission");

                    // Verify git worktree creation was called for both missions
                    AssertTrue(git.WorktreeCalls.Count >= 2, "Should have at least 2 worktree create calls");
                }
            }));

            cases.Add(CaseAsync("completion_handler_throws_captain_still_released_redispatch_attempted", "CompletionHandler_Throws_CaptainStillReleased_RedispatchAttempted", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    ServiceSet services = CreateServices(logging, db, settings, git);
                    MissionService missionService = services.Missions;

                    // Set OnMissionComplete handler that throws
                    missionService.OnMissionComplete = (mission, dock) =>
                    {
                        throw new InvalidOperationException("Simulated push failure");
                    };

                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 1, missionCount: 2);
                    Captain captain = fixture.Captains[0];
                    Vessel vessel = fixture.Vessel;

                    // Assign and start mission 1
                    await missionService.TryAssignAsync(fixture.Missions[0], vessel);

                    Mission? m1 = await db.Missions.ReadAsync(fixture.Missions[0].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m1!.Status, "Mission 1 should be InProgress");

                    // Complete mission 1 — OnMissionComplete will throw (fire-and-forget),
                    // but captain should still be released and mission 2 should be picked up
                    Captain? captainForCompletion = await db.Captains.ReadAsync(captain.Id);
                    await missionService.HandleCompletionAsync(captainForCompletion!, m1.Id);

                    // A thrown landing handoff is persisted as LandingFailed with its reason, rather than stranding the
                    // verified work in the idempotently skipped WorkProduced state.
                    m1 = await db.Missions.ReadAsync(fixture.Missions[0].Id);
                    AssertEqual(MissionStatusEnum.LandingFailed, m1!.Status, "Mission 1 should be LandingFailed when the landing handler threw");
                    AssertContains("Simulated push failure", m1.FailureReason ?? "", "the handler failure is recorded as the reason");

                    // Verify mission 2 was picked up (re-dispatch was attempted)
                    Mission? m2 = await db.Missions.ReadAsync(fixture.Missions[1].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m2!.Status, "Mission 2 should be InProgress — handler failure should not block re-dispatch");
                    AssertEqual(captain.Id, m2.CaptainId, "Mission 2 should be assigned to the same captain");
                }
            }));

            cases.Add(CaseAsync("handle_completion_async_emits_mission_completed_event", "HandleCompletionAsync_EmitsMissionCompletedEvent", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    ServiceSet services = CreateServices(logging, db, settings, git);
                    MissionService missionService = services.Missions;

                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 1, missionCount: 1);
                    Captain captain = fixture.Captains[0];
                    Vessel vessel = fixture.Vessel;

                    // Assign and start mission
                    await missionService.TryAssignAsync(fixture.Missions[0], vessel);

                    // Complete mission
                    Captain? captainForCompletion = await db.Captains.ReadAsync(captain.Id);
                    await missionService.HandleCompletionAsync(captainForCompletion!, fixture.Missions[0].Id);

                    // Verify mission.work_produced event was emitted (mission.completed happens in ArmadaServer after landing)
                    EnumerationResult<ArmadaEvent> eventResult = await db.Events.EnumerateAsync(new EnumerationQuery());
                    List<ArmadaEvent> workProducedEvents = eventResult.Objects.Where(e => e.EventType == "mission.work_produced").ToList();
                    AssertTrue(workProducedEvents.Count >= 1, "Should have at least 1 mission.work_produced event");
                    AssertEqual(fixture.Missions[0].Id, workProducedEvents[0].MissionId, "Event should reference the work_produced mission");
                    AssertEqual(captain.Id, workProducedEvents[0].CaptainId, "Event should reference the captain");
                }
            }));

            cases.Add(CaseAsync("orphan_completion_does_not_release_captain_working_on_new_mission", "OrphanCompletion_DoesNotReleaseCaptainWorkingOnNewMission", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    ServiceSet services = CreateServices(logging, db, settings, git);
                    MissionService missionService = services.Missions;

                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 1, missionCount: 2);
                    Captain captain = fixture.Captains[0];
                    Vessel vessel = fixture.Vessel;

                    await missionService.TryAssignAsync(fixture.Missions[0], vessel);

                    Mission? oldMission = await db.Missions.ReadAsync(fixture.Missions[0].Id);
                    AssertEqual(MissionStatusEnum.InProgress, oldMission!.Status, "Mission 1 should be InProgress");

                    // Simulate the captain having already moved on to mission 2 before orphan recovery finalizes mission 1.
                    AssertTrue(await db.Captains.TryReleaseFromMissionAsync(captain.Id, oldMission.Id));
                    AssertTrue(await missionService.TryAssignAsync(fixture.Missions[1], vessel));
                    Mission? newMission = await db.Missions.ReadAsync(fixture.Missions[1].Id);

                    Captain? movedCaptain = await db.Captains.ReadAsync(captain.Id);
                    movedCaptain!.Assignment = newMission;
                    movedCaptain.Assignment ??= new Mission("fixture assignment"); movedCaptain.Assignment.DockId = newMission!.DockId;
                    movedCaptain.ProcessId = newMission.ProcessId;
                    movedCaptain.State = CaptainStateEnum.Working;
                    await CaptainAssignmentFixture.UpdateAsync(db, movedCaptain);

                    await missionService.HandleCompletionAsync(movedCaptain, fixture.Missions[0].Id);

                    Captain? finalCaptain = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Working, finalCaptain!.State, "Captain should remain Working on the newer mission");
                    AssertEqual(fixture.Missions[1].Id, finalCaptain.ActiveMissionIds.SingleOrDefault(), "Captain should still track the newer mission");
                }
            }));

            cases.Add(CaseAsync("no_pending_missions_captain_released_to_idle", "NoPendingMissions_CaptainReleasedToIdle", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    ServiceSet services = CreateServices(logging, db, settings, git);
                    MissionService missionService = services.Missions;

                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 1, missionCount: 1);
                    Captain captain = fixture.Captains[0];
                    Vessel vessel = fixture.Vessel;

                    // Assign the single mission
                    await missionService.TryAssignAsync(fixture.Missions[0], vessel);

                    // Complete it — no more pending missions, captain should become Idle
                    Captain? captainForCompletion = await db.Captains.ReadAsync(captain.Id);
                    await missionService.HandleCompletionAsync(captainForCompletion!, fixture.Missions[0].Id);

                    Captain? finalCaptain = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Idle, finalCaptain!.State, "Captain should be Idle when no pending missions remain");
                    AssertNull(finalCaptain.ActiveMissionIds.SingleOrDefault(), "CurrentMissionId should be null");
                    AssertNull(finalCaptain.Assignment?.DockId, "CurrentDockId should be null");
                    AssertNull(finalCaptain.ProcessId, "ProcessId should be null");
                }
            }));

            cases.Add(CaseAsync("working_captain_never_eligible_for_additional_assignment", "WorkingCaptain_NeverEligibleForAdditionalAssignment", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    ServiceSet services = CreateServices(logging, db, settings, git);
                    MissionService missionService = services.Missions;

                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 1, missionCount: 2);
                    Captain captain = fixture.Captains[0];
                    Vessel vessel = fixture.Vessel;

                    // Assign mission 1 - should succeed
                    await missionService.TryAssignAsync(fixture.Missions[0], vessel);

                    Mission? m1 = await db.Missions.ReadAsync(fixture.Missions[0].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m1!.Status, "Mission 1 should be InProgress");

                    // Try to assign mission 2 - should fail because the only captain is working
                    await missionService.TryAssignAsync(fixture.Missions[1], vessel);

                    Mission? m2 = await db.Missions.ReadAsync(fixture.Missions[1].Id);
                    AssertEqual(MissionStatusEnum.Pending, m2!.Status, "Mission 2 should remain Pending - working captain is not eligible");

                    // Captain should still be working on mission 1 only
                    Captain? captainAfter = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Working, captainAfter!.State, "Captain should still be Working");
                    AssertEqual(m1.Id, captainAfter.ActiveMissionIds.SingleOrDefault(), "Captain should still track mission 1");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Sequential Dispatch",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static async Task CompleteAsync(DatabaseDriver db, Mission mission)
        {
            mission.Status = MissionStatusEnum.Complete;
            mission.ProcessId = null;
            await db.Missions.UpdateAsync(mission);
            if (mission.CaptainId != null) await db.Captains.TryReleaseFromMissionAsync(mission.CaptainId, mission.Id);
        }

        private static ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));
            return settings;
        }

        /// <summary>
        /// Create a MissionService with a CaptainService that has a stub OnLaunchAgent.
        /// Returns the mission service, captain service, and dock service.
        /// </summary>
        private static ServiceSet CreateServices(LoggingModule logging, DatabaseDriver db, ArmadaSettings settings, StubGitService git)
        {
            IDockService dockService = new DockService(logging, db, settings, git);
            CaptainService captainService = new CaptainService(logging, db, settings, git, dockService);

            int nextPid = 1000;
            captainService.OnLaunchAgent = (captain, mission, dock) =>
            {
                return Task.FromResult(nextPid++);
            };

            MissionService missionService = new MissionService(logging, db, settings, dockService, captainService);
            return new ServiceSet(missionService, captainService, dockService);
        }

        /// <summary>
        /// Create a fleet, vessel, voyage, captains, and pending missions.
        /// </summary>
        private static async Task<TestFixture> SetupFixtureAsync(
            DatabaseDriver db,
            int captainCount,
            int missionCount)
        {
            Fleet fleet = new Fleet("TestFleet");
            await db.Fleets.CreateAsync(fleet);

            Vessel vessel = new Vessel("TestVessel", "https://github.com/test/repo");
            vessel.FleetId = fleet.Id;
            await db.Vessels.CreateAsync(vessel);

            Voyage voyage = new Voyage("TestVoyage");
            voyage.Status = VoyageStatusEnum.InProgress;
            await db.Voyages.CreateAsync(voyage);

            List<Captain> captains = new List<Captain>();
            for (int i = 0; i < captainCount; i++)
            {
                Captain captain = new Captain("captain-" + (i + 1));
                captain.State = CaptainStateEnum.Idle;
                await CaptainAssignmentFixture.CreateAsync(db, captain);
                captains.Add(captain);
            }

            List<Mission> missions = new List<Mission>();
            for (int i = 0; i < missionCount; i++)
            {
                Mission mission = new Mission("Mission " + (i + 1));
                mission.Status = MissionStatusEnum.Pending;
                mission.VesselId = vessel.Id;
                mission.VoyageId = voyage.Id;
                mission.Priority = 100 + i;
                await db.Missions.CreateAsync(mission);
                missions.Add(mission);
            }

            return new TestFixture(fleet, vessel, voyage, captains, missions);
        }

        #endregion

        #region Private-Types

        /// <summary>
        /// Container for test service instances.
        /// </summary>
        private sealed class ServiceSet
        {
            /// <summary>
            /// Mission service.
            /// </summary>
            public MissionService Missions { get; }

            /// <summary>
            /// Captain service.
            /// </summary>
            public CaptainService Captains { get; }

            /// <summary>
            /// Dock service.
            /// </summary>
            public IDockService Docks { get; }

            /// <summary>
            /// Instantiate.
            /// </summary>
            public ServiceSet(MissionService missions, CaptainService captains, IDockService docks)
            {
                Missions = missions;
                Captains = captains;
                Docks = docks;
            }
        }

        /// <summary>
        /// Test fixture containing pre-created entities.
        /// </summary>
        private sealed class TestFixture
        {
            /// <summary>
            /// Test fleet.
            /// </summary>
            public Fleet Fleet { get; }

            /// <summary>
            /// Test vessel.
            /// </summary>
            public Vessel Vessel { get; }

            /// <summary>
            /// Test voyage.
            /// </summary>
            public Voyage Voyage { get; }

            /// <summary>
            /// Test captains.
            /// </summary>
            public List<Captain> Captains { get; }

            /// <summary>
            /// Test missions.
            /// </summary>
            public List<Mission> Missions { get; }

            /// <summary>
            /// Instantiate.
            /// </summary>
            public TestFixture(Fleet fleet, Vessel vessel, Voyage voyage, List<Captain> captains, List<Mission> missions)
            {
                Fleet = fleet;
                Vessel = vessel;
                Voyage = voyage;
                Captains = captains;
                Missions = missions;
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
