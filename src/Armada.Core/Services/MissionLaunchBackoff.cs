namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;
    using System.Threading;
    using Armada.Core.Models;

    /// <summary>
    /// Launch failures per mission and the backoff they impose (RS-3.7), kept across a restart in the recovery state
    /// store. The only owner of launch failure state.
    /// </summary>
    internal sealed class MissionLaunchBackoff
    {
        private static readonly TimeSpan _Retention = TimeSpan.FromDays(1);

        private readonly ConcurrentDictionary<string, MissionLaunchFailure> _Failures = new ConcurrentDictionary<string, MissionLaunchFailure>(StringComparer.Ordinal);
        private readonly RecoveryStateStore _Store;

        /// <summary>Instantiate.</summary>
        /// <param name="store">Recovery state store the failures persist to.</param>
        /// <exception cref="ArgumentNullException">Thrown when store is null.</exception>
        internal MissionLaunchBackoff(RecoveryStateStore store)
        {
            _Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>The recorded launch failures of a mission, if any.</summary>
        /// <param name="missionId">Mission ID.</param>
        /// <param name="failure">The failure record.</param>
        /// <returns>True when the mission has one.</returns>
        internal bool TryGet(string missionId, [NotNullWhen(true)] out MissionLaunchFailure? failure)
        {
            return _Failures.TryGetValue(missionId, out failure);
        }

        /// <summary>Count one failed launch of a mission on a captain and set its next attempt time.</summary>
        /// <param name="missionId">Mission ID.</param>
        /// <param name="captainId">Captain whose launch failed.</param>
        /// <param name="reason">Failure reason.</param>
        /// <param name="delaySeconds">Seconds until the next attempt.</param>
        /// <returns>The updated failure record.</returns>
        internal MissionLaunchFailure Record(string missionId, string captainId, string reason, out double delaySeconds)
        {
            MissionLaunchFailure failure = _Failures.AddOrUpdate(missionId,
                _ => new MissionLaunchFailure { Count = 1 },
                (_, existing) => { existing.Count++; return existing; });
            lock (failure.FailedCaptainIds) failure.FailedCaptainIds.Add(captainId);
            failure.Reason = reason.Trim().TrimEnd('.') + ".";
            failure.LastUtc = DateTime.UtcNow;
            // 30s, 60s, 2m, 4m, then every 5 minutes.
            delaySeconds = Math.Min(300, 30 * Math.Pow(2, Math.Max(0, failure.Count - 1)));
            failure.NextAttemptUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
            return failure;
        }

        /// <summary>Persist a mission's failure record.</summary>
        /// <param name="missionId">Mission ID.</param>
        /// <param name="failure">The failure record.</param>
        /// <returns>Task.</returns>
        internal async Task SaveAsync(string missionId, MissionLaunchFailure failure)
        {
            MissionLaunchFailure snapshot = new MissionLaunchFailure();
            snapshot.Count = failure.Count;
            snapshot.Reason = failure.Reason;
            snapshot.LastUtc = failure.LastUtc;
            snapshot.NextAttemptUtc = failure.NextAttemptUtc;
            lock (failure.FailedCaptainIds) snapshot.FailedCaptainIds = new HashSet<string>(failure.FailedCaptainIds, StringComparer.Ordinal);
            await _Store.SaveAsync(RecoveryStateKinds.LaunchFailure, missionId, snapshot, snapshot.NextAttemptUtc.Add(_Retention), CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>Forget a mission's failures, in memory and in the store.</summary>
        /// <param name="missionId">Mission ID.</param>
        /// <returns>Task.</returns>
        internal async Task ForgetAsync(string missionId)
        {
            if (_Failures.TryRemove(missionId, out MissionLaunchFailure? _))
                await _Store.DeleteAsync(RecoveryStateKinds.LaunchFailure, missionId, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>Load the failures a previous run left.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        internal async Task LoadAsync(CancellationToken token)
        {
            foreach (KeyValuePair<string, MissionLaunchFailure> failure in await _Store.LoadAsync<MissionLaunchFailure>(RecoveryStateKinds.LaunchFailure, token).ConfigureAwait(false))
            {
                failure.Value.FailedCaptainIds = new HashSet<string>(failure.Value.FailedCaptainIds ?? new HashSet<string>(), StringComparer.Ordinal);
                _Failures[failure.Key] = failure.Value;
            }
        }
    }
}
