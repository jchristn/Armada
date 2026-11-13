namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    public partial class MissionService
    {
        private async Task<MissionAssignmentBlocker?> EvaluateSafetyAdmissionAsync(Mission mission, Vessel vessel, CancellationToken token)
        {
            List<Mission> running = (await ActiveMissionsOnVesselAsync(vessel.Id, token).ConfigureAwait(false))
                .Where(m => m.Id != mission.Id && (m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress || m.Status == MissionStatusEnum.Verifying)).ToList();
            MissionAssignmentBlocker? safety = MissionAdmissionEvaluator.RunningLocks(mission, vessel, running,
                running.Where(IsBroadScope).ToList(), IsBroadScope(mission));
            return safety ?? await EvaluateVesselAdmissionAsync(mission, vessel, token).ConfigureAwait(false);
        }

        private static MissionAssignmentBlocker EndpointUnavailable(ModelEndpoint endpoint, string reason)
        {
            MissionAssignmentBlocker result = new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.EndpointUnavailable,
                Summary = "Endpoint " + endpoint.Name + " is " + reason + ". Increasing concurrency will not make it available." };
            result.Advice.Add(new AssignmentBlockerAdvice { Text = "Review the endpoint's enabled state and health before retrying.",
                Action = AssignmentBlockerActionEnum.EditEndpoint, TargetType = "endpoint", TargetId = endpoint.Id });
            return result;
        }

        private void AddCaptainCapacityAdvice(MissionAssignmentBlocker blocker, List<Captain> full)
        {
            blocker.Advice.Add(new AssignmentBlockerAdvice { Text = "Add another eligible captain to run work in parallel.",
                Action = AssignmentBlockerActionEnum.AddCaptain, TargetType = "captain" });
            foreach (Captain captain in full.Take(MissionAssignmentBlocker.MaxListed - 1))
            {
                if (captain.Runtime != AgentRuntimeEnum.Mux && captain.Runtime != AgentRuntimeEnum.ApiEndpoint) continue;
                int effective = Math.Min(captain.MaxConcurrency, _Settings.Concurrency.MaxCaptainConcurrency);
                AssignmentBlockerAdvice advice = MissionAdmissionEvaluator.Capacity(MissionAssignmentBlockerReasonEnum.CaptainConcurrencyLimit,
                    captain.Name, captain.ActiveMissionCount, effective, AssignmentBlockerActionEnum.EditCaptain,
                    "captain", captain.Id, "MaxConcurrency", _Settings.Concurrency.MaxCaptainConcurrency).Advice[0];
                advice.Text = captain.Name + ": " + advice.Text;
                if (!advice.SuggestedValue.HasValue && effective == _Settings.Concurrency.MaxCaptainConcurrency)
                {
                    advice.Text = captain.Name + " is capped by the server's MaxCaptainConcurrency (" + effective + "). Update settings.json or add a captain.";
                    advice.Action = null;
                    advice.Setting = "Concurrency.MaxCaptainConcurrency";
                }
                blocker.Advice.Add(advice);
            }
        }
    }
}
