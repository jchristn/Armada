namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
    /// Captain selection under concurrency: ten captains on five shared, nearly full accounts, so the protected reserve
    /// (not the concurrency limits) decides how much runs at once. Covers the 200-mission scheduler stress and a
    /// fleet-action fan-out that dispatches one voyage per vessel at once.
    /// </summary>
    public sealed class UsageSelectionStressSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.UsageSelectionStress";
        private const string Host = "stress-host";
        private const double ReservePercent = 10;
        private const double PerAttemptPercent = 4;
        // Above a 10% reserve with 4% attempts these hold 2, 2, 1, 1 and 0 attempts: six slots, fewer than the global cap of eight.
        private static readonly double[] _AccountUsedPercents = new[] { 80.0, 82.0, 84.0, 86.0, 88.0 };

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(SuiteId, "enforced_two_hundred_missions_respect_limits_and_reserve",
                    "200 enforced missions make progress without exceeding concurrency limits or any account's protected reserve", StressAsync,
                    tags: new List<string> { TestTags.Positive }),
                new TestCaseDescriptor(SuiteId, "fleet_action_fan_out_waits_at_the_reserve_and_drains",
                    "A fleet action fanned out to twelve vessels admits only what the accounts can hold, records waits, and drains as captures settle", FanOutAsync,
                    tags: new List<string> { TestTags.Positive })
            };
            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Captain selection under concurrency stress", cases: cases);
        }

        private static async Task StressAsync(CancellationToken token)
        {
            using StressHarness h = await StressHarness.CreateAsync(vesselCount: 5, token).ConfigureAwait(false);
            List<Voyage> voyages = new List<Voyage>();
            for (int i = 0; i < 10; i++)
                voyages.Add(await h.Db.Voyages.CreateAsync(new Voyage("stress-voyage-" + i) { TenantId = h.Tenant.Id, UserId = h.Owner.Id, Status = VoyageStatusEnum.InProgress }, token).ConfigureAwait(false));
            const int Count = 200;
            for (int i = 0; i < Count; i++)
                await h.Db.Missions.CreateAsync(new Mission("stress mission " + i, "produce a report") { TenantId = h.Tenant.Id, UserId = h.Owner.Id,
                    VesselId = h.Vessels[i % 5].Id, VoyageId = voyages[i % 10].Id, Mode = MissionModeEnum.Research }, token).ConfigureAwait(false);

            int completed = 0;
            int maxRunning = 0;
            for (int wave = 0; completed < Count && wave < Count * 3; wave++)
            {
                List<Mission> pending = await h.PendingAsync(token).ConfigureAwait(false);
                // Nine concurrent dispatchers race for the same shared accounts each wave.
                await Task.WhenAll(pending.Take(9).Select(m => Task.Run(() => h.Missions.TryAssignAsync(m, h.Vessels.First(v => v.Id == m.VesselId)), token))).ConfigureAwait(false);
                List<Mission> running = await h.RunningAsync(token).ConfigureAwait(false);
                maxRunning = Math.Max(maxRunning, running.Count);
                AssertTrue(running.Count <= 8, "global cap");
                foreach (IGrouping<string?, Mission> group in running.GroupBy(m => m.VoyageId)) AssertTrue(group.Count() <= 3, "voyage mission cap");
                await h.CheckInvariantsAsync(token).ConfigureAwait(false);
                if (running.Count == 0 && pending.Count > 0)
                {
                    // Every account is waiting on unsettled estimates: settle them so the next wave can admit.
                    await h.SettleAsync(token).ConfigureAwait(false);
                    continue;
                }
                completed += await h.CompleteAsync(running, token).ConfigureAwait(false);
                if (wave % 3 == 2) await h.SettleAsync(token).ConfigureAwait(false);
            }
            AssertEqual(Count, completed, "every mission completes");
            AssertTrue(maxRunning > 1, "admission ran work in parallel");
            AssertEqual(8.0, Math.Round(h.Peak[0], 6), "the roomiest account fills to its reserve");
            AssertEqual(4.0, Math.Round(h.Peak[2], 6), "an account with room for one attempt takes exactly one");
            AssertEqual(0.0, h.Peak[4], "an account at the reserve never runs work");
            AssertTrue((await h.Db.Captains.EnumerateAsync(token).ConfigureAwait(false)).All(c => c.ActiveMissionCount == 0));
            foreach (Mission mission in (await h.Db.Missions.EnumerateAsync(token).ConfigureAwait(false)).Where(m => m.TenantId == h.Tenant.Id).Take(20))
            {
                CaptainSelectionDecision? decision = await h.Db.CaptainSelections.ReadCurrentDecisionAsync(h.Tenant.Id, mission.Id, token: token).ConfigureAwait(false);
                AssertNotNull(decision, "every admitted mission has a recorded decision");
                AssertEqual(10, decision!.CandidateCount);
            }
        }

        private static async Task FanOutAsync(CancellationToken token)
        {
            using StressHarness h = await StressHarness.CreateAsync(vesselCount: 12, token).ConfigureAwait(false);
            AdmiralFleetActionMissionDispatcher dispatcher = new AdmiralFleetActionMissionDispatcher(h.Db, h.Admiral);
            // The runner dispatches each vessel's target as its own voyage; all twelve land at once.
            string[] voyageIds = await Task.WhenAll(h.Vessels.Select(v => Task.Run(() => dispatcher.DispatchAsync(v, "Repository status report", "Report the repository status.", null, token), token))).ConfigureAwait(false);
            AssertEqual(12, voyageIds.Distinct(StringComparer.Ordinal).Count());
            // Dispatchers that lost the admission race retry on the scheduler's next pass, which records why each target waits.
            foreach (Mission mission in await h.PendingAsync(token).ConfigureAwait(false))
                await h.Missions.TryAssignAsync(mission, h.Vessels.First(v => v.Id == mission.VesselId), token).ConfigureAwait(false);
            List<Mission> running = await h.RunningAsync(token).ConfigureAwait(false);
            List<Mission> waiting = await h.PendingAsync(token).ConfigureAwait(false);
            AssertEqual(12, running.Count + waiting.Count, "one mission per vessel");
            AssertEqual(6, running.Count, "the accounts hold 2, 2, 1, 1 and 0 attempts");
            await h.CheckInvariantsAsync(token).ConfigureAwait(false);
            foreach (Mission mission in waiting)
            {
                CaptainSelectionDecision? wait = (await h.Db.CaptainSelections.EnumerateByMissionAsync(h.Tenant.Id, mission.Id, token: token).ConfigureAwait(false)).Objects.FirstOrDefault();
                AssertNotNull(wait, "a waiting fan-out target records why it waits");
                AssertEqual(CaptainSelectionOutcomes.Waiting, wait!.Outcome);
                AssertEqual(UsageSelectionReasonCodes.BelowReserve, wait.PrimaryReasonCode);
            }
            int completed = 0;
            for (int round = 0; completed < 12 && round < 30; round++)
            {
                completed += await h.CompleteAsync(await h.RunningAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
                await h.SettleAsync(token).ConfigureAwait(false);
                foreach (Mission mission in await h.PendingAsync(token).ConfigureAwait(false))
                    await h.Missions.TryAssignAsync(mission, h.Vessels.First(v => v.Id == mission.VesselId), token).ConfigureAwait(false);
                await h.CheckInvariantsAsync(token).ConfigureAwait(false);
            }
            AssertEqual(12, completed, "every fan-out target runs once captures settle earlier work");
            AssertEqual(0.0, h.Peak[4], "the account at its reserve never ran a target");
        }

        private static string Account(int index) => "stress-account-" + index;

        // One tenant, ten captains (two per account) on one credential host, nearly full accounts and an Admiral service.
        private sealed class StressHarness : IDisposable
        {
            private TestDatabase _Fixture = null!;
            private ConcurrencyInvariantMonitor _Monitor = null!;
            private UsageSelectionReservationReconciler _Reconciler = null!;
            internal DatabaseDriver Db => _Fixture.Driver;
            internal MissionService Missions = null!;
            internal AdmiralService Admiral = null!;
            internal TenantMetadata Tenant = null!;
            internal UserMaster Owner = null!;
            internal List<Vessel> Vessels = new List<Vessel>();
            internal List<Captain> Fleet = new List<Captain>();
            internal double[] Peak = new double[5];

            internal static async Task<StressHarness> CreateAsync(int vesselCount, CancellationToken token)
            {
                StressHarness h = new StressHarness();
                h._Fixture = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings
                {
                    DocksDirectory = TestTemp.NewDirectory("selection_stress_docks"), ReposDirectory = TestTemp.NewDirectory("selection_stress_repos"),
                    MaxConcurrentMissions = 8
                };
                settings.UsageCapture.Enabled = true;
                settings.UsageSelection.Mode = UsageSelectionModeEnum.Enforce;
                settings.UsageSelection.Strategy = CaptainSelectionStrategyEnum.MostRemainingAllowance;
                settings.UsageSelection.ReservePercent = ReservePercent;
                settings.UsageSelection.PerAttemptReservationPercent = PerAttemptPercent;
                settings.UsageSelection.SettlementLagSeconds = 0;
                LoggingModule logging = new LoggingModule(); logging.Settings.EnableConsole = false;
                StubGitService git = new StubGitService();
                IDockService docks = new DockService(logging, h.Db, settings, git);
                CaptainService captains = new CaptainService(logging, h.Db, settings, git, docks);
                int pid = 3000;
                captains.OnLaunchAgent = (_, _, _) => Task.FromResult(Interlocked.Increment(ref pid));
                h.Missions = new MissionService(logging, h.Db, settings, docks, captains) { UsageSelectionLocalHostId = Host };
                h.Admiral = new AdmiralService(logging, h.Db, settings, captains, h.Missions, new VoyageService(logging, h.Db), docks);
                h._Monitor = new ConcurrencyInvariantMonitor(h.Db, settings, logging);
                h._Reconciler = new UsageSelectionReservationReconciler(h.Db);

                h.Tenant = await h.Db.Tenants.CreateAsync(new TenantMetadata("selection-stress"), token).ConfigureAwait(false);
                h.Owner = await h.Db.Users.CreateAsync(new UserMaster(h.Tenant.Id, "stress@example.invalid", "fixture-password"), token).ConfigureAwait(false);
                for (int i = 0; i < vesselCount; i++)
                    h.Vessels.Add(await h.Db.Vessels.CreateAsync(new Vessel("stress-vessel-" + i, "https://github.com/test/repo") { TenantId = h.Tenant.Id, UserId = h.Owner.Id,
                        MaxParallelVoyages = 2, MaxParallelMissionsPerVoyage = 3 }, token).ConfigureAwait(false));
                for (int i = 0; i < 10; i++)
                {
                    Captain captain = await h.Db.Captains.CreateAsync(new Captain("stress-captain-" + i) { TenantId = h.Tenant.Id, UserId = h.Owner.Id, Model = "opus",
                        Runtime = AgentRuntimeEnum.ClaudeCode, UsageCaptureEnabled = true }, token).ConfigureAwait(false);
                    captain.MaxConcurrency = 2;
                    await CaptainAssignmentFixture.UpdateAsync(h.Db, captain, updateConcurrency: true).ConfigureAwait(false);
                    h.Fleet.Add(captain);
                    await h.Db.UsageMemberships.CreateAsync(new UsageAccountMembership { TenantId = h.Tenant.Id, CaptainId = captain.Id, HostId = Host, Runtime = captain.Runtime,
                        Provider = "anthropic", AccountKey = Account(i % 5), ConfigurationRevision = UsageAccountResolver.ConfigurationRevision(Host, captain.Runtime, null) }, token).ConfigureAwait(false);
                }
                for (int account = 0; account < 5; account++) await h.CaptureAsync(account, token).ConfigureAwait(false);
                return h;
            }

            internal async Task<List<Mission>> PendingAsync(CancellationToken token)
                => (await Db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending, token).ConfigureAwait(false)).Where(m => m.TenantId == Tenant.Id).ToList();

            internal async Task<List<Mission>> RunningAsync(CancellationToken token)
                => (await Db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress, token).ConfigureAwait(false)).Where(m => m.TenantId == Tenant.Id).ToList();

            // Concurrency limits, the invariant monitor, and every account's reservations against its protected reserve.
            internal async Task CheckInvariantsAsync(CancellationToken token)
            {
                foreach (Captain captain in await Db.Captains.EnumerateAsync(token).ConfigureAwait(false)) AssertTrue(captain.ActiveMissionCount <= captain.MaxConcurrency, "captain cap");
                await _Monitor.CheckAsync(token).ConfigureAwait(false);
                AssertEqual(0, (await Db.Events.EnumerateByTypeAsync("concurrency.limit_exceeded", token: token).ConfigureAwait(false)).Count, "no invariant violations");
                for (int account = 0; account < 5; account++)
                {
                    double reserved = (await Db.CaptainSelections.ReadReservationsAsync(Tenant.Id, Host, "ClaudeCode", Account(account), token).ConfigureAwait(false)).Sum(r => r.ReservedPercent);
                    Peak[account] = Math.Max(Peak[account], reserved);
                    // Admission requires reserve plus one attempt before reserving, so the reserve itself is never consumed.
                    AssertTrue(reserved <= 100 - _AccountUsedPercents[account] - ReservePercent + 0.000001,
                        "account " + account + " reserved " + reserved + "% past its protected reserve (used " + _AccountUsedPercents[account] + "%)");
                }
            }

            internal async Task<int> CompleteAsync(List<Mission> running, CancellationToken token)
            {
                foreach (Mission mission in running)
                {
                    mission.Status = MissionStatusEnum.Complete;
                    mission.ProcessId = null;
                    await Db.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                    AssertTrue(await Db.Captains.TryReleaseFromMissionAsync(mission.CaptainId!, mission.Id, token).ConfigureAwait(false));
                }
                return running.Count;
            }

            // Ended attempts become unsettled, then a fresh capture of each account taken after they ended settles them.
            internal async Task SettleAsync(CancellationToken token)
            {
                for (int pass = 0; pass < 10 && await _Reconciler.ReconcileAsync(DateTime.UtcNow, token: token).ConfigureAwait(false) > 0; pass++) { }
                await Task.Delay(20, token).ConfigureAwait(false);
                for (int account = 0; account < 5; account++) await CaptureAsync(account, token).ConfigureAwait(false);
                for (int pass = 0; pass < 10 && await _Reconciler.ReconcileAsync(DateTime.UtcNow.AddMilliseconds(50), token: token).ConfigureAwait(false) > 0; pass++) { }
            }

            internal async Task CaptureAsync(int account, CancellationToken token)
            {
                DateTime now = DateTime.UtcNow;
                List<string> members = Fleet.Where((c, i) => i % 5 == account).Select(c => c.Id).ToList();
                UsageSnapshot snapshot = new UsageSnapshot { TenantId = Tenant.Id, HostId = Host, AccountKey = Account(account), Runtime = AgentRuntimeEnum.ClaudeCode,
                    Provider = "anthropic", Source = UsageCaptureSourceEnum.OAuthEndpoint, Status = UsageSnapshotStatusEnum.Ok, ProbeStartedUtc = now, CapturedUtc = now, ReceivedUtc = now,
                    WindowsJson = UsageWindowsJson.Serialize(new List<UsageWindow> { new UsageWindow { Name = "five-hour", UsedPercent = _AccountUsedPercents[account] } }) };
                await Db.UsageSnapshots.CreateAsync(snapshot, members, token).ConfigureAwait(false);
                string revision = UsageAccountResolver.ConfigurationRevision(Host, AgentRuntimeEnum.ClaudeCode, null);
                await Db.UsageSnapshots.UpsertLatestStateAsync(new UsageLatestState { TenantId = Tenant.Id, HostId = Host, AccountKey = Account(account), Runtime = AgentRuntimeEnum.ClaudeCode,
                    Provider = "anthropic", LastGoodSnapshotId = snapshot.Id, LastGoodCapturedUtc = now, LastGoodReceivedUtc = now, LastGoodStatus = UsageSnapshotStatusEnum.Ok,
                    LastGoodWindowsJson = snapshot.WindowsJson,
                    CaptureContextKey = UsageAccountResolver.CaptureContextKey(UsageAccountResolver.GroupKey(Tenant.Id, Host, AgentRuntimeEnum.ClaudeCode, null), revision) }, token).ConfigureAwait(false);
            }

            public void Dispose() => _Fixture.Dispose();
        }
    }
}
