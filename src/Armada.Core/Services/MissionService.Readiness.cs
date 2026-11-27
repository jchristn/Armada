namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    public partial class MissionService
    {
        // Readiness never claims capacity or mutates missions. Dispatch alone applies a failed-slice
        // transition; diagnostics use the exact same observation without side effects.
        private async Task<ReadinessAdmissionOutcome> EvaluateReadinessAdmissionAsync(
            Mission mission, CancellationToken token)
        {
            if (!String.IsNullOrEmpty(mission.DependsOnMissionId))
            {
                Mission? dependency = await _Database.Missions.ReadAsync(mission.DependsOnMissionId, token).ConfigureAwait(false);
                MissionAssignmentBlocker? dependencyBlocker = MissionAdmissionEvaluator.Dependency(mission, dependency,
                    dependency != null && IsPipelineHandoffPrepared(mission, dependency));
                if (dependencyBlocker != null) return new ReadinessAdmissionOutcome(dependencyBlocker);
            }

            HierarchyReadiness hierarchy = await EvaluateParentReadinessAsync(mission, token).ConfigureAwait(false);
            if (hierarchy.Blocker != null) return new ReadinessAdmissionOutcome(hierarchy.Blocker);

            IntegrationBarrierState barrier = await EvaluateIntegrationBarrierAsync(mission, token).ConfigureAwait(false);
            if (barrier.FailedMission != null)
            {
                Mission failed = barrier.FailedMission;
                string transition = _Settings.BlockDependentsOfFailedStages ? "blocked" : "cancelled";
                MissionAssignmentBlocker blocker = new MissionAssignmentBlocker
                {
                    Reason = MissionAssignmentBlockerReasonEnum.WaitingForVoyageWorkers,
                    Summary = "Mission '" + failed.Title + "' in another slice ended " + failed.Status
                        + "; this integration will not run without it and is " + transition
                        + " on the next dispatch pass. Restart that mission to continue."
                };
                blocker.SetBlockingMissions(new[] { failed.Id });
                return new ReadinessAdmissionOutcome(blocker, failed);
            }
            if (barrier.Waiting)
                return new ReadinessAdmissionOutcome(new MissionAssignmentBlocker
                {
                    Reason = MissionAssignmentBlockerReasonEnum.WaitingForVoyageWorkers,
                    Summary = barrier.WaitingSummary ?? "Waiting for the voyage's other implementation pipelines, including reviews and checks, to settle."
                });
            return ReadinessAdmissionOutcome.Ready;
        }
    }
}
