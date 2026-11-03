namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Transactional budget races, idempotency, stale evidence and rollback on all providers.</summary>
    public sealed class UsageEnforcementSuite : IArmadaTestSuite
    {
        private const string _SuiteId = "Database.UsageEnforcement";
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, Func<CancellationToken, Task> body) => cases.Add(new TestCaseDescriptor(_SuiteId, id, id.Replace('_', ' '), body));
            Add("verification_repair_claims_once_and_records_the_new_attempt_atomically", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(used: 40, token: token).ConfigureAwait(false);
                Mission verifying = (await fixture.Database.Missions.ReadAsync(fixture.Missions[0].Id, token).ConfigureAwait(false))!;
                AssertEqual(fixture.Owner.Id, verifying.UserId, "mission reads must retain ownership before update and repair");
                verifying.CaptainId = fixture.Captains[0].Id; verifying.Status = MissionStatusEnum.Verifying; verifying.ProcessId = null;
                verifying.LastUpdateUtc = DateTime.UtcNow;
                await fixture.Database.Missions.UpdateAsync(verifying, token).ConfigureAwait(false);
                verifying = (await fixture.Database.Missions.ReadAsync(verifying.Id, token).ConfigureAwait(false))!;
                AssertEqual(fixture.Owner.Id, verifying.UserId, "mission ownership must survive its update");
                AssertTrue(await fixture.Database.Captains.ReleaseVerificationCapacityAsync(verifying.CaptainId, verifying.Id, 4242, verifying.LastUpdateUtc, token).ConfigureAwait(false));
                CaptainSelectionRecordSet first = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                CaptainSelectionRecordSet second = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                first.Decision.Trigger = "Repair"; second.Decision.Trigger = "Repair";
                CaptainSelectionAdmissionReceipt[] receipts = await Task.WhenAll(
                    fixture.Database.CaptainSelections.TryAdmitEnforcedRepairAsync(first, verifying.LastUpdateUtc, token: token),
                    fixture.Database.CaptainSelections.TryAdmitEnforcedRepairAsync(second, verifying.LastUpdateUtc, token: token)).ConfigureAwait(false);
                AssertEqual(1, receipts.Count(r => r.Claimed));
                AssertEqual(MissionStatusEnum.InProgress, (await fixture.Database.Missions.ReadAsync(verifying.Id, token).ConfigureAwait(false))!.Status);
                AssertEqual(1, (await fixture.Database.Captains.ReadAsync(verifying.CaptainId, token).ConfigureAwait(false))!.ActiveMissionCount);
                CaptainSelectionDecision current = (await fixture.Database.CaptainSelections.ReadCurrentDecisionAsync(fixture.Tenant.Id, verifying.Id, token: token).ConfigureAwait(false))!;
                AssertEqual("Repair", current.Trigger);
                CaptainSelectionRecordSet winner = receipts[0].Claimed ? first : second;
                AssertTrue((await fixture.Database.CaptainSelections.TryAdmitEnforcedRepairAsync(winner, verifying.LastUpdateUtc, token: token).ConfigureAwait(false)).AlreadyCommitted);
            });
            Add("distinct_captains_cannot_spend_shared_account_twice_and_replay_never_relaunches", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(token: token).ConfigureAwait(false);
                CaptainSelectionRecordSet first = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                CaptainSelectionRecordSet second = await fixture.RecordsAsync(1, token: token).ConfigureAwait(false);
                CaptainSelectionAdmissionReceipt[] receipts = await Task.WhenAll(fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(first, token: token),
                    fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(second, token: token)).ConfigureAwait(false);
                AssertEqual(1, receipts.Count(r => r.Claimed));
                int winner = receipts[0].Claimed ? 0 : 1, loser = 1 - winner;
                AssertEqual(1, (await fixture.Database.CaptainSelections.ReadReservationsAsync(fixture.Tenant.Id, "host", "ClaudeCode", "opaque-account", token).ConfigureAwait(false)).Count);
                CaptainSelectionAdmissionReceipt replay = await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(winner == 0 ? first : second, token: token).ConfigureAwait(false);
                AssertFalse(replay.Claimed); AssertTrue(replay.AlreadyCommitted);
                AssertEqual(MissionStatusEnum.Pending, (await fixture.Database.Missions.ReadAsync(fixture.Missions[loser].Id, token).ConfigureAwait(false))!.Status);
                AssertEqual(0, (await fixture.Database.Captains.ReadAsync(fixture.Captains[loser].Id, token).ConfigureAwait(false))!.ActiveMissionCount);
                CaptainSelectionRecordSet refreshed = await fixture.RecordsAsync(loser, token: token).ConfigureAwait(false);
                AssertEqual("BelowReserve", refreshed.Candidates.Single().PrimaryReasonCode);
                AssertFalse((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(refreshed, token: token).ConfigureAwait(false)).Claimed);
            });
            Add("changed_capture_is_recomputed_even_when_percent_is_unchanged", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(token: token).ConfigureAwait(false);
                CaptainSelectionRecordSet old = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                await fixture.CaptureAsync(87, token).ConfigureAwait(false);
                AssertFalse((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(old, token: token).ConfigureAwait(false)).Claimed);
                AssertEqual(0, (await fixture.Database.Captains.ReadAsync(fixture.Captains[0].Id, token).ConfigureAwait(false))!.ActiveMissionCount);
                AssertTrue((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(await fixture.RecordsAsync(0, token: token).ConfigureAwait(false), token: token).ConfigureAwait(false)).Claimed);
            });
            Add("missing_or_tampered_reservation_cannot_bypass_atomic_budget_write", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(token: token).ConfigureAwait(false);
                CaptainSelectionRecordSet missing = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                missing.Reservations.Clear();
                AssertFalse((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(missing, token: token).ConfigureAwait(false)).Claimed);
                CaptainSelectionRecordSet reduced = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                reduced.Reservations[0].ReservedPercent = 0.1;
                AssertFalse((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(reduced, token: token).ConfigureAwait(false)).Claimed);
                AssertNull(await fixture.Database.CaptainSelections.ReadCurrentDecisionAsync(fixture.Tenant.Id, fixture.Missions[0].Id, token: token).ConfigureAwait(false));
            });
            Add("changed_mission_or_captain_versions_refuse_without_partial_claim", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(token: token).ConfigureAwait(false);
                CaptainSelectionRecordSet mission = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                mission.AdmissionMissionVersionUtc = mission.AdmissionMissionVersionUtc!.Value.AddSeconds(-1);
                AssertFalse((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(mission, token: token).ConfigureAwait(false)).Claimed);
                CaptainSelectionRecordSet captain = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                captain.AdmissionCaptainVersionUtc = captain.AdmissionCaptainVersionUtc!.Value.AddSeconds(-1);
                AssertFalse((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(captain, token: token).ConfigureAwait(false)).Claimed);
                AssertEqual(0, (await fixture.Database.Captains.ReadAsync(fixture.Captains[0].Id, token).ConfigureAwait(false))!.ActiveMissionCount);
            });
            Add("unknown_policy_is_explicit_and_wait_never_claims", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(token: token).ConfigureAwait(false);
                fixture.Policy.UnknownUsageBehavior = UsageUnknownBehaviorEnum.Wait;
                AssertFalse((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(await fixture.RecordsAsync(0, captureEnabled: false, token: token).ConfigureAwait(false), token: token).ConfigureAwait(false)).Claimed);
                fixture.Policy.UnknownUsageBehavior = UsageUnknownBehaviorEnum.AllowDeprioritized;
                AssertTrue((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(await fixture.RecordsAsync(0, captureEnabled: false, token: token).ConfigureAwait(false), token: token).ConfigureAwait(false)).Claimed);
                AssertEqual(0, (await fixture.Database.CaptainSelections.ReadReservationsAsync(fixture.Tenant.Id, "host", "ClaudeCode", "opaque-account", token).ConfigureAwait(false)).Count);
            });
            Add("ledger_collision_rolls_back_budget_slot_and_mission", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(token: token).ConfigureAwait(false);
                CaptainSelectionRecordSet existing = await fixture.RecordsAsync(1, token: token).ConfigureAwait(false);
                existing.Reservations.Clear();
                await fixture.Database.CaptainSelections.CreateDecisionAsync(existing, token).ConfigureAwait(false);
                CaptainSelectionRecordSet proposed = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                proposed.Candidates[0].Id = existing.Candidates[0].Id; proposed.Windows[0].CandidateId = proposed.Candidates[0].Id;
                AssertFalse((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(proposed, token: token).ConfigureAwait(false)).Claimed);
                AssertEqual(MissionStatusEnum.Pending, (await fixture.Database.Missions.ReadAsync(fixture.Missions[0].Id, token).ConfigureAwait(false))!.Status);
                AssertEqual(0, (await fixture.Database.Captains.ReadAsync(fixture.Captains[0].Id, token).ConfigureAwait(false))!.ActiveMissionCount);
                AssertEqual(0, (await fixture.Database.CaptainSelections.ReadReservationsAsync(fixture.Tenant.Id, "host", "ClaudeCode", "opaque-account", token).ConfigureAwait(false)).Count);
            });
            Add("disabled_execution_preserves_uncertainty_until_proven_post_exit_capture", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(20, token).ConfigureAwait(false);
                AssertTrue(await fixture.Database.CaptainSelections.TryAdmitLegacyAsync(fixture.Captains[0].Id, fixture.Missions[0].Id, "host", token: token).ConfigureAwait(false));
                AssertEqual(0L, (await fixture.Database.CaptainSelections.EnumerateByMissionAsync(fixture.Tenant.Id, fixture.Missions[0].Id, token: token).ConfigureAwait(false)).TotalRecords, "Disabled does not invent selection history");
                UsageUntrackedExecution debt = (await fixture.Database.CaptainSelections.ReadUntrackedExecutionsAsync(token: token).ConfigureAwait(false)).Single();
                AssertTrue(debt.AccountAttributionVerified);
                fixture.Policy.UnknownUsageBehavior = UsageUnknownBehaviorEnum.Wait;
                CaptainSelectionRecordSet proposed = await fixture.RecordsAsync(1, token: token).ConfigureAwait(false);
                AssertEqual("UsageUncertainDebt", proposed.Candidates[0].PrimaryReasonCode);
                AssertFalse((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(proposed, token: token).ConfigureAwait(false)).Claimed);
                Mission ended = (await fixture.Database.Missions.ReadAsync(fixture.Missions[0].Id, token).ConfigureAwait(false))!;
                ended.Status = MissionStatusEnum.Complete;
                await fixture.Database.Missions.UpdateAsync(ended, token).ConfigureAwait(false);
                DateTime now = DateTime.UtcNow.AddSeconds(1);
                UsageUntrackedExecutionReconciler reconciler = new UsageUntrackedExecutionReconciler(fixture.Database, new ArmadaSettings());
                await reconciler.ReconcileAsync(now, token: token).ConfigureAwait(false);
                DateTime capture = now.AddSeconds(62);
                await fixture.CaptureAsync(25, token, capture, activeProbe: false).ConfigureAwait(false);
                await reconciler.ReconcileAsync(capture.AddSeconds(1), token: token).ConfigureAwait(false);
                AssertEqual(UsageReservationStateEnum.Unsettled, (await fixture.Database.CaptainSelections.ReadUntrackedExecutionsAsync(token: token).ConfigureAwait(false)).Single().State);
                capture = capture.AddSeconds(2);
                await fixture.CaptureAsync(25, token, capture).ConfigureAwait(false);
                await reconciler.ReconcileAsync(capture.AddSeconds(1), token: token).ConfigureAwait(false);
                AssertEqual(0, (await fixture.Database.CaptainSelections.ReadUntrackedExecutionsAsync(token: token).ConfigureAwait(false)).Count);
                AssertFalse((await fixture.Database.CaptainSelections.ReadBudgetTotalsAsync(fixture.Tenant.Id, new[] { fixture.Captains[1].Id }, token).ConfigureAwait(false)).Any(t => t.HasUncertainDebt));
            });
            Add("startup_adoption_is_idempotent_and_unknown_host_is_not_invented", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(token: token).ConfigureAwait(false);
                AssertTrue(await fixture.Database.Captains.TryClaimAsync(fixture.Captains[0].Id, fixture.Missions[0].Id, null, token).ConfigureAwait(false));
                UsageUntrackedExecution unknown = (await fixture.Database.CaptainSelections.ReadUntrackedExecutionsAsync(token: token).ConfigureAwait(false)).Single();
                AssertFalse(unknown.AccountAttributionVerified);
                AssertTrue((await fixture.Database.CaptainSelections.ReadBudgetTotalsAsync(fixture.Tenant.Id, new[] { fixture.Captains[1].Id }, token).ConfigureAwait(false)).Any(t => t.HasUncertainDebt), "unverified account attribution cannot authorize a known balance elsewhere");
                await fixture.Database.CaptainSelections.DeleteByTenantAsync(fixture.Tenant.Id, token).ConfigureAwait(false);
                AssertEqual(1, await fixture.Database.CaptainSelections.AdoptUntrackedExecutionsAsync(localHostId: "host", token: token).ConfigureAwait(false));
                AssertEqual(0, await fixture.Database.CaptainSelections.AdoptUntrackedExecutionsAsync(localHostId: "host", token: token).ConfigureAwait(false));
                UsageUntrackedExecution adopted = (await fixture.Database.CaptainSelections.ReadUntrackedExecutionsAsync(token: token).ConfigureAwait(false)).Single();
                AssertTrue(adopted.AccountAttributionVerified);
                AssertEqual(0L, (await fixture.Database.CaptainSelections.EnumerateByMissionAsync(fixture.Tenant.Id, fixture.Missions[0].Id, token: token).ConfigureAwait(false)).TotalRecords);
                AssertFalse(await fixture.Database.CaptainSelections.UpdateUntrackedExecutionAsync(adopted, adopted.Revision + 1, token).ConfigureAwait(false));
            });
            Add("execution_host_mismatch_never_spends_another_hosts_account", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(token: token).ConfigureAwait(false);
                CaptainSelectionRecordSet records = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                records.AdmissionHostId = "other-host";
                AssertFalse((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(records, token: token).ConfigureAwait(false)).Claimed);
                AssertEqual(MissionStatusEnum.Pending, (await fixture.Database.Missions.ReadAsync(fixture.Missions[0].Id, token).ConfigureAwait(false))!.Status);
            });
            Add("fractional_policy_matches_durable_precision_without_under_reserving", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(50.123456789, token).ConfigureAwait(false);
                fixture.Policy.PerAttemptReservationPercent = 0.123456789;
                AssertEqual(0.123457, fixture.Policy.PerAttemptReservationPercent);
                CaptainSelectionRecordSet records = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                AssertTrue((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(records, token: token).ConfigureAwait(false)).Claimed);
                UsageBudgetReservation reservation = (await fixture.Database.CaptainSelections.ReadReservationsAsync(fixture.Tenant.Id, "host", "ClaudeCode", "opaque-account", token).ConfigureAwait(false)).Single();
                AssertEqual(fixture.Policy.PerAttemptReservationPercent, reservation.ReservedPercent);
                AssertTrue(records.Candidates[0].AvailablePercent <= 100 - 50.123456789);
            });
            return new TestSuiteDescriptor(_SuiteId, "Usage enforcement admission", cases);
        }
    }
}
