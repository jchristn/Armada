namespace Test.Shared.Suites.Services
{
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

    /// <summary>One selection decision drives dispatch, diagnostics and non-persisting preflight.</summary>
    public sealed class CaptainAdmissionParitySuite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            const string suite = "Services.CaptainAdmissionParity";
            List<TestCaseDescriptor> cases = new();
            void Add(string id, Func<Task> test) => cases.Add(new TestCaseDescriptor(suite, id, id, _ => test()));
            foreach (bool strict in new[] { true, false })
                Add("unresolved_override_busy_" + strict, async () =>
                {
                    using Harness h = await Harness.CreateAsync();
                    Captain pinned = await h.Captain("pinned"); Captain fallback = await h.Captain("fallback");
                    await h.Occupy(pinned); await h.Override(pinned, strict);
                    MissionAssignmentBlocker blocker = await h.ReadOnlyBlocker();
                    AssertEqual(strict ? MissionAssignmentBlockerReasonEnum.CaptainConcurrencyLimit : MissionAssignmentBlockerReasonEnum.AwaitingDispatch, blocker.Reason);
                    AssertEqual(!strict, await h.Service.IsAdmissionReadyForMonitoringAsync(h.Mission));
                    AssertEqual(!strict, await h.Service.TryAssignAsync(h.Mission, h.Vessel));
                    if (!strict) AssertEqual(fallback.Id, (await h.Db.Missions.ReadAsync(h.Mission.Id))!.CaptainId);
                    else
                    {
                        AssertTrue(blocker.BlockingMissionIds.Count > 0);
                        foreach (string id in blocker.BlockingMissionIds)
                            AssertEqual(pinned.Id, (await h.Db.Missions.ReadAsync(id))!.CaptainId);
                    }
                });
            Add("strict_free_override_bypasses_ordinary_persona_and_tier", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                Captain pinned = await h.Captain("operator choice", tier: CaptainTierEnum.Economy, allowed: "Judge");
                await h.Captain("premium alternative", tier: CaptainTierEnum.Premium);
                h.Mission.Tier = CaptainTierEnum.Premium; await h.Db.Missions.UpdateAsync(h.Mission); await h.Override(pinned, true);
                AssertEqual(MissionAssignmentBlockerReasonEnum.AwaitingDispatch, (await h.ReadOnlyBlocker()).Reason);
                AssertTrue(await h.Service.IsAdmissionReadyForMonitoringAsync(h.Mission));
                AssertTrue(await h.Service.TryAssignAsync(h.Mission, h.Vessel));
                AssertEqual(pinned.Id, (await h.Db.Missions.ReadAsync(h.Mission.Id))!.CaptainId);
            });
            Add("default_captain_uses_the_same_bypass_as_dispatch", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                Captain preferred = await h.Captain("persona default", tier: CaptainTierEnum.Economy, allowed: "Judge");
                await h.Captain("premium alternative", tier: CaptainTierEnum.Premium);
                Persona persona = await h.Db.Personas.CreateAsync(new Persona("ParityRole", "persona.worker") { DefaultCaptainId = preferred.Id });
                h.Mission.Persona = persona.Name; h.Mission.Tier = CaptainTierEnum.Premium; await h.Db.Missions.UpdateAsync(h.Mission);
                AssertEqual(MissionAssignmentBlockerReasonEnum.AwaitingDispatch, (await h.ReadOnlyBlocker()).Reason);
                AssertTrue(await h.Service.TryAssignAsync(h.Mission, h.Vessel)); AssertEqual(preferred.Id, (await h.Db.Missions.ReadAsync(h.Mission.Id))!.CaptainId);
            });
            Add("busy_default_falls_back_without_becoming_a_pin", async () =>
            {
                using Harness h = await Harness.CreateAsync(); Captain preferred = await h.Captain("default"); Captain fallback = await h.Captain("fallback");
                await h.Occupy(preferred);
                Persona persona = await h.Db.Personas.CreateAsync(new Persona("ParityRole", "persona.worker") { DefaultCaptainId = preferred.Id });
                h.Mission.Persona = persona.Name; await h.Db.Missions.UpdateAsync(h.Mission);
                AssertEqual(MissionAssignmentBlockerReasonEnum.AwaitingDispatch, (await h.ReadOnlyBlocker()).Reason);
                AssertTrue(await h.Service.TryAssignAsync(h.Mission, h.Vessel)); AssertEqual(fallback.Id, (await h.Db.Missions.ReadAsync(h.Mission.Id))!.CaptainId);
                AssertNull((await h.Db.Missions.ReadAsync(h.Mission.Id))!.RequestedCaptainId);
            });
            foreach (bool wait in new[] { true, false })
                Add("priority_availability_" + wait, async () =>
                {
                    using Harness h = await Harness.CreateAsync();
                    Captain first = await h.Captain("first", runtime: AgentRuntimeEnum.Codex); Captain second = await h.Captain("second", runtime: AgentRuntimeEnum.ClaudeCode);
                    await h.Occupy(first);
                    Persona persona = await h.Db.Personas.CreateAsync(new Persona("ParityRole", "persona.worker")
                    {
                        PriorityAvailability = wait ? PersonaPriorityAvailabilityEnum.WaitForFirstPriority : PersonaPriorityAvailabilityEnum.FallThrough,
                        CaptainPriorities = new() { new PersonaCaptainPriority { Runtime = first.Runtime }, new PersonaCaptainPriority { Runtime = second.Runtime } }
                    });
                    h.Mission.Persona = persona.Name; await h.Db.Missions.UpdateAsync(h.Mission);
                    AssertEqual(wait ? MissionAssignmentBlockerReasonEnum.CaptainConcurrencyLimit : MissionAssignmentBlockerReasonEnum.AwaitingDispatch, (await h.ReadOnlyBlocker()).Reason);
                    AssertEqual(!wait, await h.Service.TryAssignAsync(h.Mission, h.Vessel));
                    if (!wait) AssertEqual(second.Id, (await h.Db.Missions.ReadAsync(h.Mission.Id))!.CaptainId);
                });
            Add("strict_runtime_gate_does_not_consider_unrelated_free_captains", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                Captain pinned = await h.Captain("pinned runtime", runtime: AgentRuntimeEnum.Mux); Captain occupied = await h.Captain("busy runtime", runtime: AgentRuntimeEnum.Mux);
                await h.Occupy(occupied); await h.Captain("unrelated runtime"); await h.Override(pinned, true);
                h.Settings.Agents = new() { new AgentSettings { Runtime = AgentRuntimeEnum.Mux, MaxConcurrent = 1 } };
                AssertEqual(MissionAssignmentBlockerReasonEnum.RuntimeMissionLimit, (await h.ReadOnlyBlocker()).Reason);
                AssertFalse(await h.Service.IsAdmissionReadyForMonitoringAsync(h.Mission)); AssertFalse(await h.Service.TryAssignAsync(h.Mission, h.Vessel));
            });
            Add("full_eligible_captain_is_named_when_an_ineligible_one_is_free", async () =>
            {
                using Harness h = await Harness.CreateAsync(); Captain eligible = await h.Captain("worker", allowed: "Worker"); await h.Occupy(eligible);
                await h.Captain("judge only", allowed: "Judge");
                MissionAssignmentBlocker blocker = await h.ReadOnlyBlocker(); AssertEqual(MissionAssignmentBlockerReasonEnum.CaptainConcurrencyLimit, blocker.Reason);
                AssertTrue(blocker.Advice.Any(a => a.Action == AssignmentBlockerActionEnum.AddCaptain)); AssertFalse(await h.Service.TryAssignAsync(h.Mission, h.Vessel));
            });
            Add("working_captain_with_free_but_ineligible_slots_has_an_explanation", async () =>
            {
                using Harness h = await Harness.CreateAsync(); Captain captain = await h.Captain("judge only", allowed: "Judge", max: 2); await h.Occupy(captain);
                MissionAssignmentBlocker blocker = await h.ReadOnlyBlocker(); AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker.Reason);
                AssertEqual(false, blocker.Captains.Single().EligibleForMission); AssertContains("persona not allowed", blocker.Captains.Single().Detail);
                AssertFalse(await h.Service.TryAssignAsync(h.Mission, h.Vessel));
            });
            Add("effective_server_ceiling_is_not_a_configuration_write", async () =>
            {
                using Harness h = await Harness.CreateAsync(); Captain captain = await h.Captain("stored eight", max: 8); await h.Occupy(captain); await h.Occupy(captain);
                h.Settings.Concurrency.MaxCaptainConcurrency = 2; await h.Captain("judge", allowed: "Judge");
                MissionAssignmentBlocker blocker = await h.ReadOnlyBlocker(); AssertEqual(MissionAssignmentBlockerReasonEnum.CaptainConcurrencyLimit, blocker.Reason);
                AssertContains("limit 2", blocker.Summary); AssertEqual(8, (await h.Db.Captains.ReadAsync(captain.Id))!.MaxConcurrency);
            });
            Add("inactive_requested_captain_does_not_fall_back", async () =>
            {
                using Harness h = await Harness.CreateAsync(); Captain disabled = await h.Captain("inactive"); disabled.Active = false; await h.Db.Captains.UpdateAsync(disabled, updateActive: true);
                await h.Captain("alternative"); h.Mission.RequestedCaptainId = disabled.Id; await h.Db.Missions.UpdateAsync(h.Mission);
                AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, (await h.ReadOnlyBlocker()).Reason); AssertFalse(await h.Service.TryAssignAsync(h.Mission, h.Vessel));
            });
            Add("launch_backoff_is_not_bypassed_by_an_untried_ineligible_captain", async () =>
            {
                using Harness h = await Harness.CreateAsync(); await h.Captain("worker", allowed: "Worker"); await h.Captain("untried judge", allowed: "Judge");
                int attempts = 0; h.Captains.OnLaunchAgent = (_, _, _) => { attempts++; throw new InvalidOperationException("temporary launch failure"); };
                AssertFalse(await h.Service.TryAssignAsync(h.Mission, h.Vessel)); AssertEqual(1, attempts);
                AssertFalse(await h.Service.TryAssignAsync(h.Mission, h.Vessel)); AssertEqual(1, attempts);
            });
            Add("untried_working_captain_with_a_free_slot_can_bypass_backoff", async () =>
            {
                using Harness h = await Harness.CreateAsync(); Captain failed = await h.Captain("first");
                int attempts = 0; h.Captains.OnLaunchAgent = (_, _, _) => { attempts++; throw new InvalidOperationException("temporary launch failure"); };
                AssertFalse(await h.Service.TryAssignAsync(h.Mission, h.Vessel));
                Captain fallback = await h.Captain("working alternative", max: 2); await h.Occupy(fallback);
                h.Captains.OnLaunchAgent = (_, _, _) => Task.FromResult(8001);
                AssertTrue(await h.Service.TryAssignAsync(h.Mission, h.Vessel)); AssertEqual(fallback.Id, (await h.Db.Missions.ReadAsync(h.Mission.Id))!.CaptainId);
            });
            Add("diagnostics_do_not_record_worker_envelope_decisions", async () =>
            {
                using Harness h = await Harness.CreateAsync(); await h.Captain("ready");
                h.Mission.WorkerEnvelope = new WorkerEnvelope { MinServedContextTokens = 32768 };
                await h.Db.Missions.UpdateAsync(h.Mission);
                await h.ReadOnlyBlocker(); await h.Service.IsAdmissionReadyForMonitoringAsync(h.Mission);
                AssertEqual(0, (await h.Db.Events.EnumerateByMissionAsync(h.Mission.Id)).Count); AssertEqual(0, h.Launches);
                AssertFalse(await h.Service.TryAssignAsync(h.Mission, h.Vessel));
                AssertTrue((await h.Db.Events.EnumerateByMissionAsync(h.Mission.Id)).Any(e => e.EventType == WorkerEnvelopeRules.EventType));
            });
            foreach (UsageSelectionModeEnum mode in new[] { UsageSelectionModeEnum.Observe, UsageSelectionModeEnum.Enforce })
            foreach (CaptainSelectionStrategyEnum strategy in Enum.GetValues<CaptainSelectionStrategyEnum>())
            Add("scoped_dispatch_" + mode + "_" + strategy, async () =>
            {
                using Harness h = await Harness.CreateAsync();
                TenantMetadata tenant = await h.Db.Tenants.CreateAsync(new TenantMetadata("observe-dispatch"));
                UserMaster owner = await h.Db.Users.CreateAsync(new UserMaster(tenant.Id, "observe@example.invalid", "fixture-password"));
                h.Mission.TenantId = tenant.Id; h.Mission.UserId = owner.Id;
                await h.Db.Missions.UpdateAsync(h.Mission);
                h.Settings.UsageSelection.Strategy = strategy;
                h.Settings.UsageSelection.Mode = mode;
                h.Settings.UsageCapture.Enabled = true;
                Captain first = await h.Db.Captains.CreateAsync(new Captain("legacy first") { TenantId = tenant.Id, UserId = owner.Id, Model = "opus", LastUpdateUtc = DateTime.UtcNow.AddMinutes(-10), UsageCaptureEnabled = true });
                Captain second = await h.Db.Captains.CreateAsync(new Captain("more headroom") { TenantId = tenant.Id, UserId = owner.Id, Model = "opus", LastUpdateUtc = DateTime.UtcNow.AddMinutes(-5), UsageCaptureEnabled = true });
                async Task Capture(Captain captain, double used)
                {
                    string host = "host-" + captain.Id;
                    string revision = UsageAccountResolver.ConfigurationRevision(host, captain.Runtime, null);
                    string context = UsageAccountResolver.CaptureContextKey(UsageAccountResolver.GroupKey(tenant.Id, host, captain.Runtime, null), revision);
                    await h.Db.UsageMemberships.CreateAsync(new UsageAccountMembership { TenantId = tenant.Id, CaptainId = captain.Id, HostId = host,
                        AccountKey = "opaque-account", Runtime = captain.Runtime, Provider = "anthropic", ConfigurationRevision = revision });
                    UsageSnapshot snapshot = new UsageSnapshot { TenantId = tenant.Id, HostId = host, AccountKey = "opaque-account", Runtime = captain.Runtime,
                        Source = UsageCaptureSourceEnum.OAuthEndpoint, Status = UsageSnapshotStatusEnum.Ok, Provider = "anthropic", ProbeStartedUtc = DateTime.UtcNow.AddSeconds(-1),
                        WindowsJson = UsageWindowsJson.Serialize(new List<UsageWindow> { new UsageWindow { Name = "five-hour", UsedPercent = used } }) };
                    await h.Db.UsageSnapshots.CreateAsync(snapshot, new List<string> { captain.Id });
                    await h.Db.UsageSnapshots.UpsertLatestStateAsync(new UsageLatestState { TenantId = tenant.Id, HostId = host, AccountKey = "opaque-account", Runtime = captain.Runtime,
                        Provider = "anthropic", LastGoodSnapshotId = snapshot.Id, LastGoodCapturedUtc = snapshot.CapturedUtc, LastGoodReceivedUtc = snapshot.ReceivedUtc,
                        LastGoodStatus = snapshot.Status, LastGoodWindowsJson = snapshot.WindowsJson, CaptureContextKey = context });
                }
                await Capture(first, 80); await Capture(second, 20);
                Captain actualCaptain = mode == UsageSelectionModeEnum.Enforce && strategy == CaptainSelectionStrategyEnum.MostRemainingAllowance ? second : first;
                h.Service.UsageSelectionLocalHostId = "host-" + actualCaptain.Id;
                Captain proposed = strategy == CaptainSelectionStrategyEnum.MostRemainingAllowance ? second : first;
                CaptainSelectionPreview preview = await h.Service.PreviewCaptainSelectionAsync(h.Mission);
                AssertEqual(actualCaptain.Id, preview.ActualCaptainId); AssertEqual(proposed.Id, preview.HypotheticalCaptainId);
                AssertEqual(0L, (await h.Db.CaptainSelections.EnumerateByMissionAsync(tenant.Id, h.Mission.Id)).TotalRecords);
                h.Captains.OnLaunchAgent = async (captain, mission, dock) =>
                {
                    CaptainSelectionDecision committed = (await h.Db.CaptainSelections.ReadCurrentDecisionAsync(tenant.Id, mission.Id))!;
                    AssertEqual(actualCaptain.Id, committed.SelectedCaptainId, "history must exist before the process starts");
                    AssertEqual(mode == UsageSelectionModeEnum.Observe ? proposed.Id : null, committed.HypotheticalCaptainId);
                    h.Launches++; return 8001;
                };
                AssertTrue(await h.Service.TryAssignAsync(h.Mission, h.Vessel));
                AssertEqual(1, h.Launches);
                CaptainSelectionDecision decision = (await h.Db.CaptainSelections.ReadCurrentDecisionAsync(tenant.Id, h.Mission.Id))!;
                AssertEqual(actualCaptain.Id, decision.SelectedCaptainId); AssertEqual(mode == UsageSelectionModeEnum.Observe ? proposed.Id : null, decision.HypotheticalCaptainId);
                AssertEqual(2, decision.CandidateCount);
                AssertEqual(1L, (await h.Db.CaptainSelections.EnumerateExecutionEventsAsync(tenant.Id, decision.Id)).TotalRecords);
                UsageBudgetReservation reservation = (await h.Db.CaptainSelections.ReadReservationsAsync(tenant.Id, "host-" + actualCaptain.Id, actualCaptain.Runtime.ToString(), "opaque-account")).Single();
                AssertTrue(reservation.ExecutionStartedUtc.HasValue);
                AssertFalse(await h.Db.CaptainSelections.TryTransitionReservationAsync(tenant.Id, reservation.Id, reservation.Revision, UsageReservationStateEnum.Active,
                    UsageReservationStateEnum.Released, DateTime.UtcNow.AddSeconds(1), "NotStarted"), "a launched attempt cannot be released as unstarted");
                AssertEqual(0, (await h.Db.CaptainSelections.ReadReservationsAsync(tenant.Id, "host-" + (actualCaptain.Id == first.Id ? second.Id : first.Id), actualCaptain.Runtime.ToString(), "opaque-account")).Count);
            });
            return new TestSuiteDescriptor(suite, "Captain admission parity", cases);
        }

        private sealed class Harness : IDisposable
        {
            private TestDatabase _Fixture = null!; internal DatabaseDriver Db => _Fixture.Driver;
            internal ArmadaSettings Settings = new(); internal CaptainService Captains = null!; internal MissionService Service = null!;
            internal Mission Mission = null!; internal Vessel Vessel = null!; internal int Launches;
            internal static async Task<Harness> CreateAsync()
            {
                Harness h = new(); h._Fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                h.Settings.DocksDirectory = TestTemp.NewDirectory("parity_docks"); h.Settings.ReposDirectory = TestTemp.NewDirectory("parity_repos");
                LoggingModule log = new(); log.Settings.EnableConsole = false; DirCreatingGitService git = new(); DockService docks = new(log, h.Db, h.Settings, git);
                h.Captains = new(log, h.Db, h.Settings, git, docks); h.Captains.OnLaunchAgent = (_, _, _) => { h.Launches++; return Task.FromResult(8001); };
                h.Service = new(log, h.Db, h.Settings, docks, h.Captains, git: git);
                h.Vessel = await h.Db.Vessels.CreateAsync(new Vessel("parity", "https://example.invalid/repo") { MaxParallelVoyages = 0, MaxParallelMissionsPerVoyage = 0 });
                h.Mission = await h.Db.Missions.CreateAsync(new Mission("parity mission", "produce a report") { VesselId = h.Vessel.Id, Persona = "Worker", Mode = MissionModeEnum.Research }); return h;
            }
            internal Task<Captain> Captain(string name, AgentRuntimeEnum runtime = AgentRuntimeEnum.ClaudeCode, CaptainTierEnum? tier = null, string? allowed = null, int max = 1)
                => Db.Captains.CreateAsync(new Captain(name, runtime) { Tier = tier, MaxConcurrency = max, AllowedPersonas = allowed == null ? null : JsonSerializer.Serialize(new[] { allowed }) });
            internal async Task Occupy(Captain captain)
            { Mission held = await Db.Missions.CreateAsync(new Mission("held")); AssertTrue(await Db.Captains.TryClaimAsync(captain.Id, held.Id, null)); }
            internal async Task Override(Captain captain, bool strict)
            {
                Voyage voyage = await Db.Voyages.CreateAsync(new Voyage("override") { CaptainOverridesJson = JsonSerializer.Serialize(new[] { new CaptainAssignmentOverride { Persona = "*", CaptainId = captain.Id, StrictCaptain = strict } }) });
                Mission.VoyageId = voyage.Id; await Db.Missions.UpdateAsync(Mission);
            }
            internal async Task<MissionAssignmentBlocker> ReadOnlyBlocker()
            {
                Mission current = (await Db.Missions.ReadAsync(Mission.Id))!; DateTime version = current.LastUpdateUtc;
                MissionAssignmentBlocker blocker = (await Service.GetAssignmentBlockerAsync(current))!;
                Mission after = (await Db.Missions.ReadAsync(current.Id))!;
                AssertEqual(version, after.LastUpdateUtc); AssertEqual(current.RequestedCaptainId, after.RequestedCaptainId);
                AssertEqual(current.Tier, after.Tier); AssertEqual(0, (await Db.Docks.EnumerateAsync()).Count); return blocker;
            }
            public void Dispose() { TestTemp.TryDelete(Settings.DocksDirectory); TestTemp.TryDelete(Settings.ReposDirectory); _Fixture.Dispose(); }
        }
    }
}
