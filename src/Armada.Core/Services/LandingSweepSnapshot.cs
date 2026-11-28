namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The unlanded missions one landing reconciliation sweep works from (RS-9.2): each of WorkProduced, LandingFailed,
    /// Review and PullRequestOpen is read once per sweep, and every case of the sweep takes its candidates from here
    /// instead of querying again. Missions a case changed are recorded, so later cases of the same sweep skip them.
    /// </summary>
    public class LandingSweepSnapshot
    {
        #region Public-Members

        /// <summary>
        /// The statuses a sweep reads, in the order cases see them.
        /// </summary>
        public static IReadOnlyList<MissionStatusEnum> Statuses { get; } = new List<MissionStatusEnum>
        {
            MissionStatusEnum.WorkProduced,
            MissionStatusEnum.LandingFailed,
            MissionStatusEnum.Review,
            MissionStatusEnum.PullRequestOpen
        };

        #endregion

        #region Private-Members

        private readonly Dictionary<MissionStatusEnum, List<MissionSummary>> _ByStatus = new Dictionary<MissionStatusEnum, List<MissionSummary>>();
        private readonly HashSet<string> _Changed = new HashSet<string>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set the missions read for a status.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="missions">Missions in that status.</param>
        /// <exception cref="ArgumentNullException">Thrown when missions is null.</exception>
        public void Set(MissionStatusEnum status, IEnumerable<MissionSummary> missions)
        {
            if (missions == null) throw new ArgumentNullException(nameof(missions));
            _ByStatus[status] = missions.Where(m => m != null && m.Status == status).ToList();
        }

        /// <summary>
        /// The missions read for a status that no earlier case of this sweep changed.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>The missions, possibly empty.</returns>
        public List<MissionSummary> For(MissionStatusEnum status)
        {
            if (!_ByStatus.TryGetValue(status, out List<MissionSummary>? missions)) return new List<MissionSummary>();
            return missions.Where(m => !_Changed.Contains(m.Id)).ToList();
        }

        /// <summary>
        /// Record that a case changed a mission, so later cases skip it.
        /// </summary>
        /// <param name="missionId">Mission ID.</param>
        public void MarkChanged(string? missionId)
        {
            if (!String.IsNullOrEmpty(missionId)) _Changed.Add(missionId);
        }

        #endregion
    }
}
