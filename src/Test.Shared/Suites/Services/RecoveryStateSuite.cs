namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// RELIABILITY_SIMPLICITY_PLAN.md RS-3.7: endpoint breakers, launch backoff, transient outage starts and escalation
    /// cooldowns survive an Admiral restart. Each case writes the state through one service instance and reads it back
    /// through a new one, as a restarted Admiral would.
    /// </summary>
    public sealed class RecoveryStateSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.RecoveryState";

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("store_round_trip", "Entries are upserted, read, listed while unexpired, deleted and purged", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                await db.RecoveryState.UpsertAsync(new RecoveryStateEntry { Kind = "K", Key = "a", PayloadJson = "{\"n\":1}", ExpiresUtc = DateTime.UtcNow.AddHours(1) }).ConfigureAwait(false);
                await db.RecoveryState.UpsertAsync(new RecoveryStateEntry { Kind = "K", Key = "a", PayloadJson = "{\"n\":2}", ExpiresUtc = DateTime.UtcNow.AddHours(1) }).ConfigureAwait(false);
                await db.RecoveryState.UpsertAsync(new RecoveryStateEntry { Kind = "K", Key = "old", PayloadJson = "{}", ExpiresUtc = DateTime.UtcNow.AddMinutes(-1) }).ConfigureAwait(false);
                await db.RecoveryState.UpsertAsync(new RecoveryStateEntry { Kind = "Other", Key = "a", PayloadJson = "{}", ExpiresUtc = DateTime.UtcNow.AddHours(1) }).ConfigureAwait(false);

                RecoveryStateEntry? read = await db.RecoveryState.ReadAsync("K", "a").ConfigureAwait(false);
                AssertNotNull(read, "the entry is read back");
                AssertEqual("{\"n\":2}", read!.PayloadJson, "an upsert replaces the payload");

                List<RecoveryStateEntry> listed = await db.RecoveryState.EnumerateAsync("K").ConfigureAwait(false);
                AssertEqual(1, listed.Count, "an expired entry and another kind are not listed");

                AssertEqual(1, await db.RecoveryState.PurgeExpiredAsync().ConfigureAwait(false), "the expired entry is purged");
                await db.RecoveryState.DeleteAsync("K", "a").ConfigureAwait(false);
                AssertNull(await db.RecoveryState.ReadAsync("K", "a").ConfigureAwait(false), "a deleted entry is gone");
                AssertNotNull(await db.RecoveryState.ReadAsync("Other", "a").ConfigureAwait(false), "a delete touches only its kind");
            }));

            cases.Add(CaseAsync("endpoint_breaker_survives_restart", "An open endpoint breaker still holds missions after a restart, and a clean run clears the stored breaker", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelEndpoint gpu = await db.ModelEndpoints.CreateAsync(new ModelEndpoint { Name = "gpu", BaseUrl = "http://gpu-box:11434", Model = "qwen" }).ConfigureAwait(false);
                Captain captain = await CaptainAssignmentFixture.CreateAsync(db, new Captain("gpu-one", AgentRuntimeEnum.ApiEndpoint) { ModelEndpointId = gpu.Id }).ConfigureAwait(false);

                Harness before = new Harness(testDb);
                for (int i = 0; i < 3; i++) await before.Missions.RecordEndpointOutcomeAsync(captain, false).ConfigureAwait(false);

                Harness after = new Harness(testDb);
                after.Captains.OnLaunchAgent = (_, _, _) => Task.FromResult(9800);
                await after.Missions.LoadRecoveryStateAsync().ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Mission mission = await db.Missions.CreateAsync(new Mission("Work", "Do it") { VesselId = vessel.Id }).ConfigureAwait(false);
                AssertFalse(await after.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false), "the restarted Admiral keeps the failing endpoint's breaker open");

                await after.Missions.RecordEndpointOutcomeAsync(captain, true).ConfigureAwait(false);
                AssertEqual(0, (await db.RecoveryState.EnumerateAsync(RecoveryStateKinds.EndpointBreaker).ConfigureAwait(false)).Count, "a clean run deletes the stored breaker");
                AssertTrue(await after.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false), "a clean run closes the breaker");
            }));

            cases.Add(CaseAsync("launch_backoff_survives_restart", "A mission backing off after a failed launch still waits after a restart, with the same reason", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                int launches = 0;
                Harness before = new Harness(testDb);
                before.Captains.OnLaunchAgent = (_, _, _) =>
                {
                    launches++;
                    throw new AgentRuntimeNotInstalledException(AgentRuntimeEnum.Mux, "mux", null);
                };
                await CaptainAssignmentFixture.CreateAsync(db, new Captain("mux", AgentRuntimeEnum.Mux)).ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Mission mission = await db.Missions.CreateAsync(new Mission("Needs mux", "Work") { VesselId = vessel.Id }).ConfigureAwait(false);
                AssertFalse(await before.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false), "the first launch fails");

                Harness after = new Harness(testDb);
                after.Captains.OnLaunchAgent = (_, _, _) =>
                {
                    launches++;
                    return Task.FromResult(9900);
                };
                await after.Missions.LoadRecoveryStateAsync().ConfigureAwait(false);
                Mission? stored = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                MissionAssignmentBlocker? blocker = await after.Missions.GetAssignmentBlockerAsync(stored!).ConfigureAwait(false);
                AssertNotNull(blocker, "the restarted Admiral still explains the wait");
                AssertEqual(MissionAssignmentBlockerReasonEnum.LaunchFailing, blocker!.Reason);
                AssertContains("not installed", blocker.Summary);
                AssertFalse(await after.Missions.TryAssignAsync(stored!, vessel).ConfigureAwait(false), "still backing off after the restart");
                AssertEqual(1, launches, "no launch during the backoff");
            }));

            cases.Add(CaseAsync("transient_outage_start_survives_restart", "A restart during a transient outage does not grant the mission a new grace period", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = CreateSettings();
                settings.TransientOutageGraceMinutes = 10;
                AdmiralService before = CreateAdmiral(testDb, settings);
                AssertTrue(await before.WithinTransientOutageGraceAsync("msn_outage").ConfigureAwait(false), "a new outage is within its grace");
                RecoveryStateEntry? row = await testDb.Driver.RecoveryState.ReadAsync(RecoveryStateKinds.TransientOutage, "msn_outage").ConfigureAwait(false);
                AssertNotNull(row, "the outage start is stored");

                // The outage began 15 minutes ago on the previous run.
                await new RecoveryStateStore(testDb.Driver).SaveAsync(RecoveryStateKinds.TransientOutage, "msn_outage",
                    new TransientOutageRecord { StartedUtc = DateTime.UtcNow.AddMinutes(-15) }, DateTime.UtcNow.AddDays(1)).ConfigureAwait(false);
                AdmiralService after = CreateAdmiral(testDb, settings);
                await after.LoadRecoveryStateAsync().ConfigureAwait(false);
                AssertFalse(await after.WithinTransientOutageGraceAsync("msn_outage").ConfigureAwait(false), "the grace is measured from the stored start, not the restart");
                AssertNull(await testDb.Driver.RecoveryState.ReadAsync(RecoveryStateKinds.TransientOutage, "msn_outage").ConfigureAwait(false), "an outage past its grace is forgotten");
            }));

            cases.Add(CaseAsync("escalation_cooldown_survives_restart", "A rule that fired just before a restart does not fire again after it; one whose cooldown passed does", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.EscalationRules.Add(new EscalationRule(EscalationTriggerEnum.PoolExhausted, EscalationActionEnum.Log) { CooldownMinutes = 5 });

                EscalationService before = new EscalationService(CreateLogging(), testDb.Driver, settings);
                await before.FireAsync(EscalationTriggerEnum.PoolExhausted, "pool", "first").ConfigureAwait(false);
                AssertEqual(1, await CountSignalsAsync(testDb).ConfigureAwait(false), "the rule fires");

                EscalationService after = new EscalationService(CreateLogging(), testDb.Driver, settings);
                await after.LoadRecoveryStateAsync().ConfigureAwait(false);
                await after.FireAsync(EscalationTriggerEnum.PoolExhausted, "pool", "after the restart").ConfigureAwait(false);
                AssertEqual(1, await CountSignalsAsync(testDb).ConfigureAwait(false), "the cooldown survives the restart");

                // The previous run fired the rule six minutes ago, past its five-minute cooldown.
                await new RecoveryStateStore(testDb.Driver).SaveAsync(RecoveryStateKinds.EscalationCooldown, EscalationTriggerEnum.PoolExhausted + ":pool",
                    new EscalationCooldownRecord { LastFiredUtc = DateTime.UtcNow.AddMinutes(-6) }, DateTime.UtcNow.AddMinutes(1)).ConfigureAwait(false);
                EscalationService later = new EscalationService(CreateLogging(), testDb.Driver, settings);
                await later.LoadRecoveryStateAsync().ConfigureAwait(false);
                await later.FireAsync(EscalationTriggerEnum.PoolExhausted, "pool", "cooldown over").ConfigureAwait(false);
                AssertEqual(2, await CountSignalsAsync(testDb).ConfigureAwait(false), "a cooldown that ended while stopped does not hold the rule");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Recovery State", cases: cases);
        }

        private static AdmiralService CreateAdmiral(TestDatabase testDb, ArmadaSettings settings)
        {
            LoggingModule logging = CreateLogging();
            StubGitService git = new StubGitService();
            IDockService docks = new DockService(logging, testDb.Driver, settings, git);
            CaptainService captains = new CaptainService(logging, testDb.Driver, settings, git, docks);
            IMissionService missions = new MissionService(logging, testDb.Driver, settings, docks, captains);
            return new AdmiralService(logging, testDb.Driver, settings, captains, missions, new VoyageService(logging, testDb.Driver), docks);
        }

        private static async Task<int> CountSignalsAsync(TestDatabase testDb)
        {
            List<Signal> signals = await testDb.Driver.Signals.EnumerateRecentAsync(100).ConfigureAwait(false);
            return signals.Count;
        }

        private static async Task<Vessel> CreateVesselAsync(TestDatabase testDb)
        {
            Vessel vessel = new Vessel("recovery-vessel-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://github.com/test/repo.git");
            vessel.LocalPath = TestTemp.NewDirectory("recovery_bare");
            vessel.WorkingDirectory = TestTemp.NewDirectory("recovery_work");
            vessel.DefaultBranch = "main";
            return await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = TestTemp.NewDirectory("recovery_docks");
            settings.ReposDirectory = TestTemp.NewDirectory("recovery_repos");
            settings.LogDirectory = TestTemp.NewDirectory("recovery_logs");
            return settings;
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

        private sealed class Harness
        {
            public CaptainService Captains { get; }

            public MissionService Missions { get; }

            public Harness(TestDatabase testDb)
            {
                LoggingModule logging = CreateLogging();
                ArmadaSettings settings = CreateSettings();
                IGitService git = new DirCreatingGitService();
                IDockService docks = new DockService(logging, testDb.Driver, settings, git);
                Captains = new CaptainService(logging, testDb.Driver, settings, git, docks);
                Missions = new MissionService(logging, testDb.Driver, settings, docks, Captains, git: git);
            }
        }
    }
}
