namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    public partial class AdmiralService
    {
        private async Task<List<Captain>> ExpandAssignmentsAsync(Captain captain, CancellationToken token)
        {
            if (captain.ActiveMissionCount == 0) return new List<Captain> { captain };
            List<Mission> missions = await _Database.Missions.EnumerateByCaptainAsync(captain.Id, token).ConfigureAwait(false);
            List<Captain> result = new List<Captain>();
            foreach (Mission mission in missions.Where(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress || m.Status == MissionStatusEnum.Verifying))
            {
                Captain scoped = captain.ForMission(mission);
                if (mission.Status == MissionStatusEnum.Verifying && !scoped.ProcessId.HasValue)
                    scoped.ProcessId = await _Database.Captains.ReadMissionProcessAsync(captain.Id, mission.Id, token).ConfigureAwait(false);
                result.Add(scoped);
            }
            return result;
        }

        private async Task HealthCheckAssignmentsAsync(Captain captain, CancellationToken token)
        {
            List<Captain> assignments = await ExpandAssignmentsAsync(captain, token).ConfigureAwait(false);
            foreach (Captain assignment in assignments)
            {
                try { await HealthCheckCaptainAsync(assignment, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) { _Logging.Warn(_Header + "mission health check " + assignment.CurrentMissionId + " failed: " + ex.Message); }
            }
            if (assignments.Count > 1 && assignments.All(c => c.State == CaptainStateEnum.Stalled))
                await _Database.Captains.UpdateStateAsync(captain.Id, CaptainStateEnum.Stalled, token).ConfigureAwait(false);
        }
    }
}
