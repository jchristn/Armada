namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    public partial class MissionService
    {
        /// <summary>Called after a <c>mission.starving</c> event is recorded, for live delivery.</summary>
        public Action<ArmadaEvent>? OnConcurrencyEvent
        {
            get => _WaitTracker.EventRecorded;
            set => _WaitTracker.EventRecorded = value;
        }

        /// <summary>Called when a Pending mission starts waiting on a different gate, for live delivery.</summary>
        public Action<MissionWaitGateChange>? OnWaitingGateChanged
        {
            get => _WaitTracker.GateChanged;
            set => _WaitTracker.GateChanged = value;
        }

        /// <inheritdoc />
        public Task<int> CloseInterruptedWaitsAsync(CancellationToken token = default) => _WaitTracker.CloseInterruptedAsync(token);

        /// <inheritdoc />
        public Task ReconcileWaitsAsync(IEnumerable<string> pendingMissionIds, DateTime snapshotUtc, CancellationToken token = default)
        {
            if (pendingMissionIds == null) throw new ArgumentNullException(nameof(pendingMissionIds));
            HashSet<string> pending = new HashSet<string>(pendingMissionIds, StringComparer.Ordinal);
            // A result belongs to a Pending mission; one that left Pending outside admission is dropped here.
            _AdmissionResults.DropNotPending(pending, snapshotUtc);
            return _WaitTracker.ReconcileAsync(pending, snapshotUtc, token);
        }

        /// <summary>
        /// Wait for background dock provisioning and launches started by <see cref="TryAssignAsync(Mission, Vessel, bool, CancellationToken)"/>
        /// to finish, including their failure handling.
        /// </summary>
        /// <param name="timeout">Longest time to wait.</param>
        /// <returns>True when none remain in flight.</returns>
        public Task<bool> WaitForBackgroundLaunchesAsync(TimeSpan timeout) => _BackgroundLaunches.WaitAllAsync(timeout);

        /// <inheritdoc />
        public MissionAdmissionResult? GetAdmissionResult(string missionId)
            => _AdmissionResults.Get(missionId);

        private void NoteWait(Mission mission, MissionAssignmentBlocker blocker)
            => NoteWait(mission, blocker, MissionWaitGates.FromBlocker(blocker.Reason), blocker.SelectionReasonCode ?? blocker.Reason.ToString());

        // The wait tracker keeps its own gate and detail; the blocker is the one typed result every reader sees.
        private void NoteWait(Mission mission, MissionAssignmentBlocker blocker, MissionWaitGateEnum? gate, string? detail)
            => _AdmissionResults.NoteAttempt(new MissionWaitObservation { Mission = mission, Gate = gate, Detail = detail, Blocker = blocker });

        // Every gate was open but the attempt did not claim a captain (a lock timeout, a lost claim, a policy change).
        private void NoteUnadmitted(Mission mission, string summary)
            => NoteWait(mission, new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.AwaitingDispatch, Summary = summary }, null, null);

        private static MissionAssignmentBlocker LaunchFailingBlocker(MissionLaunchFailure failure) => new MissionAssignmentBlocker
        {
            Reason = MissionAssignmentBlockerReasonEnum.LaunchFailing,
            UntilUtc = failure.NextAttemptUtc,
            Summary = "Starting the captain failed " + failure.Count + (failure.Count == 1 ? " time" : " times")
                + ": " + failure.Reason + " Armada tries again at " + failure.NextAttemptUtc.ToString("HH:mm:ss") + " UTC."
        };

        private void RequestDispatch()
        {
            try { DispatchRequested?.Invoke(); }
            catch (Exception ex) { _Logging.Warn(_Header + "dispatch wake failed: " + ex.Message); }
        }

        // Wake the dispatcher when a breaker reopens to new missions, so its waiting missions do not wait for the heartbeat.
        private void ScheduleDispatchAfter(TimeSpan delay)
        {
            CancellationToken shutdown = ShutdownToken;
            _ = Task.Delay(delay + TimeSpan.FromSeconds(1), shutdown).ContinueWith(t => { if (!t.IsCanceled) RequestDispatch(); }, TaskScheduler.Default);
        }

        private void NoteLeftPending(Mission mission)
        {
            _AdmissionResults.NoteAttempt(new MissionWaitObservation { Mission = mission, LeftPending = true });
            PassStateChanged();
        }

        // Runs after the attempt released its admission locks, so interval writes never extend a lock hold.
        private async Task RecordAttemptWaitAsync(Mission mission, bool assigned, CancellationToken token)
        {
            string missionId = mission.Id;
            if (!_AdmissionResults.TryTakeAttempt(missionId, out MissionWaitObservation? taken))
            {
                // No gate spoke: the mission was assigned, or it failed after the claim. An older result is stale either way.
                if (!assigned && _LaunchBackoff.TryGet(missionId, out MissionLaunchFailure? failure))
                    _AdmissionResults.Record(new MissionWaitObservation { Mission = mission, Blocker = LaunchFailingBlocker(failure) });
                else _AdmissionResults.Remove(missionId);
                return;
            }
            MissionWaitObservation observation = taken;
            _AdmissionResults.Record(observation);
            try
            {
                if (observation.Gate.HasValue)
                    await _WaitTracker.ObserveWaitAsync(observation.Mission, observation.Gate.Value, observation.Detail, token).ConfigureAwait(false);
                else if (observation.LeftPending)
                    await _WaitTracker.ReleaseAsync(missionId, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not record the wait of mission " + missionId + ": " + ex.Message);
            }
        }
    }
}
