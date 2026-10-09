namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using Armada.Core.Models;

    /// <summary>
    /// The latest activity of each running mission's captain, kept in memory on the Admiral while the captain runs (it is
    /// not stored). The mission REST and WebSocket models read it.
    /// </summary>
    public class MissionActivityTracker
    {
        #region Private-Members

        private readonly ConcurrentDictionary<string, RuntimeActivity> _Latest = new ConcurrentDictionary<string, RuntimeActivity>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record a mission's latest activity.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <param name="activity">Activity.</param>
        public void Update(string missionId, RuntimeActivity activity)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            if (activity == null) throw new ArgumentNullException(nameof(activity));
            _Latest[missionId] = activity.Clone();
        }

        /// <summary>
        /// A mission's latest activity (a copy), or null when its captain is not running or has reported none.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <returns>The activity, or null.</returns>
        public RuntimeActivity? Get(string missionId)
        {
            if (String.IsNullOrEmpty(missionId)) return null;
            return _Latest.TryGetValue(missionId, out RuntimeActivity? activity) ? activity.Clone() : null;
        }

        /// <summary>
        /// Forget a mission's activity (its captain stopped).
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        public void Clear(string missionId)
        {
            if (String.IsNullOrEmpty(missionId)) return;
            _Latest.TryRemove(missionId, out RuntimeActivity? _);
        }

        #endregion
    }
}
