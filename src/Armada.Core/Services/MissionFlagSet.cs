namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;

    /// <summary>A thread-safe set of mission IDs carrying one flag from one step of a mission's handling to a later one.</summary>
    internal sealed class MissionFlagSet
    {
        private readonly ConcurrentDictionary<string, byte> _Ids = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        /// <summary>Set the flag for a mission.</summary>
        /// <param name="missionId">Mission ID.</param>
        internal void Set(string missionId) => _Ids[missionId] = 0;

        /// <summary>Set the flag for a mission unless already set.</summary>
        /// <param name="missionId">Mission ID.</param>
        /// <returns>True when this call set it.</returns>
        internal bool TrySetFirst(string missionId) => _Ids.TryAdd(missionId, 0);

        /// <summary>Clear the flag for a mission and report whether it was set.</summary>
        /// <param name="missionId">Mission ID.</param>
        /// <returns>True when it was set.</returns>
        internal bool TryTake(string missionId) => _Ids.TryRemove(missionId, out _);
    }
}
