namespace Test.Shared.Suites.Database
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
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Structured selection history, tenant/owner scoping, immutable evidence and reservation lifecycle on every provider.</summary>
    public sealed class CaptainSelectionSuite : IArmadaTestSuite
    {
        private const string _SuiteId = "Database.CaptainSelection";

        /// <summary>Build provider-neutral portable descriptors.</summary>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, Func<CancellationToken, Task> body) => cases.Add(new TestCaseDescriptor(_SuiteId, id, id.Replace('_', ' '), body));
            Add("aggregate_roundtrip_idempotency_policy_reuse_and_actual_model_evidence", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CaptainSelectionRecordSet records = Records();
                CaptainSelectionDecision created = await database.Driver.CaptainSelections.CreateDecisionAsync(records, token).ConfigureAwait(false);
                AssertEqual(records.Decision.Id, created.Id);
                AssertEqual(created.Id, (await database.Driver.CaptainSelections.CreateDecisionAsync(records, token).ConfigureAwait(false)).Id);
                CaptainSelectionDecision stored = (await database.Driver.CaptainSelections.ReadDecisionAsync("tenant-a", created.Id, "owner", token).ConfigureAwait(false))!;
                AssertNotNull(stored); AssertEqual("opus", stored.SelectedModel); AssertEqual(UsageSelectionModeEnum.Observe, stored.Mode);
                CaptainSelectionPolicyRevision policy = (await database.Driver.CaptainSelections.ReadPolicyAsync("tenant-a", stored.PolicyRevisionId, token).ConfigureAwait(false))!;
                AssertEqual(CaptainSelectionStrategyEnum.MostRemainingAllowance, policy.Strategy); AssertEqual(10.0, policy.ReservePercent);
                AssertEqual(UsageSelectionPolicy.AlgorithmVersion, policy.AlgorithmVersion);
                CaptainSelectionRecordSet second = Records();
                await database.Driver.CaptainSelections.CreateDecisionAsync(second, token).ConfigureAwait(false);
                AssertEqual(stored.PolicyRevisionId, second.Decision.PolicyRevisionId, "equal effective policies reuse a revision");
                CaptainSelectionExecutionEvent actual = new CaptainSelectionExecutionEvent { TenantId = "tenant-a", UserId = "owner", DecisionId = created.Id,
                    ExecutionAttemptId = created.ExecutionAttemptId, EventKind = "ActualModelReported", ActualModel = "opus-resolved", ReportingSource = "Harness", OccurredUtc = DateTime.UtcNow };
                await database.Driver.CaptainSelections.AppendExecutionEventAsync(actual, token).ConfigureAwait(false);
                EnumerationResult<CaptainSelectionExecutionEvent> events = await database.Driver.CaptainSelections.EnumerateExecutionEventsAsync("tenant-a", created.Id, "owner", token: token).ConfigureAwait(false);
                AssertEqual(1L, events.TotalRecords); AssertEqual("opus-resolved", events.Objects[0].ActualModel);
                AssertEqual(0L, (await database.Driver.CaptainSelections.EnumerateExecutionEventsAsync("tenant-a", created.Id, "other", token: token).ConfigureAwait(false)).TotalRecords);
                AssertEqual("SufficientHeadroom", (await database.Driver.CaptainSelections.ReadReasonsAsync("tenant-a", created.Id, records.Candidates[0].Id, "owner", "owner", token).ConfigureAwait(false)).Single().ReasonCode);

                AssertEqual("opus", (await database.Driver.CaptainSelections.ReadDecisionAsync("tenant-a", created.Id, "owner", token).ConfigureAwait(false))!.SelectedModel, "actual event never rewrites intended selection");
            });
            Add("tenant_and_owner_scope_precede_candidate_counts_and_pages", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CaptainSelectionRecordSet records = Records();
                CaptainSelectionCandidateRecord hidden = new CaptainSelectionCandidateRecord { TenantId = "tenant-a", UserId = "other", DecisionId = records.Decision.Id,
                    CaptainId = "hidden", Rank = 2, ConfiguredModel = "private", PrimaryReasonCode = "BelowReserve" };
                records.Candidates.Add(hidden); records.Decision.CandidateCount++;
                await database.Driver.CaptainSelections.CreateDecisionAsync(records, token).ConfigureAwait(false);
                AssertNull(await database.Driver.CaptainSelections.ReadDecisionAsync("tenant-b", records.Decision.Id, token: token).ConfigureAwait(false));
                AssertNull(await database.Driver.CaptainSelections.ReadDecisionAsync("tenant-a", records.Decision.Id, "other", token).ConfigureAwait(false));
                EnumerationResult<CaptainSelectionCandidateRecord> visible = await database.Driver.CaptainSelections.EnumerateCandidatesAsync("tenant-a", records.Decision.Id, "owner", "owner", token: token).ConfigureAwait(false);
                AssertEqual(1L, visible.TotalRecords); AssertEqual("visible", visible.Objects[0].CaptainId);
                AssertEqual(0L, (await database.Driver.CaptainSelections.EnumerateCandidatesAsync("tenant-b", records.Decision.Id, token: token).ConfigureAwait(false)).TotalRecords);
                AssertEqual(0L, (await database.Driver.CaptainSelections.EnumerateCandidatesAsync("tenant-a", records.Decision.Id, "other", token: token).ConfigureAwait(false)).TotalRecords);
                AssertEqual(2L, (await database.Driver.CaptainSelections.EnumerateCandidatesAsync("tenant-a", records.Decision.Id, pageSize: 1, token: token).ConfigureAwait(false)).TotalRecords);
                AssertEqual("hidden", (await database.Driver.CaptainSelections.EnumerateCandidatesAsync("tenant-a", records.Decision.Id, pageNumber: 2, pageSize: 1, token: token).ConfigureAwait(false)).Objects[0].CaptainId);
                AssertEqual(0, (await database.Driver.CaptainSelections.ReadWindowsAsync("tenant-b", records.Decision.Id, records.Candidates[0].Id, token: token).ConfigureAwait(false)).Count);
                AssertEqual(0, (await database.Driver.CaptainSelections.ReadWindowsAsync("tenant-a", records.Decision.Id, records.Candidates[0].Id, "owner", "other", token).ConfigureAwait(false)).Count);
            });
            Add("window_evidence_survives_absent_source_and_preserves_nulls", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CaptainSelectionRecordSet records = Records();
                await database.Driver.CaptainSelections.CreateDecisionAsync(records, token).ConfigureAwait(false);
                List<CaptainSelectionWindowRecord> windows = await database.Driver.CaptainSelections.ReadWindowsAsync("tenant-a", records.Decision.Id, records.Candidates[0].Id, "owner", "owner", token).ConfigureAwait(false);
                AssertEqual(1, windows.Count); AssertEqual("pruned-source", windows[0].SnapshotId);
                AssertEqual(40.0, windows[0].UsedPercent!.Value); AssertEqual(2.0, windows[0].ActiveReservationPercent);
                AssertNull(windows[0].ProbeStartedUtc); AssertNull(windows[0].ResetsUtc); AssertEqual("opus", windows[0].Model);
            });
            Add("reservation_compare_and_set_scope_and_transition_guards", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CaptainSelectionRecordSet records = Records(reserve: true);
                await database.Driver.CaptainSelections.CreateDecisionAsync(records, token).ConfigureAwait(false);
                UsageBudgetReservation row = records.Reservations[0]; DateTime at = DateTime.UtcNow.AddSeconds(1);
                AssertEqual(1, (await database.Driver.CaptainSelections.ReadReservationsAsync("tenant-a", "host", "ClaudeCode", "account", token).ConfigureAwait(false)).Count);
                AssertEqual(0, (await database.Driver.CaptainSelections.ReadReservationsAsync("tenant-a", "other-host", "ClaudeCode", "account", token).ConfigureAwait(false)).Count);
                AssertFalse(await database.Driver.CaptainSelections.TryTransitionReservationAsync("tenant-b", row.Id, 0, UsageReservationStateEnum.Active, UsageReservationStateEnum.Unsettled, at, "Ended", token: token).ConfigureAwait(false));
                AssertTrue(await database.Driver.CaptainSelections.TryTransitionReservationAsync("tenant-a", row.Id, 0, UsageReservationStateEnum.Active, UsageReservationStateEnum.Unsettled, at, "Ended", token: token).ConfigureAwait(false));
                AssertFalse(await database.Driver.CaptainSelections.TryTransitionReservationAsync("tenant-a", row.Id, 0, UsageReservationStateEnum.Active, UsageReservationStateEnum.Released, at, "Late", token: token).ConfigureAwait(false));
                AssertTrue(await database.Driver.CaptainSelections.TryTransitionReservationAsync("tenant-a", row.Id, 1, UsageReservationStateEnum.Unsettled, UsageReservationStateEnum.Settled, at, "Observed", "new-snapshot", token).ConfigureAwait(false));
                AssertEqual(0, (await database.Driver.CaptainSelections.ReadReservationsAsync("tenant-a", "host", "ClaudeCode", "account", token).ConfigureAwait(false)).Count);
                AssertThrows<ArgumentException>(() => database.Driver.CaptainSelections.TryTransitionReservationAsync("tenant-a", row.Id, 2, UsageReservationStateEnum.Settled, UsageReservationStateEnum.Active, at, "Invalid", token: token));
            });
            Add("retention_preserves_live_debt_and_purges_bounded_history", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CaptainSelectionRecordSet keep = Records(reserve: true); keep.Decision.EvaluatedUtc = DateTime.UtcNow.AddDays(-100);
                CaptainSelectionRecordSet remove = Records(); remove.Decision.EvaluatedUtc = keep.Decision.EvaluatedUtc;
                await database.Driver.CaptainSelections.CreateDecisionAsync(keep, token).ConfigureAwait(false);
                await database.Driver.CaptainSelections.CreateDecisionAsync(remove, token).ConfigureAwait(false);
                AssertEqual(1, await database.Driver.CaptainSelections.DeleteExpiredAsync(DateTime.UtcNow.AddDays(-90), 1, token).ConfigureAwait(false));
                AssertNotNull(await database.Driver.CaptainSelections.ReadDecisionAsync("tenant-a", keep.Decision.Id, token: token).ConfigureAwait(false));
                AssertNull(await database.Driver.CaptainSelections.ReadDecisionAsync("tenant-a", remove.Decision.Id, token: token).ConfigureAwait(false));
                await database.Driver.CaptainSelections.DeleteByTenantAsync("tenant-a", token).ConfigureAwait(false);
                AssertNull(await database.Driver.CaptainSelections.ReadDecisionAsync("tenant-a", keep.Decision.Id, token: token).ConfigureAwait(false));
            });
            Add("invalid_aggregate_rejected_without_partial_history", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CaptainSelectionRecordSet records = Records(); records.Windows[0].TenantId = "tenant-b";
                bool rejected = false;
                try { await database.Driver.CaptainSelections.CreateDecisionAsync(records, token).ConfigureAwait(false); }
                catch (ArgumentException) { rejected = true; }
                AssertTrue(rejected);
                AssertNull(await database.Driver.CaptainSelections.ReadDecisionAsync("tenant-a", records.Decision.Id, token: token).ConfigureAwait(false));
                AssertNull(await database.Driver.CaptainSelections.ReadPolicyAsync("tenant-a", records.Policy.Id, token).ConfigureAwait(false));
            });
            Add("observed_admission_claim_history_and_replay_are_atomic", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                TenantMetadata tenant = await database.Driver.Tenants.CreateAsync(new TenantMetadata("selection-atomic"), token).ConfigureAwait(false);
                UserMaster owner = await database.Driver.Users.CreateAsync(new UserMaster(tenant.Id, "selection@example.invalid", "fixture-password"), token).ConfigureAwait(false);
                Captain captain = await database.Driver.Captains.CreateAsync(new Captain("selection-atomic") { TenantId = tenant.Id, UserId = owner.Id, MaxConcurrency = 2 }, token).ConfigureAwait(false);
                Mission mission = await database.Driver.Missions.CreateAsync(new Mission("selection-atomic") { TenantId = tenant.Id, UserId = owner.Id }, token).ConfigureAwait(false);
                CaptainSelectionRecordSet records = Records(tenantId: tenant.Id, ownerId: owner.Id);
                records.Decision.MissionId = mission.Id; records.Decision.SelectedCaptainId = captain.Id; records.Candidates[0].CaptainId = captain.Id;
                CaptainSelectionAdmissionReceipt receipt = await database.Driver.CaptainSelections.TryAdmitObservedAsync(records, token: token).ConfigureAwait(false);
                AssertTrue(receipt.Claimed); AssertFalse(receipt.AlreadyCommitted);
                AssertEqual(records.Decision.Id, receipt.DecisionId);
                AssertEqual(MissionStatusEnum.Assigned, (await database.Driver.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false))!.Status);
                AssertEqual(1, (await database.Driver.Captains.ReadAsync(captain.Id, token).ConfigureAwait(false))!.ActiveMissionCount);
                receipt = await database.Driver.CaptainSelections.TryAdmitObservedAsync(records, token: token).ConfigureAwait(false);
                AssertFalse(receipt.Claimed); AssertTrue(receipt.AlreadyCommitted);
                AssertEqual(1, (await database.Driver.Captains.ReadAsync(captain.Id, token).ConfigureAwait(false))!.ActiveMissionCount);
                AssertEqual(1L, (await database.Driver.CaptainSelections.EnumerateByMissionAsync(tenant.Id, mission.Id, owner.Id, token: token).ConfigureAwait(false)).TotalRecords);
            });
            Add("failed_history_insert_rolls_back_slot_and_mission_assignment", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                TenantMetadata tenant = await database.Driver.Tenants.CreateAsync(new TenantMetadata("selection-rollback"), token).ConfigureAwait(false);
                UserMaster owner = await database.Driver.Users.CreateAsync(new UserMaster(tenant.Id, "rollback@example.invalid", "fixture-password"), token).ConfigureAwait(false);
                Captain captain = await database.Driver.Captains.CreateAsync(new Captain("selection-rollback") { TenantId = tenant.Id, UserId = owner.Id }, token).ConfigureAwait(false);
                Mission mission = await database.Driver.Missions.CreateAsync(new Mission("selection-rollback") { TenantId = tenant.Id, UserId = owner.Id }, token).ConfigureAwait(false);
                CaptainSelectionRecordSet existing = Records(tenantId: tenant.Id, ownerId: owner.Id);
                await database.Driver.CaptainSelections.CreateDecisionAsync(existing, token).ConfigureAwait(false);
                CaptainSelectionRecordSet records = Records(tenantId: tenant.Id, ownerId: owner.Id);
                records.Decision.MissionId = mission.Id; records.Decision.SelectedCaptainId = captain.Id; records.Candidates[0].CaptainId = captain.Id;
                records.Candidates[0].Id = existing.Candidates[0].Id;
                records.Windows[0].CandidateId = records.Candidates[0].Id; records.Reasons[0].CandidateId = records.Candidates[0].Id;
                CaptainSelectionAdmissionReceipt receipt = await database.Driver.CaptainSelections.TryAdmitObservedAsync(records, token: token).ConfigureAwait(false);
                AssertFalse(receipt.Claimed); AssertFalse(receipt.AlreadyCommitted);
                AssertEqual(MissionStatusEnum.Pending, (await database.Driver.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false))!.Status);
                AssertEqual(0, (await database.Driver.Captains.ReadAsync(captain.Id, token).ConfigureAwait(false))!.ActiveMissionCount);
                AssertNull(await database.Driver.CaptainSelections.ReadDecisionAsync(tenant.Id, records.Decision.Id, owner.Id, token).ConfigureAwait(false));
            });
            Add("historical_names_and_legal_long_models_survive_storage", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CaptainSelectionRecordSet records = Records();
                records.Candidates[0].CaptainName = new string('界', 1000);
                records.Candidates[0].ConfiguredModel = new string('m', 1000);
                records.Decision.SelectedModel = records.Candidates[0].ConfiguredModel;
                records.Windows[0].Model = records.Candidates[0].ConfiguredModel;
                await database.Driver.CaptainSelections.CreateDecisionAsync(records, token).ConfigureAwait(false);
                CaptainSelectionCandidateRecord candidate = (await database.Driver.CaptainSelections.EnumerateCandidatesAsync("tenant-a", records.Decision.Id, token: token).ConfigureAwait(false)).Objects.Single();
                AssertEqual(records.Candidates[0].CaptainName, candidate.CaptainName);
                AssertEqual(records.Candidates[0].ConfiguredModel, candidate.ConfiguredModel);
                AssertEqual(records.Decision.SelectedModel, (await database.Driver.CaptainSelections.ReadDecisionAsync("tenant-a", records.Decision.Id, token: token).ConfigureAwait(false))!.SelectedModel);
            });
            Add("active_capture_start_timestamp_roundtrips_and_legacy_remains_null", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                UsageSnapshot snapshot = new UsageSnapshot { TenantId = "tenant-a", HostId = "host", Runtime = AgentRuntimeEnum.ClaudeCode,
                    ProbeStartedUtc = DateTime.UtcNow.AddSeconds(-2), CapturedUtc = DateTime.UtcNow, ReceivedUtc = DateTime.UtcNow, Status = UsageSnapshotStatusEnum.Ok };
                await database.Driver.UsageSnapshots.CreateAsync(snapshot, null, token).ConfigureAwait(false);
                UsageSnapshot read = (await database.Driver.UsageSnapshots.ReadAsync(snapshot.Id, "tenant-a", token).ConfigureAwait(false))!;
                AssertEqual(snapshot.ProbeStartedUtc!.Value, read.ProbeStartedUtc!.Value);
                snapshot = new UsageSnapshot { TenantId = "tenant-a", HostId = "host", CapturedUtc = DateTime.UtcNow, ReceivedUtc = DateTime.UtcNow };
                await database.Driver.UsageSnapshots.CreateAsync(snapshot, null, token).ConfigureAwait(false);
                AssertNull((await database.Driver.UsageSnapshots.ReadAsync(snapshot.Id, "tenant-a", token).ConfigureAwait(false))!.ProbeStartedUtc);
            });
            Add("reconciliation_preserves_running_attempt_then_keeps_unproven_debt_uncertain", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                TenantMetadata tenant = await database.Driver.Tenants.CreateAsync(new TenantMetadata("selection-reconciliation"), token).ConfigureAwait(false);
                UserMaster owner = await database.Driver.Users.CreateAsync(new UserMaster(tenant.Id, "reconciliation@example.invalid", "fixture-password"), token).ConfigureAwait(false);
                Captain captain = await database.Driver.Captains.CreateAsync(new Captain("selection-reconciliation") { TenantId = tenant.Id, UserId = owner.Id }, token).ConfigureAwait(false);
                Mission mission = await database.Driver.Missions.CreateAsync(new Mission("selection-reconciliation") { TenantId = tenant.Id, UserId = owner.Id }, token).ConfigureAwait(false);
                CaptainSelectionRecordSet records = Records(reserve: true, tenantId: tenant.Id, ownerId: owner.Id);
                records.Decision.MissionId = mission.Id; records.Decision.SelectedCaptainId = captain.Id; records.Candidates[0].CaptainId = captain.Id;
                records.Reservations[0].MissionId = mission.Id; records.Reservations[0].CaptainId = captain.Id;
                AssertTrue((await database.Driver.CaptainSelections.TryAdmitObservedAsync(records, token: token).ConfigureAwait(false)).Claimed);
                AssertEqual(records.Decision.Id, (await database.Driver.CaptainSelections.ReadCurrentDecisionAsync(tenant.Id, mission.Id, owner.Id, token).ConfigureAwait(false))!.Id);
                AssertNull(await database.Driver.CaptainSelections.ReadCurrentDecisionAsync(tenant.Id, mission.Id, "other", token).ConfigureAwait(false));
                UsageSelectionReservationReconciler reconciler = new UsageSelectionReservationReconciler(database.Driver);
                DateTime now = DateTime.UtcNow.AddSeconds(1);
                AssertEqual(0, await reconciler.ReconcileAsync(now, token: token).ConfigureAwait(false));
                UsageBudgetReservation row = (await database.Driver.CaptainSelections.ReadReservationsAsync(tenant.Id, "host", "ClaudeCode", "account", token).ConfigureAwait(false)).Single();
                AssertEqual(UsageReservationStateEnum.Active, row.State);
                mission = (await database.Driver.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false))!;
                mission.Status = MissionStatusEnum.Complete;
                await database.Driver.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                AssertEqual(1, await reconciler.ReconcileAsync(now.AddSeconds(1), token: token).ConfigureAwait(false));
                row = (await database.Driver.CaptainSelections.ReadReservationsAsync(tenant.Id, "host", "ClaudeCode", "account", token).ConfigureAwait(false)).Single();
                AssertEqual(UsageReservationStateEnum.Unsettled, row.State);
                AssertEqual(0, await reconciler.ReconcileAsync(now.AddSeconds(120), token: token).ConfigureAwait(false), "elapsed reset/settlement time alone does not release debt");
                AssertEqual(1, await reconciler.ReconcileAsync(now.AddHours(2), token: token).ConfigureAwait(false));
                row = (await database.Driver.CaptainSelections.ReadReservationsAsync(tenant.Id, "host", "ClaudeCode", "account", token).ConfigureAwait(false)).Single();
                AssertEqual(UsageReservationStateEnum.Uncertain, row.State);
                AssertNull(row.SettledBySnapshotId);
            });
            Add("filtered_history_totals_and_reconciliation_pages_are_bounded", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CaptainSelectionRecordSet first = Records(reserve: true), second = Records(reserve: true);
                second.Decision.Outcome = "Waiting"; second.Decision.Trigger = "Waiting";
                await database.Driver.CaptainSelections.CreateDecisionAsync(first, token).ConfigureAwait(false);
                await database.Driver.CaptainSelections.CreateDecisionAsync(second, token).ConfigureAwait(false);
                EnumerationResult<CaptainSelectionDecision> page = await database.Driver.CaptainSelections.EnumerateHistoryAsync("tenant-a", "mission", "owner", new CaptainSelectionHistoryQuery { Outcome = "Waiting", PageSize = 1 }, token).ConfigureAwait(false);
                AssertEqual(1L, page.TotalRecords); AssertEqual(second.Decision.Id, page.Objects.Single().Id);
                List<UsageBudgetReservation> batch = await database.Driver.CaptainSelections.ReadReconciliationBatchAsync(1, token).ConfigureAwait(false);
                AssertEqual(1, batch.Count);
                AssertTrue(await database.Driver.CaptainSelections.TouchReservationAsync("tenant-a", batch[0].Id, batch[0].Revision, DateTime.UtcNow.AddMinutes(1), token).ConfigureAwait(false));
                AssertFalse(await database.Driver.CaptainSelections.TouchReservationAsync("tenant-a", batch[0].Id, batch[0].Revision, DateTime.UtcNow.AddMinutes(2), token).ConfigureAwait(false));
                AssertTrue(batch[0].Id != (await database.Driver.CaptainSelections.ReadReconciliationBatchAsync(1, token).ConfigureAwait(false)).Single().Id, "checked live rows do not starve the rest of the page");
            });
            Add("shared_account_batch_and_post_execution_capture_proof", async token =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                TenantMetadata tenant = await database.Driver.Tenants.CreateAsync(new TenantMetadata("selection-settlement"), token).ConfigureAwait(false);
                UserMaster owner = await database.Driver.Users.CreateAsync(new UserMaster(tenant.Id, "settlement@example.invalid", "fixture-password"), token).ConfigureAwait(false);
                Captain captain = await database.Driver.Captains.CreateAsync(new Captain("selection-settlement") { TenantId = tenant.Id, UserId = owner.Id, Runtime = AgentRuntimeEnum.ClaudeCode }, token).ConfigureAwait(false);
                string revision = UsageAccountResolver.ConfigurationRevision("host", captain.Runtime, null);
                string context = UsageAccountResolver.CaptureContextKey(UsageAccountResolver.GroupKey(tenant.Id, "host", captain.Runtime, null), revision);
                UsageAccountMembership member = await database.Driver.UsageMemberships.CreateAsync(new UsageAccountMembership { TenantId = tenant.Id, CaptainId = captain.Id,
                    HostId = "host", Runtime = captain.Runtime, AccountKey = "account", ConfigurationRevision = revision }, token).ConfigureAwait(false);
                UsageWindow window = new UsageWindow { Name = "five-hour", UsedPercent = 40 };
                CaptainSelectionRecordSet records = Records(reserve: true, tenantId: tenant.Id, ownerId: owner.Id);
                records.Candidates[0].CaptainId = captain.Id;
                records.Reservations[0].CaptainId = captain.Id; records.Reservations[0].MembershipId = member.Id;
                records.Reservations[0].WindowKey = UsageBudgetIdentity.WindowKey(window);
                records.Reservations[0].Generation = UsageBudgetIdentity.ContinuousGeneration;
                await database.Driver.CaptainSelections.CreateDecisionAsync(records, token).ConfigureAwait(false);
                DateTime now = DateTime.UtcNow.AddSeconds(1);
                UsageSelectionReservationReconciler reconciler = new UsageSelectionReservationReconciler(database.Driver);
                AssertEqual(1, await reconciler.ReconcileAsync(now, token: token).ConfigureAwait(false));
                DateTime captureTime = now.AddSeconds(records.Policy.SettlementLagSeconds + 1);
                async Task Capture(DateTime? started)
                {
                    UsageSnapshot snapshot = new UsageSnapshot { TenantId = tenant.Id, HostId = "host", Runtime = captain.Runtime, AccountKey = "account",
                        Source = UsageCaptureSourceEnum.OAuthEndpoint, Status = UsageSnapshotStatusEnum.Ok, CapturedUtc = captureTime, ReceivedUtc = captureTime,
                        ProbeStartedUtc = started, WindowsJson = UsageWindowsJson.Serialize(new List<UsageWindow> { window }) };
                    await database.Driver.UsageSnapshots.CreateAsync(snapshot, new List<string> { captain.Id }, token).ConfigureAwait(false);
                    await database.Driver.UsageSnapshots.UpsertLatestStateAsync(new UsageLatestState { TenantId = tenant.Id, HostId = "host", Runtime = captain.Runtime,
                        AccountKey = "account", LastGoodSnapshotId = snapshot.Id, LastGoodCapturedUtc = captureTime, LastGoodReceivedUtc = captureTime,
                        LastGoodStatus = UsageSnapshotStatusEnum.Ok, LastGoodWindowsJson = snapshot.WindowsJson, CaptureContextKey = context }, token).ConfigureAwait(false);
                }
                await Capture(null).ConfigureAwait(false);
                List<UsageSelectionAccountEvidence> evidence = await database.Driver.CaptainSelections.ReadAccountEvidenceAsync(tenant.Id, new[] { captain.Id }, token).ConfigureAwait(false);
                AssertEqual(1, evidence.Count); AssertEqual(member.Id, evidence[0].MembershipId); AssertEqual(context, evidence[0].CaptureContextKey);
                AssertEqual(UsageCaptureSourceEnum.OAuthEndpoint, evidence[0].Source); AssertNull(evidence[0].ProbeStartedUtc);
                AssertEqual(0, (await database.Driver.CaptainSelections.ReadAccountEvidenceAsync("tenant-other", new[] { captain.Id }, token).ConfigureAwait(false)).Count);
                UsageSelectionBudgetTotals total = (await database.Driver.CaptainSelections.ReadBudgetTotalsAsync(tenant.Id, new[] { captain.Id }, token).ConfigureAwait(false)).Single();
                AssertEqual(0.0, total.ActivePercent); AssertEqual(2.0, total.UnsettledPercent);
                AssertEqual(0, await reconciler.ReconcileAsync(captureTime.AddSeconds(1), token: token).ConfigureAwait(false), "legacy/passive timestamps cannot prove post-execution capture");
                captureTime = captureTime.AddSeconds(2);
                await Capture(captureTime.AddMilliseconds(-100)).ConfigureAwait(false);
                AssertEqual(1, await reconciler.ReconcileAsync(captureTime.AddSeconds(1), token: token).ConfigureAwait(false));
                AssertEqual(0, (await database.Driver.CaptainSelections.ReadReservationsAsync(tenant.Id, "host", "ClaudeCode", "account", token).ConfigureAwait(false)).Count);
                AssertEqual(0, (await database.Driver.CaptainSelections.ReadBudgetTotalsAsync(tenant.Id, new[] { captain.Id }, token).ConfigureAwait(false)).Count);
            });
            return new TestSuiteDescriptor(_SuiteId, "Captain selection ledger", cases);
        }

        private static CaptainSelectionRecordSet Records(bool reserve = false, string tenantId = "tenant-a", string ownerId = "owner")
        {
            CaptainSelectionRecordSet records = new CaptainSelectionRecordSet();
            records.Policy = CaptainSelectionPolicyRevision.FromSettings(tenantId, new UsageSelectionSettings { Strategy = CaptainSelectionStrategyEnum.MostRemainingAllowance });
            CaptainSelectionDecision decision = records.Decision;
            decision.TenantId = tenantId; decision.UserId = ownerId; decision.MissionId = "mission";
            decision.ExecutionAttemptId = decision.Id; decision.Mode = UsageSelectionModeEnum.Observe;
            decision.Outcome = "Admitted"; decision.Trigger = "Dispatch"; decision.SelectedCaptainId = "visible";
            decision.SelectedProvider = "anthropic"; decision.SelectedModel = "opus"; decision.PolicyRevisionId = records.Policy.Id;
            decision.AlgorithmVersion = UsageSelectionPolicy.AlgorithmVersion; decision.EvidenceFingerprint = "evidence";
            decision.EvaluatedUtc = DateTime.UtcNow; decision.PrimaryReasonCode = "SelectedForHeadroom"; decision.CandidateCount = 1;
            CaptainSelectionCandidateRecord candidate = new CaptainSelectionCandidateRecord { TenantId = tenantId, UserId = ownerId, DecisionId = decision.Id,
                CaptainId = "visible", ConfiguredModel = "opus", Provider = "anthropic", Rank = 1, Capacity = 1, Eligible = true, Band = UsageSelectionBandEnum.Comfortable, PrimaryReasonCode = "SufficientHeadroom", AvailablePercent = 58 };
            records.Candidates.Add(candidate);
            records.Windows.Add(new CaptainSelectionWindowRecord { TenantId = tenantId, UserId = ownerId, DecisionId = decision.Id, CandidateId = candidate.Id,
                SnapshotId = "pruned-source", WindowKey = "five-hour", UsedPercent = 40, ActiveReservationPercent = 2, ReservePercent = 10, Model = "opus", Applicable = true });
            records.Reasons.Add(new CaptainSelectionReason { TenantId = tenantId, UserId = ownerId, DecisionId = decision.Id, CandidateId = candidate.Id, ReasonCode = "SufficientHeadroom", RequiredPercent = 12, AvailablePercent = 58 });
            if (reserve) records.Reservations.Add(new UsageBudgetReservation { TenantId = tenantId, UserId = ownerId, DecisionId = decision.Id,
                MissionId = decision.MissionId, ExecutionAttemptId = decision.ExecutionAttemptId, CaptainId = "visible", MembershipId = "membership", HostId = "host", Runtime = "ClaudeCode",
                AccountKey = "account", WindowKey = "five-hour", Generation = "generation", ReservedPercent = 2 });
            return records;
        }
    }
}
