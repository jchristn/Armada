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
            Add("enforced_preferred_fallback_requires_permission_and_preserves_strict_assignment", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                TenantMetadata tenant = await h.Db.Tenants.CreateAsync(new TenantMetadata("recovery-routing"));
                UserMaster owner = await h.Db.Users.CreateAsync(new UserMaster(tenant.Id, "recovery-routing@example.invalid", "fixture-password"));
                Captain preferred = await h.Db.Captains.CreateAsync(new Captain("preferred") { TenantId = tenant.Id, UserId = owner.Id });
                Captain alternative = await h.Db.Captains.CreateAsync(new Captain("alternative") { TenantId = tenant.Id, UserId = owner.Id });
                Mission occupied = await h.Db.Missions.CreateAsync(new Mission("occupied") { TenantId = tenant.Id, UserId = owner.Id });
                AssertTrue(await h.Db.Captains.TryClaimAsync(preferred.Id, occupied.Id, null));
                h.Mission.TenantId = tenant.Id; h.Mission.UserId = owner.Id; h.Mission.RequestedCaptainId = preferred.Id;
                await h.Db.Missions.UpdateAsync(h.Mission); h.Settings.UsageSelection.Mode = UsageSelectionModeEnum.Enforce;
                CaptainSelectionPreview waiting = await h.Service.PreviewCaptainSelectionAsync(h.Mission);
                AssertNull(waiting.ActualCaptainId);
                AssertEqual("FallbackPermissionRequired", waiting.Candidates.Objects.Single(c => c.CaptainId == alternative.Id).ReasonCode);
                AssertNotNull(await h.Db.CaptainSelections.TryAuthorizeRecoveryAsync(tenant.Id, h.Mission.Id, owner.Id,
                    new UsageSelectionRecoveryRequest { AllowFallback = true, Reason = "Allow a qualified alternative" }));
                CaptainSelectionPreview permitted = await h.Service.PreviewCaptainSelectionAsync((await h.Db.Missions.ReadAsync(h.Mission.Id))!);
                AssertEqual(alternative.Id, permitted.ActualCaptainId);
                preferred.Active = false; await h.Db.Captains.UpdateAsync(preferred, updateActive: true);
                AssertEqual(alternative.Id, (await h.Service.PreviewCaptainSelectionAsync((await h.Db.Missions.ReadAsync(h.Mission.Id))!)).ActualCaptainId);
                preferred.Active = true; await h.Db.Captains.UpdateAsync(preferred, updateActive: true);
                Voyage strict = await h.Db.Voyages.CreateAsync(new Voyage("strict recovery") { TenantId = tenant.Id, UserId = owner.Id,
                    CaptainOverridesJson = JsonSerializer.Serialize(new[] { new CaptainAssignmentOverride { Persona = "*", CaptainId = preferred.Id, StrictCaptain = true } }) });
                h.Mission.VoyageId = strict.Id; await h.Db.Missions.UpdateAsync(h.Mission);
                CaptainSelectionPreview pinned = await h.Service.PreviewCaptainSelectionAsync(h.Mission);
                AssertNull(pinned.ActualCaptainId);
                AssertEqual("StrictCaptainOverride", pinned.Candidates.Objects.Single(c => c.CaptainId == alternative.Id).ReasonCode);
                AssertEqual(0, h.Launches);
            });
            Add("usage_preview_mirrors_retry_backoff_and_untried_alternative", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                TenantMetadata tenant = await h.Db.Tenants.CreateAsync(new TenantMetadata("preview-retry"));
                UserMaster owner = await h.Db.Users.CreateAsync(new UserMaster(tenant.Id, "retry@example.invalid", "fixture-password"));
                h.Mission.TenantId = tenant.Id; h.Mission.UserId = owner.Id; await h.Db.Missions.UpdateAsync(h.Mission);
                await h.Db.Captains.CreateAsync(new Captain("first") { TenantId = tenant.Id, UserId = owner.Id });
                int attempts = 0; h.Captains.OnLaunchAgent = (_, _, _) => { attempts++; throw new InvalidOperationException("fixture launch failure"); };
                AssertFalse(await h.Service.TryAssignAsync(h.Mission, h.Vessel));
                Mission current = (await h.Db.Missions.ReadAsync(h.Mission.Id))!;
                AssertEqual("LaunchRetryPending", (await h.Service.PreviewCaptainSelectionAsync(current)).ReasonCode);
                Captain fallback = await h.Db.Captains.CreateAsync(new Captain("untried alternative") { TenantId = tenant.Id, UserId = owner.Id });
                long history = (await h.Db.CaptainSelections.EnumerateByMissionAsync(tenant.Id, current.Id)).TotalRecords;
                int docks = (await h.Db.Docks.EnumerateAsync()).Count;
                CaptainSelectionPreview preview = await h.Service.PreviewCaptainSelectionAsync(current);
                AssertEqual(fallback.Id, preview.ActualCaptainId); AssertEqual(1, attempts);
                AssertEqual(history, (await h.Db.CaptainSelections.EnumerateByMissionAsync(tenant.Id, current.Id)).TotalRecords);
                AssertEqual(docks, (await h.Db.Docks.EnumerateAsync()).Count);
            });
            Add("enforced_wait_records_one_event_and_notification_per_material_state", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                TenantMetadata tenant = await h.Db.Tenants.CreateAsync(new TenantMetadata("selection-wait-event"));
                UserMaster owner = await h.Db.Users.CreateAsync(new UserMaster(tenant.Id, "wait-event@example.invalid", "fixture-password"));
                h.Mission.TenantId = tenant.Id; h.Mission.UserId = owner.Id; await h.Db.Missions.UpdateAsync(h.Mission);
                await h.Db.Captains.CreateAsync(new Captain("no evidence") { TenantId = tenant.Id, UserId = owner.Id });
                h.Settings.UsageSelection.Mode = UsageSelectionModeEnum.Enforce;
                h.Settings.UsageSelection.UnknownUsageBehavior = UsageUnknownBehaviorEnum.Wait;
                List<CaptainSelectionChange> changes = new List<CaptainSelectionChange>();
                h.Service.OnCaptainSelectionChanged = changes.Add;
                AssertFalse(await h.Service.TryAssignAsync(h.Mission, h.Vessel), "unknown usage waits");
                AssertFalse(await h.Service.TryAssignAsync((await h.Db.Missions.ReadAsync(h.Mission.Id))!, h.Vessel), "still waiting");
                AssertEqual(0, h.Launches);
                List<ArmadaEvent> events = (await h.Db.Events.EnumerateAsync(new EnumerationQuery { PageNumber = 1, PageSize = 100 })).Objects
                    .Where(e => e.EventType == MissionService.SelectionWaitingEventType && e.MissionId == h.Mission.Id).ToList();
                AssertEqual(1, events.Count, "unchanged evidence records one event");
                CaptainSelectionWaitingEvent payload = JsonSerializer.Deserialize<CaptainSelectionWaitingEvent>(events[0].Payload!)!;
                AssertEqual(UsageSelectionModeEnum.Enforce, payload.Mode);
                AssertFalse(String.IsNullOrEmpty(payload.ReasonCode), "stable reason recorded");
                AssertFalse(events[0].Payload!.Contains("opaque", StringComparison.OrdinalIgnoreCase), "no account identity in the payload");
                AssertEqual(1, changes.Count(c => c.Outcome == CaptainSelectionOutcomes.Waiting), "one invalidation for one material state");
                AssertEqual(payload.DecisionId, changes.Single().DecisionId);
            });
            Add("usage_preview_honors_placement_without_writes", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                TenantMetadata tenant = await h.Db.Tenants.CreateAsync(new TenantMetadata("preview-placement"));
                UserMaster owner = await h.Db.Users.CreateAsync(new UserMaster(tenant.Id, "preview@example.invalid", "fixture-password"));
                h.Mission.TenantId = tenant.Id; h.Mission.UserId = owner.Id;
                await h.Db.Missions.UpdateAsync(h.Mission);
                await h.Db.Captains.CreateAsync(new Captain("preview candidate") { TenantId = tenant.Id, UserId = owner.Id });
                h.Service.CanAssignMissionAsync = (_, _) => Task.FromResult(false);
                DateTime version = (await h.Db.Missions.ReadAsync(h.Mission.Id))!.LastUpdateUtc;
                CaptainSelectionPreview preview = await h.Service.PreviewCaptainSelectionAsync((await h.Db.Missions.ReadAsync(h.Mission.Id))!);
                AssertEqual("PlacementWaiting", preview.ReasonCode); AssertNull(preview.ActualCaptainId);
                AssertEqual(1L, preview.Candidates.TotalRecords);
                AssertEqual(version, (await h.Db.Missions.ReadAsync(h.Mission.Id))!.LastUpdateUtc);
                AssertEqual(0, h.Launches); AssertEqual(0, (await h.Db.Docks.EnumerateAsync()).Count);
                AssertEqual(0L, (await h.Db.CaptainSelections.EnumerateByMissionAsync(tenant.Id, h.Mission.Id)).TotalRecords);
            });
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
                List<CaptainSelectionChange> changes = new List<CaptainSelectionChange>();
                h.Service.OnCaptainSelectionChanged = changes.Add;
                AssertTrue(await h.Service.TryAssignAsync(h.Mission, h.Vessel));
                AssertEqual(1, h.Launches);
                CaptainSelectionDecision decision = (await h.Db.CaptainSelections.ReadCurrentDecisionAsync(tenant.Id, h.Mission.Id))!;
                CaptainSelectionChange change = changes.Single();
                AssertEqual(h.Mission.Id, change.MissionId); AssertEqual(tenant.Id, change.TenantId);
                AssertEqual(decision.Id, change.DecisionId); AssertEqual(CaptainSelectionOutcomes.Admitted, change.Outcome);
                h.Service.ReconsiderCaptainSelection(h.Mission);
                AssertEqual(CaptainSelectionOutcomes.RecoveryAuthorized, changes.Last().Outcome, "recovery invalidates without a decision");
                AssertNull(changes.Last().DecisionId);
                AssertEqual(actualCaptain.Id, decision.SelectedCaptainId); AssertEqual(mode == UsageSelectionModeEnum.Observe ? proposed.Id : null, decision.HypotheticalCaptainId);
                AssertEqual(2, decision.CandidateCount);
                EnumerationResult<CaptainSelectionExecutionEvent> executionEvents = await h.Db.CaptainSelections.EnumerateExecutionEventsAsync(tenant.Id, decision.Id);
                AssertEqual(1, executionEvents.Objects.Count(e => e.EventKind == "LaunchAttempted"));
                AssertEqual(1, executionEvents.Objects.Count(e => e.EventKind == "ExecutionStarted"));
                UsageBudgetReservation reservation = (await h.Db.CaptainSelections.ReadReservationsAsync(tenant.Id, "host-" + actualCaptain.Id, actualCaptain.Runtime.ToString(), "opaque-account")).Single();
                AssertTrue(reservation.ExecutionStartedUtc.HasValue);
                AssertFalse(await h.Db.CaptainSelections.TryTransitionReservationAsync(tenant.Id, reservation.Id, reservation.Revision, UsageReservationStateEnum.Active,
                    UsageReservationStateEnum.Released, DateTime.UtcNow.AddSeconds(1), "NotStarted"), "a launched attempt cannot be released as unstarted");
                AssertEqual(0, (await h.Db.CaptainSelections.ReadReservationsAsync(tenant.Id, "host-" + (actualCaptain.Id == first.Id ? second.Id : first.Id), actualCaptain.Runtime.ToString(), "opaque-account")).Count);
            });
            // M7: requirements an Architect plan sets on a slice (here its Worker envelope) go through Enforce routing: the
            // captain with more headroom that does not meet them is filtered out before usage ranking.
            Add("enforce_honors_architect_slice_requirements", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                TenantMetadata tenant = await h.Db.Tenants.CreateAsync(new TenantMetadata("architect-requirements"));
                UserMaster owner = await h.Db.Users.CreateAsync(new UserMaster(tenant.Id, "architect-req@example.invalid", "fixture-password"));
                h.Mission.TenantId = tenant.Id; h.Mission.UserId = owner.Id;
                h.Mission.WorkerEnvelope = new WorkerEnvelope { MinServedContextTokens = 32768 };
                await h.Db.Missions.UpdateAsync(h.Mission);
                h.Settings.UsageSelection.Strategy = CaptainSelectionStrategyEnum.MostRemainingAllowance;
                h.Settings.UsageSelection.Mode = UsageSelectionModeEnum.Enforce;
                h.Settings.UsageCapture.Enabled = true;
                ModelProfile wide = new ModelProfile
                {
                    TenantId = tenant.Id,
                    Identity = new ModelProfileIdentity { Runtime = "ClaudeCode", Model = "m", ServedContextTokens = 131072 },
                    Capabilities = new ModelProfileCapabilities { ToolsAvailable = true }
                };
                wide = await h.Db.ModelProfiles.CreateAsync(wide, "architect-req-" + Guid.NewGuid().ToString("N"));
                Captain eligible = await h.Db.Captains.CreateAsync(new Captain("meets the envelope") { TenantId = tenant.Id, UserId = owner.Id, Model = "opus", ModelProfileId = wide.Id, UsageCaptureEnabled = true });
                await h.Db.ModelProfiles.LinkCaptainAsync(eligible.Id, wide.Id);
                Captain roomier = await h.Db.Captains.CreateAsync(new Captain("more headroom, no profile") { TenantId = tenant.Id, UserId = owner.Id, Model = "opus", UsageCaptureEnabled = true });
                foreach (KeyValuePair<Captain, double> usage in new[] { new KeyValuePair<Captain, double>(eligible, 80), new KeyValuePair<Captain, double>(roomier, 10) })
                {
                    Captain captain = usage.Key;
                    string host = "host-" + captain.Id;
                    string revision = UsageAccountResolver.ConfigurationRevision(host, captain.Runtime, null);
                    string context = UsageAccountResolver.CaptureContextKey(UsageAccountResolver.GroupKey(tenant.Id, host, captain.Runtime, null), revision);
                    await h.Db.UsageMemberships.CreateAsync(new UsageAccountMembership { TenantId = tenant.Id, CaptainId = captain.Id, HostId = host,
                        AccountKey = "opaque-account", Runtime = captain.Runtime, Provider = "anthropic", ConfigurationRevision = revision });
                    UsageSnapshot snapshot = new UsageSnapshot { TenantId = tenant.Id, HostId = host, AccountKey = "opaque-account", Runtime = captain.Runtime,
                        Source = UsageCaptureSourceEnum.OAuthEndpoint, Status = UsageSnapshotStatusEnum.Ok, Provider = "anthropic", ProbeStartedUtc = DateTime.UtcNow.AddSeconds(-1),
                        WindowsJson = UsageWindowsJson.Serialize(new List<UsageWindow> { new UsageWindow { Name = "five-hour", UsedPercent = usage.Value } }) };
                    await h.Db.UsageSnapshots.CreateAsync(snapshot, new List<string> { captain.Id });
                    await h.Db.UsageSnapshots.UpsertLatestStateAsync(new UsageLatestState { TenantId = tenant.Id, HostId = host, AccountKey = "opaque-account", Runtime = captain.Runtime,
                        Provider = "anthropic", LastGoodSnapshotId = snapshot.Id, LastGoodCapturedUtc = snapshot.CapturedUtc, LastGoodReceivedUtc = snapshot.ReceivedUtc,
                        LastGoodStatus = snapshot.Status, LastGoodWindowsJson = snapshot.WindowsJson, CaptureContextKey = context });
                }
                h.Service.UsageSelectionLocalHostId = "host-" + eligible.Id;
                bool admitted = await h.Service.TryAssignAsync(h.Mission, h.Vessel);
                MissionAssignmentBlocker? why = admitted ? null : await h.Service.GetAssignmentBlockerAsync((await h.Db.Missions.ReadAsync(h.Mission.Id))!);
                AssertTrue(admitted, "the eligible captain is admitted: " + (why == null ? "" : why.Reason + " " + why.Summary + " " + why.SelectionReasonCode));
                AssertEqual(eligible.Id, (await h.Db.Missions.ReadAsync(h.Mission.Id))!.CaptainId, "headroom never overrides the slice's requirements");
                CaptainSelectionDecision decision = (await h.Db.CaptainSelections.ReadCurrentDecisionAsync(tenant.Id, h.Mission.Id))!;
                AssertEqual(eligible.Id, decision.SelectedCaptainId);
                // The envelope filters captains before usage ranking: the roomier captain is not even a usage candidate,
                // and the envelope decision records why it was excluded.
                List<CaptainSelectionCandidateRecord> candidates = (await h.Db.CaptainSelections.EnumerateCandidatesAsync(tenant.Id, decision.Id, pageSize: 50)).Objects;
                AssertEqual(eligible.Id, candidates.Single().CaptainId, "only the captain that meets the envelope is ranked by usage");
                AssertTrue(candidates.Single().Eligible);
                List<ArmadaEvent> events = await h.Db.Events.EnumerateByMissionAsync(h.Mission.Id);
                ArmadaEvent envelopeEvent = events.Single(e => e.EventType == WorkerEnvelopeRules.EventType);
                AssertContains(roomier.Id, envelopeEvent.Payload ?? "", "the envelope decision names the excluded captain");
                AssertContains("no model profile", envelopeEvent.Payload ?? "");
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
