namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>Shared sticky voyage occupancy for admission and invariant checks.</summary>
    internal static class VoyageSlotPolicy
    {
        internal static List<MissionAdmissionSnapshot> Runnable(IEnumerable<MissionAdmissionSnapshot> missions) =>
            missions.Where(m => m.VoyageStatus != VoyageStatusEnum.Complete
                && m.VoyageStatus != VoyageStatusEnum.Failed && m.VoyageStatus != VoyageStatusEnum.Cancelled).ToList();

        internal static string Group(MissionAdmissionSnapshot mission, IReadOnlyDictionary<string, MissionAdmissionSnapshot> byId)
        {
            if (!String.IsNullOrEmpty(mission.VoyageId)) return "voyage:" + mission.VoyageId;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            while (!String.IsNullOrEmpty(mission.DependsOnMissionId) && seen.Add(mission.Id)
                && byId.TryGetValue(mission.DependsOnMissionId, out MissionAdmissionSnapshot? upstream)
                && String.IsNullOrEmpty(upstream.VoyageId)) mission = upstream;
            return "mission:" + mission.Id;
        }

        internal static bool Admitted(IEnumerable<MissionAdmissionSnapshot> group) =>
            group.Any(m => HoldsVoyage(m.Status)) && group.Any(m => m.Status != MissionStatusEnum.Pending);

        internal static bool Running(MissionStatusEnum status) => status == MissionStatusEnum.Assigned || status == MissionStatusEnum.InProgress;

        internal static bool HoldsVoyage(MissionStatusEnum status) => status == MissionStatusEnum.Pending
            || Running(status) || status == MissionStatusEnum.WorkProduced || status == MissionStatusEnum.PullRequestOpen
            || status == MissionStatusEnum.Testing || status == MissionStatusEnum.Review || status == MissionStatusEnum.Verifying;
    }
}
