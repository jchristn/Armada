namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>Deterministic vessel admission, shared by dispatch and waiting diagnostics.</summary>
    public static class VesselAdmission
    {
        /// <summary>Evaluate voyage occupancy and running mission capacity without mutating state.</summary>
        public static MissionAssignmentBlocker? Evaluate(Mission candidate, Vessel vessel,
            IEnumerable<Mission> missions, IEnumerable<Voyage> voyages)
        {
            Dictionary<string, VoyageStatusEnum> statuses = voyages.GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.First().Status);
            return EvaluateSnapshots(candidate, vessel, missions.Select(m =>
            {
                MissionAdmissionSnapshot snapshot = MissionAdmissionSnapshot.FromMission(m);
                if (m.VoyageId != null && statuses.TryGetValue(m.VoyageId, out VoyageStatusEnum status)) snapshot.VoyageStatus = status;
                return snapshot;
            }));
        }

        /// <summary>Evaluate storage projections without loading mission artifacts or voyage descriptions.</summary>
        public static MissionAssignmentBlocker? EvaluateSnapshots(Mission candidate, Vessel vessel, IEnumerable<MissionAdmissionSnapshot> missions)
        {
            List<MissionAdmissionSnapshot> all = missions.Where(m => m.VoyageStatus != VoyageStatusEnum.Complete
                && m.VoyageStatus != VoyageStatusEnum.Failed && m.VoyageStatus != VoyageStatusEnum.Cancelled).ToList();
            List<MissionAdmissionSnapshot> others = all.Where(m => m.Id != candidate.Id).ToList();
            Dictionary<string, MissionAdmissionSnapshot> byId = all.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            string Group(MissionAdmissionSnapshot mission)
            {
                if (!String.IsNullOrEmpty(mission.VoyageId)) return "voyage:" + mission.VoyageId;
                // A standalone pipeline is one unit of work. Its review stages must be able to
                // follow WorkProduced even when the vessel admits only one voyage group.
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                while (!String.IsNullOrEmpty(mission.DependsOnMissionId) && seen.Add(mission.Id)
                    && byId.TryGetValue(mission.DependsOnMissionId, out MissionAdmissionSnapshot? upstream)
                    && String.IsNullOrEmpty(upstream.VoyageId)) mission = upstream;
                return "mission:" + mission.Id;
            }
            string key = Group(MissionAdmissionSnapshot.FromMission(candidate));
            List<IGrouping<string, MissionAdmissionSnapshot>> admitted = all.GroupBy(Group)
                .Where(g => g.Any(m => HoldsVoyage(m.Status))
                    && g.Any(m => m.Status != MissionStatusEnum.Pending)).ToList();
            bool alreadyAdmitted = admitted.Any(g => g.Key == key);
            if (!alreadyAdmitted && vessel.MaxParallelVoyages > 0 && admitted.Count >= vessel.MaxParallelVoyages)
            {
                MissionAssignmentBlocker result = new MissionAssignmentBlocker
                {
                    Reason = MissionAssignmentBlockerReasonEnum.VesselVoyageLimit,
                    Summary = "Vessel " + vessel.Name + " has " + admitted.Count + " of " + vessel.MaxParallelVoyages
                        + " voyage slots occupied. Existing voyages retain their slot through review and landing."
                };
                result.SetBlockingMissions(admitted.SelectMany(g => g).Where(m => HoldsVoyage(m.Status)).Select(m => m.Id));
                return result;
            }
            List<MissionAdmissionSnapshot> running = others.Where(m => Group(m) == key &&
                (m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress)).ToList();
            if (vessel.MaxParallelMissionsPerVoyage > 0 && running.Count >= vessel.MaxParallelMissionsPerVoyage)
            {
                MissionAssignmentBlocker result = new MissionAssignmentBlocker
                {
                    Reason = MissionAssignmentBlockerReasonEnum.VoyageMissionLimit,
                    Summary = "This voyage has " + running.Count + " of " + vessel.MaxParallelMissionsPerVoyage
                        + " running mission slots occupied. A slot opens when an agent finishes."
                };
                result.SetBlockingMissions(running.Select(m => m.Id));
                return result;
            }
            return null;
        }

        private static bool HoldsVoyage(MissionStatusEnum status) => status == MissionStatusEnum.Pending
            || status == MissionStatusEnum.Assigned || status == MissionStatusEnum.InProgress
            || status == MissionStatusEnum.WorkProduced || status == MissionStatusEnum.PullRequestOpen
            || status == MissionStatusEnum.Testing || status == MissionStatusEnum.Review
            || status == MissionStatusEnum.Verifying;
    }
}
