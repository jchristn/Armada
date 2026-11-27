namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    /// <summary>
    /// Usage-selection state per Pending mission: the transient blocker that holds it until a retry time or policy
    /// change, and the execution attempt whose reservation a pending no-op redispatch takes over.
    /// </summary>
    internal sealed class UsageSelectionBlockerStore
    {
        private readonly ConcurrentDictionary<string, UsageSelectionTransientBlocker> _Blockers = new ConcurrentDictionary<string, UsageSelectionTransientBlocker>(StringComparer.Ordinal);
        // Mission ID to the execution attempt whose reservation its pending no-op redispatch takes over.
        private readonly ConcurrentDictionary<string, string> _NoOpRedispatchCarry = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The blocker held for a mission, if any.</summary>
        /// <param name="missionId">Mission ID.</param>
        /// <param name="blocker">The blocker.</param>
        /// <returns>True when one is held.</returns>
        internal bool TryGetBlocker(string missionId, [NotNullWhen(true)] out UsageSelectionTransientBlocker? blocker)
        {
            return _Blockers.TryGetValue(missionId, out blocker);
        }

        /// <summary>Hold a blocker for a mission.</summary>
        /// <param name="missionId">Mission ID.</param>
        /// <param name="blocker">The blocker.</param>
        internal void SetBlocker(string missionId, UsageSelectionTransientBlocker blocker) => _Blockers[missionId] = blocker;

        /// <summary>Hold a blocker, first evicting the one whose retry is due soonest when the store is full.</summary>
        /// <param name="missionId">Mission ID.</param>
        /// <param name="blocker">The blocker.</param>
        /// <param name="maxTracked">Most blockers kept.</param>
        internal void SetBlockerBounded(string missionId, UsageSelectionTransientBlocker blocker, int maxTracked)
        {
            if (_Blockers.Count >= maxTracked)
            {
                // Evict the blocker whose retry is due soonest; it is the least valuable to keep.
                string? oldest = _Blockers.OrderBy(pair => pair.Value.RetryUtc).Select(pair => pair.Key).FirstOrDefault();
                if (oldest != null) _Blockers.TryRemove(oldest, out _);
            }
            _Blockers[missionId] = blocker;
        }

        /// <summary>Release a mission's blocker.</summary>
        /// <param name="missionId">Mission ID.</param>
        internal void ClearBlocker(string missionId) => _Blockers.TryRemove(missionId, out _);

        /// <summary>Carry an execution attempt's reservation to the mission's next no-op redispatch.</summary>
        /// <param name="missionId">Mission ID.</param>
        /// <param name="executionKey">Execution attempt ID.</param>
        internal void SetCarry(string missionId, string executionKey) => _NoOpRedispatchCarry[missionId] = executionKey;

        /// <summary>The carried execution attempt of a mission, or null.</summary>
        /// <param name="missionId">Mission ID.</param>
        /// <returns>Execution attempt ID or null.</returns>
        internal string? GetCarry(string missionId) => _NoOpRedispatchCarry.TryGetValue(missionId, out string? carried) ? carried : null;

        /// <summary>Drop a mission's carried execution attempt.</summary>
        /// <param name="missionId">Mission ID.</param>
        internal void ClearCarry(string missionId) => _NoOpRedispatchCarry.TryRemove(missionId, out _);
    }
}
