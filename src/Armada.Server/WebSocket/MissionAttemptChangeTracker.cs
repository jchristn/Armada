namespace Armada.Server.WebSocket
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Derives the mission.escalated, mission.repaired and mission.attempt_changed WebSocket events from successive
    /// mission changes: it remembers each open mission's recovery counters and reports which went up. The first change
    /// seen for a mission (including after an Admiral restart) only records the counters. A mission is forgotten when it
    /// reaches a terminal status.
    /// </summary>
    public class MissionAttemptChangeTracker
    {
        #region Public-Members

        /// <summary>
        /// Event raised when the escalation count went up.
        /// </summary>
        public const string EscalatedEvent = "mission.escalated";

        /// <summary>
        /// Event raised when the output repair count went up.
        /// </summary>
        public const string RepairedEvent = "mission.repaired";

        /// <summary>
        /// Event raised when a redispatch, no-op redispatch or stall recovery started a new attempt.
        /// </summary>
        public const string AttemptChangedEvent = "mission.attempt_changed";

        /// <summary>
        /// Open missions remembered at most; past this the memory is cleared (10000).
        /// </summary>
        public const int MaxTracked = 10000;

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, int[]> _Counters = new ConcurrentDictionary<string, int[]>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record a mission change and return the events it implies, in order escalated, repaired, attempt changed.
        /// </summary>
        /// <param name="mission">Changed mission.</param>
        /// <returns>Event types to broadcast (often empty).</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        public List<string> Observe(Mission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            List<string> events = new List<string>();
            if (String.IsNullOrEmpty(mission.Id)) return events;

            int[] current = new int[]
            {
                mission.EscalationCount,
                mission.OutputRepairAttempts,
                mission.RedispatchAttempts + mission.NoOpRedispatchAttempts + mission.StallRecoveryAttempts
            };

            if (_Counters.TryGetValue(mission.Id, out int[]? previous))
            {
                if (current[0] > previous[0]) events.Add(EscalatedEvent);
                if (current[1] > previous[1]) events.Add(RepairedEvent);
                if (current[2] > previous[2]) events.Add(AttemptChangedEvent);
            }

            if (IsTerminal(mission.Status))
            {
                _Counters.TryRemove(mission.Id, out int[]? _);
            }
            else
            {
                if (_Counters.Count >= MaxTracked && !_Counters.ContainsKey(mission.Id)) _Counters.Clear();
                _Counters[mission.Id] = current;
            }

            return events;
        }

        #endregion

        #region Private-Methods

        private static bool IsTerminal(MissionStatusEnum status)
        {
            return status == MissionStatusEnum.Complete
                || status == MissionStatusEnum.Failed
                || status == MissionStatusEnum.Cancelled;
        }

        #endregion
    }
}
