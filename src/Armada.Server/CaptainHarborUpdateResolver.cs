namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Fills <see cref="Captain.HarborUpdateRecommended"/> on captain reads: true when the Harbor running the captain's
    /// exclusive session or one of its active missions is connected and missing a capability flag this Admiral expects.
    /// </summary>
    public sealed class CaptainHarborUpdateResolver
    {
        private readonly DatabaseDriver _Database;
        private readonly HarborConnectionManager _Harbors;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver, used to read active missions' assigned Harbors.</param>
        /// <param name="harbors">Harbor connection manager.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public CaptainHarborUpdateResolver(DatabaseDriver database, HarborConnectionManager harbors)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Harbors = harbors ?? throw new ArgumentNullException(nameof(harbors));
        }

        /// <summary>
        /// Set <see cref="Captain.HarborUpdateRecommended"/> on each captain.
        /// </summary>
        /// <param name="captains">Captains to fill; null entries are skipped.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="captains"/> is null.</exception>
        public async Task ApplyAsync(IEnumerable<Captain> captains, CancellationToken token = default)
        {
            if (captains == null) throw new ArgumentNullException(nameof(captains));
            if (!_Harbors.HasConnectedHarbor()) return;
            foreach (Captain captain in captains)
            {
                if (captain == null) continue;
                captain.HarborUpdateRecommended = await RecommendedAsync(captain, token).ConfigureAwait(false);
            }
        }

        private async Task<bool> RecommendedAsync(Captain captain, CancellationToken token)
        {
            if (captain.ProcessId.HasValue
                && _Harbors.TryGetHarborForProcess(captain.ProcessId.Value, out string? sessionHarborId)
                && _Harbors.UpdateRecommended(sessionHarborId))
                return true;

            if (captain.ActiveMissionIds == null) return false;
            foreach (string missionId in captain.ActiveMissionIds)
            {
                if (String.IsNullOrWhiteSpace(missionId)) continue;
                Mission? mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
                if (mission != null && _Harbors.UpdateRecommended(mission.AssignedHarborId)) return true;
            }

            return false;
        }
    }
}
