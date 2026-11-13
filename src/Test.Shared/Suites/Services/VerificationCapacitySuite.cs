namespace Test.Shared.Suites.Services
{
    using System.Linq;
    using System.Reflection;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Verification is durable ownership, not agent capacity; repair admission is race-safe.</summary>
    public sealed class VerificationCapacitySuite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            const string suite = "Services.VerificationCapacity";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, Func<Task> test) => cases.Add(new TestCaseDescriptor(suite, id, id, _ => test()));
            Add("retained_exit_survives_reconciliation_and_sibling_admission", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                Mission verify = await h.MarkVerifyingAsync();
                Captain free = (await h.Db.Captains.ReadAsync(h.Captain.Id))!;
                AssertEqual(0, free.ActiveMissionCount); AssertEqual(0, free.ActiveMissionIds.Count);
                AssertEqual(CaptainStateEnum.Idle, free.State); AssertTrue(free.HasMissionCapacity);
                Mission sibling = await h.Db.Missions.CreateAsync(new Mission("sibling"));
                AssertTrue(await h.Db.Captains.TryClaimAsync(free.Id, sibling.Id, null));
                await h.Db.Captains.ReconcileMissionSlotsAsync();
                AssertEqual(4242, await h.Db.Captains.ReadMissionProcessAsync(free.Id, verify.Id));
                Captain busy = (await h.Db.Captains.ReadAsync(free.Id))!;
                AssertEqual(1, busy.ActiveMissionCount); AssertEqual(sibling.Id, busy.ActiveMissionIds.Single());
                AssertFalse(await h.Missions.TryAdmitVerificationResumeAsync(verify, free));
                await h.FinishAsync(sibling);
                bool[] winners = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => h.Missions.TryAdmitVerificationResumeAsync(verify, free)));
                AssertEqual(1, winners.Count(x => x));
                AssertEqual(1, (await h.Db.Captains.ReadAsync(free.Id))!.ActiveMissionCount);
            });
            Add("stale_verification_cannot_mutate_new_assignment", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                Mission verify = await h.MarkVerifyingAsync();
                DateTime staleVersion = verify.LastUpdateUtc;
                verify.Status = MissionStatusEnum.Complete; await h.Db.Missions.UpdateAsync(verify);
                await h.Db.Captains.TryReleaseFromMissionAsync(h.Captain.Id, verify.Id);
                Mission sibling = await h.Db.Missions.CreateAsync(new Mission("new owner"));
                AssertTrue(await h.Db.Captains.TryClaimAsync(h.Captain.Id, sibling.Id, null));
                AssertFalse(await h.Db.Captains.ReleaseVerificationCapacityAsync(h.Captain.Id, verify.Id, 4242, staleVersion));
                AssertFalse(await h.Db.Captains.TryResumeVerificationAsync(h.Captain.Id, verify.Id, staleVersion, 256));
                AssertEqual(sibling.Id, (await h.Db.Captains.ReadAsync(h.Captain.Id))!.ActiveMissionIds.Single());
            });
            Add("long_check_releases_capacity_before_finishing", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
                h.Commands.Handler = async _ => { entered.TrySetResult(); await finish.Task; return new HostCommandResult(); };
                Task exit = h.Admiral.HandleProcessExitAsync(4242, 0, h.Captain.Id, h.Mission.Id);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                try
                {
                    AssertEqual(MissionStatusEnum.Verifying, (await h.Db.Missions.ReadAsync(h.Mission.Id))!.Status);
                    AssertEqual(0, (await h.Db.Captains.ReadAsync(h.Captain.Id))!.ActiveMissionCount);
                    await h.Admiral.HandleProcessExitAsync(4242, 0, h.Captain.Id, h.Mission.Id);
                    Mission sibling = await h.Db.Missions.CreateAsync(new Mission("during checks"));
                    AssertTrue(await h.Db.Captains.TryClaimAsync(h.Captain.Id, sibling.Id, null));
                    await h.Db.Captains.ReconcileMissionSlotsAsync();
                    finish.TrySetResult(); await exit.WaitAsync(TimeSpan.FromSeconds(10));
                    AssertEqual(MissionStatusEnum.Complete, (await h.Db.Missions.ReadAsync(h.Mission.Id))!.Status);
                    AssertEqual(sibling.Id, (await h.Db.Captains.ReadAsync(h.Captain.Id))!.ActiveMissionIds.Single());
                }
                finally { finish.TrySetResult(); await exit; }
            });
            foreach (string gate in new[] { "captain", "global", "runtime", "vessel", "endpoint" })
                Add("failed_check_waits_for_" + gate + "_then_replays_after_restart", async () =>
                {
                    using Harness h = await Harness.CreateAsync();
                    int headChecks = 0;
                    h.Commands.Handler = request => Task.FromResult(new HostCommandResult
                    {
                        ExitCode = !request.Arguments.Any(a => a.Contains("verify-fixture", StringComparison.Ordinal)) || request.WorkingDirectory.EndsWith(".base-check", StringComparison.Ordinal) ? 0 : Interlocked.Increment(ref headChecks) > 0 ? 1 : 0,
                        StandardOutput = "error: broken build"
                    });
                    Mission verify = await h.MarkVerifyingAsync();
                    if (gate == "global") h.Settings.MaxConcurrentMissions = 1;
                    if (gate == "runtime") h.Settings.Agents = new List<AgentSettings> { new AgentSettings { Runtime = AgentRuntimeEnum.Mux, MaxConcurrent = 1 } };
                    if (gate == "vessel") { h.Vessel.MaxParallelMissionsPerVoyage = 1; await h.Db.Vessels.UpdateAsync(h.Vessel); }
                    Captain other = gate == "captain" ? h.Captain : await CaptainAssignmentFixture.CreateAsync(h.Db, new Captain("occupier", AgentRuntimeEnum.Mux));
                    if (gate == "endpoint")
                    {
                        ModelEndpoint endpoint = await h.Db.ModelEndpoints.CreateAsync(new ModelEndpoint { Name = "shared", BaseUrl = "http://127.0.0.1:19899/v1", InferenceOptions = new InferenceOptions { MaxConcurrentMissions = 1 } });
                        foreach (Captain captain in new[] { h.Captain, other })
                        { captain.Runtime = AgentRuntimeEnum.ApiEndpoint; captain.ModelEndpointId = endpoint.Id; await CaptainAssignmentFixture.UpdateAsync(h.Db, captain); }
                    }
                    Mission held = await h.Db.Missions.CreateAsync(new Mission("other process") { VesselId = gate == "vessel" ? h.Vessel.Id : null, VoyageId = gate == "vessel" ? verify.VoyageId : null });
                    AssertTrue(await h.Db.Captains.TryClaimAsync(other.Id, held.Id, null));
                    await h.Admiral.HandleProcessExitAsync(4242, 0, h.Captain.Id, verify.Id);
                    AssertEqual(MissionStatusEnum.Verifying, (await h.Db.Missions.ReadAsync(verify.Id))!.Status, gate + " " + String.Join(";", (await h.Db.Events.EnumerateByMissionAsync(verify.Id)).Where(e => e.EventType == StageCheckEvents.EventType).Select(e => e.Payload)));
                    AssertEqual(1, headChecks, "one mechanical check"); AssertEqual(0, h.Launches);
                    await h.Admiral.HandleProcessExitAsync(4242, 0, h.Captain.Id, verify.Id);
                    AssertEqual(1, headChecks, "waiting reuses versioned persisted evidence");
                    await h.FinishAsync(held);
                    AdmiralService restarted = h.NewAdmiral(); await h.Db.Captains.ReconcileMissionSlotsAsync();
                    await restarted.HealthCheckAsync();
                    Mission resumed = (await h.Db.Missions.ReadAsync(verify.Id))!;
                    AssertEqual(MissionStatusEnum.InProgress, resumed.Status, gate);
                    AssertEqual(8001, resumed.ProcessId); AssertEqual(1, resumed.StallRecoveryAttempts);
                    AssertEqual(1, headChecks); AssertEqual(1, h.Launches);
                    AssertContains("broken build", resumed.Description!);
                });
            Add("idle_captain_restart_replays_unfinished_verification", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                await h.MarkVerifyingAsync();
                await h.Db.Captains.ReconcileMissionSlotsAsync();
                AssertEqual(CaptainStateEnum.Idle, (await h.Db.Captains.ReadAsync(h.Captain.Id))!.State);
                await h.NewAdmiral().HealthCheckAsync();
                AssertEqual(MissionStatusEnum.Complete, (await h.Db.Missions.ReadAsync(h.Mission.Id))!.Status);
                AssertEqual(0, h.Launches);
            });
            Add("recall_cancels_verification_with_no_execution_slots", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                await h.MarkVerifyingAsync(); await h.Captains.RecallAsync(h.Captain.Id);
                AssertEqual(MissionStatusEnum.Cancelled, (await h.Db.Missions.ReadAsync(h.Mission.Id))!.Status);
                await h.NewAdmiral().HealthCheckAsync(); AssertEqual(0, h.Launches);
            });
            Add("late_check_result_preserves_cancellation_and_sibling", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
                h.Commands.Handler = async _ => { entered.TrySetResult(); await finish.Task; return new HostCommandResult(); };
                Task exit = h.Admiral.HandleProcessExitAsync(4242, 0, h.Captain.Id, h.Mission.Id);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                try
                {
                    Mission current = (await h.Db.Missions.ReadAsync(h.Mission.Id))!;
                    current.Status = MissionStatusEnum.Cancelled; await h.Db.Missions.UpdateAsync(current);
                    await h.Db.Captains.TryReleaseFromMissionAsync(h.Captain.Id, current.Id);
                    Mission sibling = await h.Db.Missions.CreateAsync(new Mission("new work"));
                    AssertTrue(await h.Db.Captains.TryClaimAsync(h.Captain.Id, sibling.Id, null));
                    finish.TrySetResult(); await exit;
                    AssertEqual(MissionStatusEnum.Cancelled, (await h.Db.Missions.ReadAsync(current.Id))!.Status);
                    AssertEqual(sibling.Id, (await h.Db.Captains.ReadAsync(h.Captain.Id))!.ActiveMissionIds.Single());
                }
                finally { finish.TrySetResult(); await exit; }
            });
            Add("stale_cached_check_is_not_reused_for_a_new_version", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                Mission verify = await h.MarkVerifyingAsync();
                await h.Db.Events.CreateAsync(StageCheckEvents.Build(verify, h.Captain, new StageCheckResult { Status = StageCheckStatusEnum.Failed, FailureMessage = "stale failure" }));
                verify.Description += " updated"; await h.Db.Missions.UpdateAsync(verify);
                int commands = 0;
                h.Commands.Handler = request => { if (request.Arguments.Any(a => a.Contains("verify-fixture"))) commands++; return Task.FromResult(new HostCommandResult()); };
                await h.Admiral.HandleProcessExitAsync(4242, 0, h.Captain.Id, verify.Id);
                AssertEqual(1, commands); AssertEqual(MissionStatusEnum.Complete, (await h.Db.Missions.ReadAsync(verify.Id))!.Status);
                AssertEqual(0, h.Launches);
            });
            Add("exhausted_repair_budget_fails_without_reclaiming_capacity", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                h.Settings.MaxRecoveryAttempts = 0;
                Mission verify = await h.MarkVerifyingAsync();
                await h.Db.Events.CreateAsync(StageCheckEvents.Build(verify, h.Captain, new StageCheckResult { Status = StageCheckStatusEnum.Failed, FailureMessage = "confirmed defect" }));
                await h.Admiral.HandleProcessExitAsync(4242, 0, h.Captain.Id, verify.Id);
                AssertEqual(MissionStatusEnum.Failed, (await h.Db.Missions.ReadAsync(verify.Id))!.Status);
                AssertEqual(0, (await h.Db.Captains.ReadAsync(h.Captain.Id))!.ActiveMissionCount);
                AssertEqual(0, h.Launches);
            });
            Add("verification_release_preserves_an_exclusive_session", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                Mission verify = await h.MarkVerifyingAsync();
                Captain planning = (await h.Db.Captains.ReadAsync(h.Captain.Id))!;
                AssertTrue(await h.Db.Captains.TryClaimExclusiveAsync(planning.Id, "planning-session", CaptainStateEnum.Planning));
                AssertTrue(await h.Db.Captains.TryUpdateExclusiveAsync(planning.Id, "planning-session", CaptainStateEnum.Planning, 9020));
                planning = (await h.Db.Captains.ReadAsync(planning.Id))!; planning.RecoveryAttempts = 2;
                await CaptainAssignmentFixture.UpdateAsync(h.Db, planning);
                AssertTrue(await h.Db.Captains.ReleaseVerificationCapacityAsync(h.Captain.Id, verify.Id, 4242, verify.LastUpdateUtc));
                Captain stored = (await h.Db.Captains.ReadAsync(h.Captain.Id))!;
                AssertEqual(CaptainStateEnum.Planning, stored.State); AssertEqual("planning-session", stored.CurrentSessionId);
                AssertNull(stored.ActiveMissionIds.SingleOrDefault()); AssertEqual(9020, stored.ProcessId); AssertEqual(2, stored.RecoveryAttempts);
                AssertFalse(await h.Missions.TryAdmitVerificationResumeAsync(verify, h.Captain));
            });
            Add("public_cancel_releases_only_verification_ownership", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                Mission verify = await h.MarkVerifyingAsync();
                Mission sibling = await h.Db.Missions.CreateAsync(new Mission("sibling during cancel"));
                AssertTrue(await h.Db.Captains.TryClaimAsync(h.Captain.Id, sibling.Id, null));
                await h.Admiral.CancelMissionAsync(verify);
                AssertEqual(MissionStatusEnum.Cancelled, (await h.Db.Missions.ReadAsync(verify.Id))!.Status);
                AssertNull(await h.Db.Captains.ReadMissionProcessAsync(h.Captain.Id, verify.Id));
                AssertEqual(sibling.Id, (await h.Db.Captains.ReadAsync(h.Captain.Id))!.ActiveMissionIds.Single());
            });
            Add("recall_all_includes_idle_verification_captains", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                await h.MarkVerifyingAsync(); await h.Admiral.RecallAllAsync();
                AssertEqual(MissionStatusEnum.Cancelled, (await h.Db.Missions.ReadAsync(h.Mission.Id))!.Status);
                AssertNull(await h.Db.Captains.ReadMissionProcessAsync(h.Captain.Id, h.Mission.Id));
            });
            Add("recall_releases_exclusive_owner_and_retained_verification", async () =>
            {
                // Recall of a planning owner needs planning sessions, which exist only on SQLite-backed deployments.
                if (!TestDatabaseConfig.IsSqlite) return;
                using Harness h = await Harness.CreateAsync(); Mission verify = await h.MarkVerifyingAsync();
                AssertTrue(await h.Db.Captains.TryClaimExclusiveAsync(h.Captain.Id, "session", CaptainStateEnum.Planning));
                Captain owner = (await h.Db.Captains.ReadAsync(h.Captain.Id))!;
                AssertNull(owner.ForMission(verify).CurrentSessionId);
                await h.Captains.RecallAsync(h.Captain.Id);
                AssertEqual(MissionStatusEnum.Cancelled, (await h.Db.Missions.ReadAsync(verify.Id))!.Status);
                Captain stored = (await h.Db.Captains.ReadAsync(h.Captain.Id))!;
                AssertNull(stored.CurrentSessionId); AssertEqual(CaptainStateEnum.Idle, stored.State);
                AssertNull(await h.Db.Captains.ReadMissionProcessAsync(h.Captain.Id, verify.Id));
            });
            Add("recall_releases_exclusive_owner_without_a_mission", async () =>
            {
                // Recall of a planning owner needs planning sessions, which exist only on SQLite-backed deployments.
                if (!TestDatabaseConfig.IsSqlite) return;
                using Harness h = await Harness.CreateAsync();
                Captain captain = await CaptainAssignmentFixture.CreateAsync(h.Db, new Captain("exclusive refiner"));
                AssertTrue(await h.Db.Captains.TryClaimExclusiveAsync(captain.Id, "session", CaptainStateEnum.Refining));
                await h.Captains.RecallAsync(captain.Id);
                Captain stored = (await h.Db.Captains.ReadAsync(captain.Id))!;
                AssertNull(stored.CurrentSessionId); AssertEqual(CaptainStateEnum.Idle, stored.State);
            });
            return new TestSuiteDescriptor(suite, "Verification capacity and recovery", cases);
        }

        private sealed class Commands : IHostCommandExecutor
        {
            internal Func<HostCommandRequest, Task<HostCommandResult>> Handler = _ => Task.FromResult(new HostCommandResult());
            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default) => Handler(request);
        }
        private sealed class Harness : IDisposable
        {
            private TestDatabase _Fixture = null!;
            internal DatabaseDriver Db => _Fixture.Driver;
            internal ArmadaSettings Settings = new ArmadaSettings();
            internal LoggingModule Log = new LoggingModule();
            internal StubGitService Git = new StubGitService();
            internal DockService Docks = null!; internal CaptainService Captains = null!;
            internal MissionService Missions = null!; internal AdmiralService Admiral = null!;
            internal Captain Captain = null!; internal Mission Mission = null!; internal Vessel Vessel = null!;
            internal Commands Commands = new Commands(); internal int Launches;
            internal static async Task<Harness> CreateAsync()
            {
                Harness h = new Harness(); h._Fixture = await TestDatabaseHelper.CreateDatabaseAsync(); h.Log.Settings.EnableConsole = false;
                h.Settings.LogDirectory = TestTemp.NewDirectory("verification_capacity_logs");
                h.Docks = new DockService(h.Log, h.Db, h.Settings, h.Git);
                h.Captains = new CaptainService(h.Log, h.Db, h.Settings, h.Git, h.Docks);
                h.Missions = new MissionService(h.Log, h.Db, h.Settings, h.Docks, h.Captains);
                typeof(MissionService).GetField("_LocalCommands", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(h.Missions, h.Commands);
                h.Vessel = await h.Db.Vessels.CreateAsync(new Vessel("verification", "https://example.invalid/repo") { MaxParallelVoyages = 0, MaxParallelMissionsPerVoyage = 0 });
                await h.Db.WorkflowProfiles.CreateAsync(new WorkflowProfile { Name = "checks", VesselId = h.Vessel.Id, Scope = WorkflowProfileScopeEnum.Vessel, Active = true, IsDefault = true, BuildCommand = "verify-fixture" });
                h.Captain = await CaptainAssignmentFixture.CreateAsync(h.Db, new Captain("verify", AgentRuntimeEnum.Mux) { MaxConcurrency = 1 });
                string path = TestTemp.NewDirectory("verification_capacity_dock");
                Dock dock = await h.Db.Docks.CreateAsync(new Dock(h.Vessel.Id) { WorktreePath = path, Active = true });
                Directory.CreateDirectory(Path.Combine(h.Settings.LogDirectory, "docks"));
                await File.WriteAllTextAsync(Path.Combine(h.Settings.LogDirectory, "docks", dock.Id + ".start"), "0123456789abcdef0123456789abcdef01234567");
                Voyage voyage = await h.Db.Voyages.CreateAsync(new Voyage("verification") { Status = VoyageStatusEnum.InProgress });
                h.Mission = await h.Db.Missions.CreateAsync(new Mission("verify", "produce a report") { VesselId = h.Vessel.Id, VoyageId = voyage.Id, DockId = dock.Id, Mode = MissionModeEnum.Research });
                AssertTrue(await h.Db.Captains.TryClaimAsync(h.Captain.Id, h.Mission.Id, dock.Id));
                h.Mission.Status = MissionStatusEnum.InProgress; h.Mission.CaptainId = h.Captain.Id; h.Mission.StartedUtc = DateTime.UtcNow; h.Mission.ProcessId = 4242;
                await h.Db.Missions.UpdateAsync(h.Mission); await h.Db.Captains.TrySetProcessAsync(h.Captain.Id, h.Mission.Id, 4242);
                h.Captains.OnLaunchAgent = (_, _, _) => { Interlocked.Increment(ref h.Launches); return Task.FromResult(8001); };
                h.Admiral = h.NewAdmiral(); return h;
            }
            internal AdmiralService NewAdmiral() => new AdmiralService(Log, Db, Settings, Captains, Missions, new VoyageService(Log, Db), Docks) { OnIsProcessTracked = _ => true };
            internal async Task<Mission> MarkVerifyingAsync()
            {
                AssertTrue(await Db.Missions.TryClaimProcessExitAsync(Mission.Id, 4242));
                Mission current = (await Db.Missions.ReadAsync(Mission.Id))!;
                current.Status = MissionStatusEnum.Verifying; await Db.Missions.UpdateAsync(current);
                current = (await Db.Missions.ReadAsync(current.Id))!;
                AssertTrue(await Db.Captains.ReleaseVerificationCapacityAsync(Captain.Id, current.Id, 4242, current.LastUpdateUtc)); return current;
            }
            internal async Task FinishAsync(Mission mission)
            {
                Mission current = (await Db.Missions.ReadAsync(mission.Id))!;
                current.Status = MissionStatusEnum.Complete; await Db.Missions.UpdateAsync(current);
                await Db.Captains.TryReleaseFromMissionAsync(current.CaptainId!, current.Id);
            }
            public void Dispose()
            {
                foreach (Dock dock in Db.Docks.EnumerateAsync().GetAwaiter().GetResult()) TestTemp.TryDelete(dock.WorktreePath!);
                TestTemp.TryDelete(Settings.LogDirectory); _Fixture.Dispose();
            }
        }
    }
}
