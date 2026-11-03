namespace Armada.Core.Services
{
    using Armada.Core.Models;
    using Armada.Core.Enums;

    public partial class MissionService
    {
        /// <inheritdoc />
        public async Task<bool> TryAdmitVerificationResumeAsync(Mission mission, Captain captain, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(mission.VesselId)) return false;
            Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            if (vessel == null) return false;
            if (!String.IsNullOrEmpty(mission.VoyageId))
            {
                Voyage? voyage = await _Database.Voyages.ReadAsync(mission.VoyageId, token).ConfigureAwait(false);
                if (voyage?.Status == VoyageStatusEnum.Complete || voyage?.Status == VoyageStatusEnum.Cancelled || voyage?.Status == VoyageStatusEnum.Failed) return false;
            }
            TimeSpan timeout = TimeSpan.FromSeconds(_Settings.Concurrency.AdmissionLockTimeoutSeconds);
            using IDisposable? vesselLease = await _VesselAdmissionLocks.TryEnterAsync(vessel.Id, timeout, token).ConfigureAwait(false);
            if (vesselLease == null || await EvaluateSafetyAdmissionAsync(mission, vessel, token).ConfigureAwait(false) != null) return false;
            if (!await _CapacityLock.WaitAsync(timeout, token).ConfigureAwait(false)) return false;
            try
            {
                if (await EvaluateGlobalAdmissionAsync(token).ConfigureAwait(false) != null) return false;
                Captain? current = await _Database.Captains.ReadAsync(captain.Id, token).ConfigureAwait(false);
                if (current == null || !current.HasMissionCapacity || !CaptainVisibleToMission(current, mission)
                    || current.QuarantineUntilUtc > DateTime.UtcNow) return false;
                Mission routingView = mission.AdmissionView();
                await ResolvePreferredCaptainAsync(routingView, token, persist: false).ConfigureAwait(false);
                CaptainEligibilityContext eligibility = await BuildCaptainEligibilityContextAsync(routingView, token).ConfigureAwait(false);
                if (CaptainIneligibilityReason(current, routingView, eligibility) != null) return false;
                List<Captain> admitted = await ApplyCapacityLimitsAsync(new List<Captain> { current }, routingView, token).ConfigureAwait(false);
                if (admitted.Count == 0) return false;
                if (CanAssignMissionAsync != null && !await CanAssignMissionAsync(routingView, current).ConfigureAwait(false)) return false;
                if (_Settings.EffectiveUsageSelection.Mode == UsageSelectionModeEnum.Enforce && String.IsNullOrWhiteSpace(mission.TenantId)) return false;
                if (_Settings.EffectiveUsageSelection.Mode != UsageSelectionModeEnum.Disabled && !String.IsNullOrEmpty(mission.TenantId))
                {
                    CaptainAdmissionSelection repair = new CaptainAdmissionSelection { Mission = routingView, Eligibility = eligibility,
                        Visible = new List<Captain> { current }, Available = new List<Captain> { current }, Selected = current,
                        RoutingBuckets = new List<CaptainRoutingBucket> { new CaptainRoutingBucket { ReasonCode = "RepairCaptain", Captains = new List<Captain> { current } } } };
                    UsageSelectionSnapshot snapshot = await BuildUsageSelectionAsync(repair, _Settings.EffectiveUsageSelection.Copy(), token).ConfigureAwait(false);
                    if (snapshot.Settings.Mode == UsageSelectionModeEnum.Enforce && snapshot.Evaluation.SelectedCaptainId != current.Id) return false;
                    if (UsagePolicyKey() != System.Text.Json.JsonSerializer.Serialize(snapshot.Settings)) return false;
                    string? executionHost = mission.AssignedHarborId ?? UsageSelectionLocalHostId;
                    UsageSelectionCandidate? repairUsage = snapshot.Evaluation.Candidates.SingleOrDefault(c => c.Candidate.CaptainId == current.Id)?.Candidate;
                    if (snapshot.Settings.Mode == UsageSelectionModeEnum.Enforce && (String.IsNullOrWhiteSpace(executionHost)
                        || repairUsage?.IdentityVerified == true && repairUsage.ConfigurationMatches && snapshot.Evidence.TryGetValue(current.Id, out UsageSelectionAccountEvidence? observed) && observed.HostId != executionHost)) return false;
                    CaptainSelectionRecordSet records = UsageSelectionRecordBuilder.Build(snapshot, mission, "Repair", admitted: true);
                    records.AdmissionHostId = executionHost;
                    CaptainSelectionAdmissionReceipt receipt = snapshot.Settings.Mode == UsageSelectionModeEnum.Enforce
                        ? await _Database.CaptainSelections.TryAdmitEnforcedRepairAsync(records, mission.LastUpdateUtc, _Settings.Concurrency.MaxCaptainConcurrency, token).ConfigureAwait(false)
                        : await _Database.CaptainSelections.TryAdmitObservedRepairAsync(records, mission.LastUpdateUtc, _Settings.Concurrency.MaxCaptainConcurrency, token).ConfigureAwait(false);
                    return receipt.Claimed;
                }
                return await _Database.CaptainSelections.TryAdmitLegacyRepairAsync(current.Id, mission.Id, mission.LastUpdateUtc, mission.AssignedHarborId ?? UsageSelectionLocalHostId,
                    _Settings.Concurrency.MaxCaptainConcurrency, token).ConfigureAwait(false);
            }
            finally { _CapacityLock.Release(); }
        }
    }
}
