namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    public partial class MissionService
    {
        /// <inheritdoc />
        public async Task<bool> IsAdmissionReadyForMonitoringAsync(Mission mission, CancellationToken token = default)
        {
            if (_Captains.OnLaunchAgent == null || mission.Status != MissionStatusEnum.Pending || _InFlightAssignments.ContainsKey(mission.Id)
                || _LaunchFailures.ContainsKey(mission.Id) || _ProvisionFailureCaptures.ContainsKey(mission.Id)) return false;
            Mission view = mission.AdmissionView();
            CaptainAdmissionSelection? selection = null;
            MissionAssignmentBlocker? blocker = await GetAssignmentBlockerCoreAsync(view, token, value => selection = value).ConfigureAwait(false);
            if (blocker?.Reason != MissionAssignmentBlockerReasonEnum.AwaitingDispatch) return false;
            if (!String.IsNullOrEmpty(view.VoyageId))
            {
                Voyage? voyage = await _Database.Voyages.ReadAsync(view.VoyageId, token).ConfigureAwait(false);
                if (voyage?.Status == VoyageStatusEnum.Complete || voyage?.Status == VoyageStatusEnum.Cancelled || voyage?.Status == VoyageStatusEnum.Failed) return false;
            }
            Captain? captain = selection?.Selected;
            if (selection != null) view = selection.Mission;
            if (captain == null || CanAssignMissionAsync != null && !await CanAssignMissionAsync(view, captain).ConfigureAwait(false)) return false;
            Vessel? vessel = String.IsNullOrEmpty(view.VesselId) ? null : await _Database.Vessels.ReadAsync(view.VesselId, token).ConfigureAwait(false);
            if (vessel == null) return false;
            if (ResolveDockPlacementAsync != null && (await ResolveDockPlacementAsync(view, captain, vessel).ConfigureAwait(false)).Wait) return false;
            return !_InFlightAssignments.ContainsKey(mission.Id);
        }
    }
}
