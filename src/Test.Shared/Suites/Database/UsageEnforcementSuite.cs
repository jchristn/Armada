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
            Add("superseded_launch_and_cleanup_cannot_touch_a_new_attempt", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(used: 40, token: token).ConfigureAwait(false);
                CaptainSelectionRecordSet old = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                AssertTrue((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(old, token: token).ConfigureAwait(false)).Claimed);
                AssertTrue(await fixture.Database.CaptainSelections.ReleaseUnstartedAsync(fixture.Tenant.Id, fixture.Missions[0].Id, fixture.Captains[0].Id, old.Decision.ExecutionAttemptId, token).ConfigureAwait(false));
                Mission pending = (await fixture.Database.Missions.ReadAsync(fixture.Missions[0].Id, token).ConfigureAwait(false))!;
                System.Reflection.PropertyInfo receiptProperty = typeof(Mission).GetProperty("UsageAdmissionExecutionKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                AssertEqual(old.Decision.ExecutionAttemptId, receiptProperty.GetValue(pending)?.ToString());
                AssertFalse(System.Text.Json.JsonSerializer.Serialize(pending).Contains("UsageAdmissionExecutionKey", StringComparison.OrdinalIgnoreCase), "internal execution ownership is not a transport field");
                pending.Status = MissionStatusEnum.Pending; pending.CaptainId = null; await fixture.Database.Missions.UpdateAsync(pending, token).ConfigureAwait(false);
                AssertTrue(await fixture.Database.Captains.TryReleaseFromMissionAsync(fixture.Captains[0].Id, pending.Id, token).ConfigureAwait(false));
                CaptainSelectionRecordSet fresh = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                AssertTrue((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(fresh, token: token).ConfigureAwait(false)).Claimed);
                AssertEqual(old.Decision.ExecutionAttemptId, receiptProperty.GetValue(pending)?.ToString(), "a previously read snapshot retains its old attempt identity");
                AssertEqual(fresh.Decision.ExecutionAttemptId, receiptProperty.GetValue((await fixture.Database.Missions.ReadAsync(pending.Id, token).ConfigureAwait(false))!)?.ToString());
                AssertFalse(await fixture.Database.CaptainSelections.ReleaseUnstartedAsync(fixture.Tenant.Id, pending.Id, fixture.Captains[0].Id, old.Decision.ExecutionAttemptId, token).ConfigureAwait(false));
                AssertFalse(await fixture.Database.CaptainSelections.MarkLaunchAttemptedAsync(fixture.Tenant.Id, pending.Id, fixture.Captains[0].Id, old.Decision.ExecutionAttemptId, token).ConfigureAwait(false));
                AssertEqual(fresh.Decision.Id, (await fixture.Database.CaptainSelections.ReadReservationsAsync(fixture.Tenant.Id, "host", "ClaudeCode", "opaque-account", token).ConfigureAwait(false)).Single().DecisionId);
                AssertEqual(1, (await fixture.Database.Captains.ReadAsync(fixture.Captains[0].Id, token).ConfigureAwait(false))!.ActiveMissionCount);
            });
            Add("fixed_window_reservations_carry_the_evidence_generation", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(used: 40, token: token).ConfigureAwait(false);
                fixture.FixedResetUtc = DateTime.UtcNow.AddHours(2);
                await fixture.CaptureAsync(40, token).ConfigureAwait(false);
                CaptainSelectionRecordSet mismatched = await fixture.RecordsAsync(1, token: token).ConfigureAwait(false);
                mismatched.Reservations[0].Generation = UsageBudgetIdentity.ContinuousGeneration;
                AssertFalse((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(mismatched, token: token).ConfigureAwait(false)).Claimed, "a reservation must use the evidence window's generation");
                CaptainSelectionRecordSet admitted = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                AssertTrue(admitted.Reservations[0].Generation.StartsWith(UsageBudgetIdentity.FixedGenerationPrefix, StringComparison.Ordinal), "fixed generation recorded");
                AssertTrue((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(admitted, token: token).ConfigureAwait(false)).Claimed);
                UsageBudgetReservation stored = (await fixture.Database.CaptainSelections.ReadReservationsAsync(fixture.Tenant.Id, "host", "ClaudeCode", "opaque-account", token).ConfigureAwait(false)).Single();
                AssertEqual(admitted.Reservations[0].Generation, stored.Generation, "generation persisted");
                UsageSelectionBudgetTotals total = (await fixture.Database.CaptainSelections.ReadBudgetTotalsAsync(fixture.Tenant.Id, new[] { fixture.Captains[1].Id }, token).ConfigureAwait(false)).Single();
                AssertEqual(stored.Generation, total.Generation); AssertTrue(total.ActivePercent > 0, "the shared account still sees the active estimate");
            });
            Add("cancelled_before_launch_releases_only_with_durable_proof", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(used: 40, token: token).ConfigureAwait(false);
                CaptainSelectionRecordSet admitted = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                AssertTrue((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(admitted, token: token).ConfigureAwait(false)).Claimed);
                AssertFalse(await fixture.Database.CaptainSelections.ReleaseStoppedUnstartedAsync(fixture.Tenant.Id, admitted.Decision.ExecutionAttemptId, token).ConfigureAwait(false));
                Mission mission = (await fixture.Database.Missions.ReadAsync(fixture.Missions[0].Id, token).ConfigureAwait(false))!;
                mission.Status = MissionStatusEnum.Cancelled; await fixture.Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                AssertTrue(await fixture.Database.CaptainSelections.ReleaseStoppedUnstartedAsync(fixture.Tenant.Id, admitted.Decision.ExecutionAttemptId, token).ConfigureAwait(false));
                AssertFalse(await fixture.Database.CaptainSelections.MarkLaunchAttemptedAsync(fixture.Tenant.Id, mission.Id, fixture.Captains[0].Id, await fixture.ExecutionKeyAsync(0, token).ConfigureAwait(false), token).ConfigureAwait(false));
                AssertEqual(0, (await fixture.Database.CaptainSelections.ReadReservationsAsync(fixture.Tenant.Id, "host", "ClaudeCode", "opaque-account", token).ConfigureAwait(false)).Count);
                AssertEqual(1, (await fixture.Database.CaptainSelections.EnumerateExecutionEventsAsync(fixture.Tenant.Id, admitted.Decision.Id, token: token).ConfigureAwait(false)).Objects.Count(e => e.EventKind == "ProvenUnstarted"));
            });
            Add("admitted_recovery_retains_the_exact_audit_revision", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(used: 95, token: token).ConfigureAwait(false);
                UsageSelectionRecovery audit = (await fixture.Database.CaptainSelections.TryAuthorizeRecoveryAsync(fixture.Tenant.Id, fixture.Missions[0].Id, fixture.Owner.Id,
                    new UsageSelectionRecoveryRequest { ReserveException = true, Reason = "Authorized bounded reserve test" }, token).ConfigureAwait(false))!;
                CaptainSelectionRecordSet records = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                CaptainSelectionExecutionEvent applied = records.Events.Single(); records.Events.Clear();
                AssertFalse((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(records, token: token).ConfigureAwait(false)).Claimed, "missing audit correlation cannot admit an exception");
                records.Events.Add(applied);
                AssertTrue((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(records, token: token).ConfigureAwait(false)).Claimed);
                UsageSelectionRecovery retained = (await fixture.Database.CaptainSelections.ReadDecisionRecoveryAsync(fixture.Tenant.Id, records.Decision.Id, token).ConfigureAwait(false))!;
                AssertEqual(audit.Id, retained.Id); AssertEqual(fixture.Owner.Id, retained.ActorUserId); AssertEqual(audit.Reason, retained.Reason);
                AssertNull(await fixture.Database.CaptainSelections.TryAuthorizeRecoveryAsync(fixture.Tenant.Id, fixture.Missions[0].Id, fixture.Owner.Id,
                    new UsageSelectionRecoveryRequest { ExpectedRevision = 1, Reason = "Cannot edit running work" }, token).ConfigureAwait(false));
                AssertNull(await fixture.Database.CaptainSelections.ReadDecisionRecoveryAsync("other-tenant", records.Decision.Id, token).ConfigureAwait(false));
            });
            Add("fifty_simultaneous_admissions_share_exactly_one_reservation_budget", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(token: token, captainCount: 50).ConfigureAwait(false);
                List<CaptainSelectionRecordSet> proposals = new List<CaptainSelectionRecordSet>();
                for (int index = 0; index < 50; index++) proposals.Add(await fixture.RecordsAsync(index, token: token).ConfigureAwait(false));
                Task<CaptainSelectionAdmissionReceipt>[] attempts = proposals.Select(p => fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(p, token: token)).ToArray();
                CaptainSelectionAdmissionReceipt[] receipts = await Task.WhenAll(attempts).ConfigureAwait(false);
                AssertEqual(1, receipts.Count(r => r.Claimed));
                AssertEqual(1, (await fixture.Database.CaptainSelections.ReadReservationsAsync(fixture.Tenant.Id, "host", "ClaudeCode", "opaque-account", token).ConfigureAwait(false)).Count);
                int assigned = 0, occupied = 0;
                foreach (Mission mission in fixture.Missions)
                    if ((await fixture.Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false))!.Status == MissionStatusEnum.Assigned) assigned++;
                foreach (Captain captain in fixture.Captains)
                    occupied += (await fixture.Database.Captains.ReadAsync(captain.Id, token).ConfigureAwait(false))!.ActiveMissionCount;
                AssertEqual(1, assigned); AssertEqual(1, occupied);
            });
            Add("classified_cooldown_is_shared_durable_idempotent_and_revalidated", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(used: 40, token: token).ConfigureAwait(false);
                CaptainSelectionRecordSet admitted = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                AssertTrue((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(admitted, token: token).ConfigureAwait(false)).Claimed);
                CaptainSelectionRecordSet stale = await fixture.RecordsAsync(1, token: token).ConfigureAwait(false);
                AssertTrue(await fixture.Database.CaptainSelections.MarkLaunchAttemptedAsync(fixture.Tenant.Id, fixture.Missions[0].Id, fixture.Captains[0].Id, await fixture.ExecutionKeyAsync(0, token).ConfigureAwait(false), token).ConfigureAwait(false));
                Mission running = (await fixture.Database.Missions.ReadAsync(fixture.Missions[0].Id, token).ConfigureAwait(false))!;
                ArmadaSettings settings = new ArmadaSettings();
                UsageSelectionCooldownService service = new UsageSelectionCooldownService(fixture.Database, settings);
                RuntimeExitInfo limit = RuntimeFailureClassifier.Decide(1, new RuntimeProviderError { ErrorType = "rate_limit_error", RetryAfterSeconds = 180 });
                DateTime observed = DateTime.UtcNow;
                AssertTrue(await service.RecordAsync(running, limit, admitted.Decision.ExecutionAttemptId, observed, token).ConfigureAwait(false));
                AssertFalse(await service.RecordAsync(running, limit, admitted.Decision.ExecutionAttemptId, observed.AddSeconds(20), token).ConfigureAwait(false), "duplicate exits never extend a cooldown");
                AssertFalse((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(stale, token: token).ConfigureAwait(false)).Claimed);
                UsageSelectionAccountEvidence account = (await fixture.Database.CaptainSelections.ReadAccountEvidenceAsync(fixture.Tenant.Id, new[] { fixture.Captains[1].Id }, token).ConfigureAwait(false)).Single();
                AssertEqual(observed.AddSeconds(180), account.CooldownUntilUtc!.Value);
                await fixture.CaptureAsync(20, token).ConfigureAwait(false);
                CaptainSelectionRecordSet blocked = await fixture.RecordsAsync(1, token: token).ConfigureAwait(false);
                AssertEqual("AccountCooldown", blocked.Candidates.Single().PrimaryReasonCode);
                AssertEqual(account.CooldownUntilUtc, blocked.Candidates.Single().NextReconsiderUtc);
                AssertEqual(0, (await fixture.Database.Captains.ReadAsync(fixture.Captains[1].Id, token).ConfigureAwait(false))!.ActiveMissionCount);
                EnumerationResult<CaptainSelectionExecutionEvent> events = await fixture.Database.CaptainSelections.EnumerateExecutionEventsAsync(fixture.Tenant.Id, admitted.Decision.Id, fixture.Owner.Id, token: token).ConfigureAwait(false);
                AssertEqual(1, events.Objects.Count(e => e.EventKind == "ProviderCooldownObserved"));
            });
            Add("cooldown_requires_classification_owned_attempt_and_actual_host_proof", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(used: 40, token: token).ConfigureAwait(false);
                fixture.Policy.Mode = UsageSelectionModeEnum.Observe;
                CaptainSelectionRecordSet records = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                records.AdmissionHostId = "different-host";
                AssertTrue((await fixture.Database.CaptainSelections.TryAdmitObservedAsync(records, token: token).ConfigureAwait(false)).Claimed);
                AssertEqual(0, (await fixture.Database.CaptainSelections.ReadReservationsAsync(fixture.Tenant.Id, "host", "ClaudeCode", "opaque-account", token).ConfigureAwait(false)).Count);
                AssertTrue(await fixture.Database.CaptainSelections.MarkLaunchAttemptedAsync(fixture.Tenant.Id, fixture.Missions[0].Id, fixture.Captains[0].Id, await fixture.ExecutionKeyAsync(0, token).ConfigureAwait(false), token).ConfigureAwait(false));
                Mission mission = (await fixture.Database.Missions.ReadAsync(fixture.Missions[0].Id, token).ConfigureAwait(false))!;
                UsageSelectionCooldownService service = new UsageSelectionCooldownService(fixture.Database, new ArmadaSettings());
                AssertFalse(await service.RecordAsync(mission, RuntimeFailureClassifier.Decide(1, new RuntimeProviderError { HttpStatusCode = 503 }), records.Decision.ExecutionAttemptId, DateTime.UtcNow, token).ConfigureAwait(false));
                AssertFalse(await service.RecordAsync(mission, RuntimeFailureClassifier.Decide(1, new RuntimeProviderError { ErrorType = "rate_limit_error" }), records.Decision.ExecutionAttemptId, DateTime.UtcNow, token).ConfigureAwait(false));
                AssertNull((await fixture.Database.CaptainSelections.ReadAccountEvidenceAsync(fixture.Tenant.Id, new[] { fixture.Captains[1].Id }, token).ConfigureAwait(false)).Single().CooldownUntilUtc);
            });
            Add("proven_unstarted_release_never_releases_a_launcher_or_sibling", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(used: 40, token: token).ConfigureAwait(false);
                CaptainSelectionRecordSet first = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                AssertTrue((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(first, token: token).ConfigureAwait(false)).Claimed);
                CaptainSelectionRecordSet second = await fixture.RecordsAsync(1, token: token).ConfigureAwait(false);
                AssertTrue((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(second, token: token).ConfigureAwait(false)).Claimed);
                AssertTrue(await fixture.Database.CaptainSelections.MarkLaunchAttemptedAsync(fixture.Tenant.Id, fixture.Missions[1].Id, fixture.Captains[1].Id, await fixture.ExecutionKeyAsync(1, token).ConfigureAwait(false), token).ConfigureAwait(false));
                AssertTrue(await fixture.Database.CaptainSelections.ReleaseUnstartedAsync(fixture.Tenant.Id, fixture.Missions[0].Id, fixture.Captains[0].Id, await fixture.ExecutionKeyAsync(0, token).ConfigureAwait(false), token).ConfigureAwait(false));
                AssertFalse(await fixture.Database.CaptainSelections.MarkLaunchAttemptedAsync(fixture.Tenant.Id, fixture.Missions[0].Id, fixture.Captains[0].Id, await fixture.ExecutionKeyAsync(0, token).ConfigureAwait(false), token).ConfigureAwait(false));
                AssertFalse(await fixture.Database.CaptainSelections.ReleaseUnstartedAsync(fixture.Tenant.Id, fixture.Missions[1].Id, fixture.Captains[1].Id, await fixture.ExecutionKeyAsync(1, token).ConfigureAwait(false), token).ConfigureAwait(false));
                List<UsageBudgetReservation> active = await fixture.Database.CaptainSelections.ReadReservationsAsync(fixture.Tenant.Id, "host", "ClaudeCode", "opaque-account", token).ConfigureAwait(false);
                AssertEqual(1, active.Count); AssertEqual(second.Decision.Id, active.Single().DecisionId);
                AssertTrue(active.Single().ExecutionStartedUtc.HasValue);
            });
            Add("recovery_authorization_is_immutable_revision_bound_and_never_bypasses_exhaustion", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(used: 95, token: token).ConfigureAwait(false);
                CaptainSelectionRecordSet before = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                AssertEqual("BelowReserve", before.Candidates.Single().PrimaryReasonCode);
                UsageSelectionRecoveryRequest request = new UsageSelectionRecoveryRequest { ExpectedRevision = 0, ReserveException = true, Reason = "Time-sensitive controlled test" };
                Task<UsageSelectionRecovery?> one = fixture.Database.CaptainSelections.TryAuthorizeRecoveryAsync(fixture.Tenant.Id, fixture.Missions[0].Id, fixture.Owner.Id, request, token);
                Task<UsageSelectionRecovery?> two = fixture.Database.CaptainSelections.TryAuthorizeRecoveryAsync(fixture.Tenant.Id, fixture.Missions[0].Id, fixture.Owner.Id, request, token);
                UsageSelectionRecovery?[] writes = await Task.WhenAll(one, two).ConfigureAwait(false);
                AssertEqual(1, writes.Count(w => w != null));
                AssertFalse((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(before, token: token).ConfigureAwait(false)).Claimed);
                CaptainSelectionRecordSet authorized = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                AssertEqual("AuthorizedReserveException", authorized.Candidates.Single().PrimaryReasonCode);
                AssertEqual(1, authorized.AdmissionRecoveryRevision);
                UsageSelectionRecovery? revoke = await fixture.Database.CaptainSelections.TryAuthorizeRecoveryAsync(fixture.Tenant.Id, fixture.Missions[0].Id, fixture.Owner.Id,
                    new UsageSelectionRecoveryRequest { ExpectedRevision = 1, Reason = "Revoke the temporary exception" }, token).ConfigureAwait(false);
                AssertNotNull(revoke); AssertEqual(2, revoke!.Revision);
                AssertFalse((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(authorized, token: token).ConfigureAwait(false)).Claimed);
                await fixture.CaptureAsync(100, token).ConfigureAwait(false);
                AssertEqual("AccountQuotaExhausted", (await fixture.RecordsAsync(0, token: token).ConfigureAwait(false)).Candidates.Single().PrimaryReasonCode);
                AssertNull(await fixture.Database.CaptainSelections.ReadRecoveryAsync("other-tenant", fixture.Missions[0].Id, token).ConfigureAwait(false));
            });
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
            Add("slot_claims_record_assignment_time_independent_of_other_captain_updates", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(20, token).ConfigureAwait(false);
                string[] ids = new[] { fixture.Captains[0].Id, fixture.Captains[1].Id };
                AssertEqual(0, (await fixture.Database.CaptainSelections.ReadLastAssignedAsync(fixture.Tenant.Id, ids, token).ConfigureAwait(false)).Count, "never-assigned captains have no assignment time");

                Captain touched = (await fixture.Database.Captains.ReadAsync(fixture.Captains[1].Id, token).ConfigureAwait(false))!;
                touched.Name = touched.Name + "-renamed";
                await fixture.Database.Captains.UpdateAsync(touched, token).ConfigureAwait(false);
                AssertEqual(0, (await fixture.Database.CaptainSelections.ReadLastAssignedAsync(fixture.Tenant.Id, ids, token).ConfigureAwait(false)).Count, "an ordinary captain update is not an assignment");

                DateTime before = DateTime.UtcNow.AddSeconds(-1);
                AssertTrue(await fixture.Database.CaptainSelections.TryAdmitLegacyAsync(fixture.Captains[0].Id, fixture.Missions[0].Id, "host", token: token).ConfigureAwait(false));
                DateTime after = DateTime.UtcNow.AddSeconds(1);
                Dictionary<string, DateTime> assigned = await fixture.Database.CaptainSelections.ReadLastAssignedAsync(fixture.Tenant.Id, ids, token).ConfigureAwait(false);
                AssertEqual(1, assigned.Count);
                AssertTrue(assigned[fixture.Captains[0].Id] >= before && assigned[fixture.Captains[0].Id] <= after, "claim time is recorded");
                AssertEqual(0, (await fixture.Database.CaptainSelections.ReadLastAssignedAsync("ten_other", ids, token).ConfigureAwait(false)).Count, "tenant scoped");
            });
            Add("actionable_waits_use_latest_enforced_decision_scope_and_reason", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(20, token, captainCount: 3).ConfigureAwait(false);
                DateTime now = DateTime.UtcNow;
                await Waiting(fixture, 0, UsageSelectionReasonCodes.UsageStale, now.AddMinutes(-30), UsageSelectionModeEnum.Enforce, token).ConfigureAwait(false);
                await Waiting(fixture, 1, UsageSelectionReasonCodes.BelowReserve, now.AddMinutes(-30), UsageSelectionModeEnum.Enforce, token).ConfigureAwait(false);
                await Waiting(fixture, 2, UsageSelectionReasonCodes.UsageIdentityUnknown, now.AddMinutes(-30), UsageSelectionModeEnum.Observe, token).ConfigureAwait(false);
                DateTime cutoff = now.AddMinutes(-15);
                EnumerationResult<CaptainSelectionWaitingItem> waits = await fixture.Database.CaptainSelections.EnumerateActionableWaitsAsync(fixture.Tenant.Id, null,
                    UsageSelectionReasonCodes.OperatorActionable, cutoff, token: token).ConfigureAwait(false);
                AssertEqual(1L, waits.TotalRecords, "only the enforced, operator-actionable, old wait");
                AssertEqual(fixture.Missions[0].Id, waits.Objects.Single().MissionId);
                AssertEqual(UsageSelectionReasonCodes.UsageStale, waits.Objects.Single().ReasonCode);
                AssertEqual(fixture.Missions[0].Title, waits.Objects.Single().MissionTitle);
                AssertEqual(1L, (await fixture.Database.CaptainSelections.EnumerateActionableWaitsAsync(null, null, UsageSelectionReasonCodes.OperatorActionable, cutoff, token: token).ConfigureAwait(false)).TotalRecords, "global scope");
                AssertEqual(0L, (await fixture.Database.CaptainSelections.EnumerateActionableWaitsAsync("ten_other", null, UsageSelectionReasonCodes.OperatorActionable, cutoff, token: token).ConfigureAwait(false)).TotalRecords, "tenant scoped");
                AssertEqual(0L, (await fixture.Database.CaptainSelections.EnumerateActionableWaitsAsync(fixture.Tenant.Id, "usr_other", UsageSelectionReasonCodes.OperatorActionable, cutoff, token: token).ConfigureAwait(false)).TotalRecords, "owner scoped");
                AssertEqual(1L, (await fixture.Database.CaptainSelections.EnumerateActionableWaitsAsync(fixture.Tenant.Id, fixture.Owner.Id, UsageSelectionReasonCodes.OperatorActionable, cutoff, token: token).ConfigureAwait(false)).TotalRecords, "owner sees own");

                InboxService inbox = new InboxService(fixture.Database, new SyslogLogging.LoggingModule());
                AuthContext owner = AuthContext.Authenticated(fixture.Tenant.Id, fixture.Owner.Id, false, false, "Session");
                EnumerationResult<InboxItem> items = await inbox.EnumerateAsync(owner, new EnumerationQuery { PageNumber = 1, PageSize = 25 }, token).ConfigureAwait(false);
                InboxItem item = items.Objects.Single(i => i.Kind == InboxItemKinds.SelectionBlocked);
                AssertEqual("/missions/" + fixture.Missions[0].Id, item.Href);
                AssertEqual(fixture.Missions[0].Id, item.EntityId);
                AssertEqual(1L, await inbox.CountAsync(owner, new[] { InboxItemKinds.SelectionBlocked }, token).ConfigureAwait(false));

                // A newer, materially different wait restarts the clock.
                await Waiting(fixture, 0, UsageSelectionReasonCodes.UsageMissing, now.AddMinutes(-1), UsageSelectionModeEnum.Enforce, token).ConfigureAwait(false);
                AssertEqual(0L, (await fixture.Database.CaptainSelections.EnumerateActionableWaitsAsync(fixture.Tenant.Id, null, UsageSelectionReasonCodes.OperatorActionable, cutoff, token: token).ConfigureAwait(false)).TotalRecords, "latest decision is too recent");
                AssertEqual(0, (await fixture.Database.CaptainSelections.EnumerateActionableWaitsAsync(fixture.Tenant.Id, null, new List<string>(), cutoff, token: token).ConfigureAwait(false)).Objects.Count, "no reasons, no items");
            });
            Add("latest_usage_reports_reservations_separately_from_observed_usage", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(20, token).ConfigureAwait(false);
                Armada.Server.UsageAccessService access = new Armada.Server.UsageAccessService(fixture.Database, null!, null!);
                AuthContext owner = AuthContext.Authenticated(fixture.Tenant.Id, fixture.Owner.Id, false, false, "Session");
                UsageSnapshotView before = (await access.LatestAsync(owner, new UsageQuery()).ConfigureAwait(false)).Objects.Single();
                AssertEqual(0, before.ReservationEstimates.Count, "nothing reserved yet");
                CaptainSelectionRecordSet admitted = await fixture.RecordsAsync(0, token: token).ConfigureAwait(false);
                AssertTrue((await fixture.Database.CaptainSelections.TryAdmitEnforcedAsync(admitted, token: token).ConfigureAwait(false)).Claimed);
                UsageSnapshotView after = (await access.LatestAsync(owner, new UsageQuery()).ConfigureAwait(false)).Objects.Single();
                UsageReservationEstimate estimate = after.ReservationEstimates.Single();
                AssertEqual("five-hour", estimate.WindowName);
                AssertEqual(fixture.Policy.PerAttemptReservationPercent, estimate.ActivePercent, "one attempt's reservation");
                AssertEqual(0.0, estimate.UnsettledPercent);
                AssertEqual(20.0, after.Windows.Single().UsedPercent, "observed usage is never inflated by estimates");
                AssertFalse(after.ReservationUncertain);
            });
            Add("disabled_execution_preserves_uncertainty_until_proven_post_exit_capture", async token =>
            {
                using UsageEnforcementFixture fixture = await UsageEnforcementFixture.CreateAsync(20, token).ConfigureAwait(false);
                AssertTrue(await fixture.Database.CaptainSelections.TryAdmitLegacyAsync(fixture.Captains[0].Id, fixture.Missions[0].Id, "host", token: token).ConfigureAwait(false));
                AssertTrue(await fixture.Database.CaptainSelections.MarkLaunchAttemptedAsync(fixture.Tenant.Id, fixture.Missions[0].Id, fixture.Captains[0].Id, await fixture.ExecutionKeyAsync(0, token).ConfigureAwait(false), token).ConfigureAwait(false));
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
                AssertFalse(adopted.AccountAttributionVerified, "present membership cannot prove an older execution used those credentials");
                AssertFalse(await fixture.Database.CaptainSelections.ReleaseUnstartedAsync(fixture.Tenant.Id, fixture.Missions[0].Id, fixture.Captains[0].Id, await fixture.ExecutionKeyAsync(0, token).ConfigureAwait(false), token).ConfigureAwait(false), "startup adoption is never proof that a launcher was not called");
                Mission stopped = (await fixture.Database.Missions.ReadAsync(fixture.Missions[0].Id, token).ConfigureAwait(false))!;
                stopped.Status = MissionStatusEnum.Cancelled; await fixture.Database.Missions.UpdateAsync(stopped, token).ConfigureAwait(false);
                AssertFalse(await fixture.Database.CaptainSelections.ReleaseStoppedUnstartedAsync(fixture.Tenant.Id, adopted.ExecutionKey, token).ConfigureAwait(false));
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

        private static async Task Waiting(UsageEnforcementFixture fixture, int index, string reason, DateTime evaluatedUtc, UsageSelectionModeEnum mode, CancellationToken token)
        {
            UsageSelectionSettings policy = new UsageSelectionSettings { Mode = mode, Strategy = fixture.Policy.Strategy };
            CaptainSelectionRecordSet records = new CaptainSelectionRecordSet { Policy = CaptainSelectionPolicyRevision.FromSettings(fixture.Tenant.Id, policy) };
            CaptainSelectionDecision decision = records.Decision;
            decision.TenantId = fixture.Tenant.Id; decision.UserId = fixture.Owner.Id; decision.MissionId = fixture.Missions[index].Id;
            decision.ExecutionAttemptId = decision.Id; decision.PolicyRevisionId = records.Policy.Id; decision.Mode = mode;
            decision.Outcome = CaptainSelectionOutcomes.Waiting; decision.Trigger = "Dispatch"; decision.EvaluatedUtc = evaluatedUtc;
            decision.AlgorithmVersion = UsageSelectionPolicy.AlgorithmVersion; decision.PrimaryReasonCode = reason; decision.EvidenceFingerprint = reason + evaluatedUtc.Ticks;
            await fixture.Database.CaptainSelections.CreateDecisionAsync(records, token).ConfigureAwait(false);
        }
    }
}
