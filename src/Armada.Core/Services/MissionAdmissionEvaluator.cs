namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>Shared safety and capacity explanations used by dispatch and waiting diagnostics.</summary>
    public static class MissionAdmissionEvaluator
    {
        /// <summary>Evaluate a pipeline dependency before safety or capacity gates.</summary>
        public static MissionAssignmentBlocker? Dependency(Mission candidate, Mission? dependency, bool handoffPrepared)
        {
            if (String.IsNullOrEmpty(candidate.DependsOnMissionId)) return null;
            MissionAssignmentBlocker blocker = new MissionAssignmentBlocker { DependsOnMissionId = candidate.DependsOnMissionId };
            if (dependency == null)
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.DependencyNotFinished;
                blocker.Summary = "Waiting for mission " + candidate.DependsOnMissionId + ", which no longer exists.";
            }
            else if (dependency.Status != MissionStatusEnum.Complete && dependency.Status != MissionStatusEnum.WorkProduced)
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.DependencyNotFinished;
                blocker.Summary = "Waiting for mission '" + dependency.Title + "' (" + dependency.Status + ") to finish.";
            }
            else if (dependency.Status == MissionStatusEnum.WorkProduced && !handoffPrepared)
            {
                blocker.Reason = MissionAssignmentBlockerReasonEnum.DependencyHandoffPending;
                blocker.Summary = "Mission '" + dependency.Title + "' finished; Armada is preparing the handoff to this stage.";
            }
            else return null;
            return blocker;
        }

        /// <summary>Evaluate running branch/broad-scope locks in their canonical order.</summary>
        public static MissionAssignmentBlocker? RunningLocks(Mission candidate, Vessel vessel,
            IReadOnlyList<Mission> running, IReadOnlyList<Mission> broad, bool candidateIsBroad)
        {
            MissionAssignmentBlocker? result = null;
            IEnumerable<Mission> holders = running;
            if (broad.Count > 0)
            {
                result = new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.VesselBroadScopeMissionActive,
                    Summary = "Broad-scope mission '" + broad[0].Title + "' holds vessel " + vessel.Name + " until it finishes." };
                holders = broad;
            }
            else if (candidateIsBroad && running.Count > 0)
                result = new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.BroadScopeWaitingForVessel,
                    Summary = "This is a broad-scope mission; it waits until vessel " + vessel.Name + " has no active missions (" + running.Count + " running)." };
            else if (!String.IsNullOrEmpty(candidate.BranchName))
            {
                List<Mission> branch = running.Where(m => String.Equals(m.BranchName, candidate.BranchName, StringComparison.Ordinal)).ToList();
                if (branch.Count > 0)
                {
                    result = new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.BranchInUse,
                        Summary = "Branch " + candidate.BranchName + " is in use by '" + branch[0].Title + "'. This mission continues that branch and starts when it finishes." };
                    holders = branch;
                }
            }
            if (result != null)
            {
                result.SetBlockingMissions(holders.Select(m => m.Id));
                foreach (Mission holder in holders.Take(MissionAssignmentBlocker.MaxListed))
                    result.Advice.Add(new AssignmentBlockerAdvice { Text = "Inspect mission " + holder.Id + " (" + holder.Status + ") before starting overlapping work.",
                        Action = AssignmentBlockerActionEnum.OpenMission, TargetType = "mission", TargetId = holder.Id });
            }
            return result;
        }

        /// <summary>Describe a full numeric gate, including a non-preemptive lowered limit.</summary>
        public static MissionAssignmentBlocker Capacity(MissionAssignmentBlockerReasonEnum reason, string name, int running, int limit,
            AssignmentBlockerActionEnum action, string targetType, string? targetId, string setting, int ceiling = Int32.MaxValue)
        {
            string label = setting switch
            {
                "MaxParallelVoyages" => "maximum parallel voyages",
                "MaxParallelMissionsPerVoyage" => "maximum parallel missions per voyage",
                "MaxConcurrency" => "maximum concurrent missions",
                "MaxConcurrentMissions" => "global maximum concurrent missions",
                "InferenceOptions.MaxConcurrentMissions" => "endpoint maximum concurrent missions",
                _ when setting.StartsWith("Agents.", StringComparison.Ordinal) => "runtime maximum concurrent missions",
                _ => setting
            };
            int required = running == Int32.MaxValue ? Int32.MaxValue : running + 1;
            int? suggested = required <= ceiling && required > limit ? required : null;
            MissionAssignmentBlocker result = new MissionAssignmentBlocker { Reason = reason,
                Summary = name + " has " + running + " running / limit " + limit + ". "
                    + (running > limit ? "The limit is below current usage; existing work continues and new missions wait until usage falls below the limit." : "A slot opens when an agent finishes.") };
            result.Advice.Add(new AssignmentBlockerAdvice { Text = suggested.HasValue
                    ? "To admit one more mission now, raise " + label + " from " + limit + " to " + suggested + "."
                    : "Wait for running work to finish; no supported increase to " + label + " admits another mission now.",
                Action = suggested.HasValue ? action : null, TargetType = targetType, TargetId = targetId,
                Setting = setting, CurrentValue = limit, SuggestedValue = suggested });
            return result;
        }
    }
}
