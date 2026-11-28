namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// Completion handlers in flight per mission, and the running Definition-of-Done gate commands, each mapped to its
    /// mission so cancelling the mission stops the gate's command.
    /// </summary>
    internal sealed class CompletionRunTracker
    {
        private readonly ConcurrentDictionary<string, int> _InFlight = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<CancellationTokenSource, string> _CommandRuns = new ConcurrentDictionary<CancellationTokenSource, string>();

        /// <summary>Count a completion handler starting for a mission.</summary>
        /// <param name="missionId">Mission ID.</param>
        internal void Enter(string missionId) => _InFlight.AddOrUpdate(missionId, 1, (_, count) => count + 1);

        /// <summary>Count a completion handler finishing for a mission.</summary>
        /// <param name="missionId">Mission ID.</param>
        internal void Exit(string missionId)
        {
            if (_InFlight.AddOrUpdate(missionId, 0, (_, count) => count - 1) <= 0)
                _InFlight.TryRemove(new KeyValuePair<string, int>(missionId, 0));
        }

        /// <summary>True when a completion handler is running for the mission.</summary>
        /// <param name="missionId">Mission ID.</param>
        /// <returns>True when in flight.</returns>
        internal bool IsInFlight(string missionId) => _InFlight.TryGetValue(missionId, out int count) && count > 0;

        /// <summary>Register a running gate command for a mission.</summary>
        /// <param name="run">The command's cancellation source.</param>
        /// <param name="missionId">Mission ID.</param>
        internal void RegisterCommand(CancellationTokenSource run, string missionId) => _CommandRuns[run] = missionId;

        /// <summary>Unregister a finished gate command.</summary>
        /// <param name="run">The command's cancellation source.</param>
        internal void UnregisterCommand(CancellationTokenSource run) => _CommandRuns.TryRemove(run, out _);

        /// <summary>Cancel every running gate command of a mission.</summary>
        /// <param name="missionId">Mission ID.</param>
        internal void StopCommands(string missionId)
        {
            foreach (KeyValuePair<CancellationTokenSource, string> run in _CommandRuns)
            {
                if (!String.Equals(run.Value, missionId, StringComparison.Ordinal)) continue;
                try { run.Key.Cancel(); }
                catch (ObjectDisposedException) { } // The run finished between the lookup and the cancel.
            }
        }
    }
}
