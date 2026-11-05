namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    public partial class MissionService
    {
        /// <inheritdoc />
        public Task<int> CloseInterruptedWaitsAsync(CancellationToken token = default) => _WaitTracker.CloseInterruptedAsync(token);

        /// <inheritdoc />
        public Task ReconcileWaitsAsync(IEnumerable<string> pendingMissionIds, DateTime snapshotUtc, CancellationToken token = default)
        {
            if (pendingMissionIds == null) throw new ArgumentNullException(nameof(pendingMissionIds));
            return _WaitTracker.ReconcileAsync(pendingMissionIds, snapshotUtc, token);
        }

        /// <summary>
        /// Wait for background dock provisioning and launches started by <see cref="TryAssignAsync(Mission, Vessel, bool, CancellationToken)"/>
        /// to finish, including their failure handling.
        /// </summary>
        /// <param name="timeout">Longest time to wait.</param>
        /// <returns>True when none remain in flight.</returns>
        public async Task<bool> WaitForBackgroundLaunchesAsync(TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (!_BackgroundLaunches.IsEmpty)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) return false;
                Task all = Task.WhenAll(_BackgroundLaunches.Values.ToArray());
                await Task.WhenAny(all, Task.Delay(remaining)).ConfigureAwait(false);
                await Task.Yield();
            }
            return true;
        }

        private void NoteWait(Mission mission, MissionAssignmentBlocker blocker)
            => NoteWait(mission, MissionWaitGates.FromBlocker(blocker.Reason), blocker.SelectionReasonCode ?? blocker.Reason.ToString());

        private void NoteWait(Mission mission, MissionWaitGateEnum gate, string? detail)
            => _AttemptWaits[mission.Id] = new MissionWaitObservation { Mission = mission, Gate = gate, Detail = detail };

        private void NoteLeftPending(Mission mission)
            => _AttemptWaits[mission.Id] = new MissionWaitObservation { Mission = mission };

        // Runs after the attempt released its admission locks, so interval writes never extend a lock hold.
        private async Task RecordAttemptWaitAsync(string missionId, CancellationToken token)
        {
            if (!_AttemptWaits.TryRemove(missionId, out MissionWaitObservation? observation)) return;
            try
            {
                if (observation.Gate.HasValue)
                    await _WaitTracker.ObserveWaitAsync(observation.Mission, observation.Gate.Value, observation.Detail, token).ConfigureAwait(false);
                else
                    await _WaitTracker.ReleaseAsync(missionId, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not record the wait of mission " + missionId + ": " + ex.Message);
            }
        }
    }
}
