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
                CaptainEligibilityContext eligibility = await BuildCaptainEligibilityContextAsync(mission, token).ConfigureAwait(false);
                if (CaptainIneligibilityReason(current, mission, eligibility) != null) return false;
                List<Captain> admitted = await ApplyCapacityLimitsAsync(new List<Captain> { current }, mission, token).ConfigureAwait(false);
                if (admitted.Count == 0) return false;
                if (CanAssignMissionAsync != null && !await CanAssignMissionAsync(mission, current).ConfigureAwait(false)) return false;
                return await _Database.Captains.TryResumeVerificationAsync(current.Id, mission.Id, mission.LastUpdateUtc,
                    _Settings.Concurrency.MaxCaptainConcurrency, token).ConfigureAwait(false);
            }
            finally { _CapacityLock.Release(); }
        }
    }
}
