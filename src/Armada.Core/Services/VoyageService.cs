namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Service for voyage lifecycle management.
    /// </summary>
    public class VoyageService : IVoyageService
    {
        #region Private-Members

        private string _Header = "[VoyageService] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        public VoyageService(LoggingModule logging, DatabaseDriver database)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<List<Voyage>> CheckCompletionsAsync(CancellationToken token = default)
        {
            return CheckCompletionsAsync(null, token);
        }

        /// <inheritdoc />
        public async Task<List<Voyage>> CheckCompletionsAsync(Func<string, bool>? isCompletionInFlight, CancellationToken token = default)
        {
            // CheckCompletionsAsync is a background/system method (called from Admiral loop).
            // It scans all tenants' voyages, so unscoped calls are appropriate here.
            List<Voyage> completedVoyages = new List<Voyage>();
            List<Voyage> activeVoyages = await _Database.Voyages.EnumerateByStatusAsync(VoyageStatusEnum.InProgress, token).ConfigureAwait(false);
            List<Voyage> openVoyages = await _Database.Voyages.EnumerateByStatusAsync(VoyageStatusEnum.Open, token).ConfigureAwait(false);
            activeVoyages.AddRange(openVoyages);
            HashSet<string>? missionsInMergeQueue = null;

            foreach (Voyage voyage in activeVoyages)
            {
                List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(voyage.Id, token).ConfigureAwait(false);
                // The merge-queue read is shared across voyages in this pass and made only when a voyage needs it.
                VoyageStatusEnum? settled = await VoyageSettlement.DecideAsync(missions, isCompletionInFlight, async () =>
                    missionsInMergeQueue ??= await MergeQueueMissionActivity.GetMissionIdsWithActiveEntriesAsync(_Database, token).ConfigureAwait(false)).ConfigureAwait(false);
                if (settled == null) continue;

                await VoyageSettlement.ApplyAsync(_Database, voyage, settled.Value, missions, token, _Logging.Warn).ConfigureAwait(false);
                _Logging.Info(_Header + "voyage " + voyage.Id + " reached terminal status " + voyage.Status);
                completedVoyages.Add(voyage);
            }

            return completedVoyages;
        }

        /// <inheritdoc />
        public async Task<VoyageProgress?> GetProgressAsync(string voyageId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(voyageId)) return null;

            Voyage? voyage = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Voyages.ReadAsync(tenantId, voyageId, token).ConfigureAwait(false)
                : await _Database.Voyages.ReadAsync(voyageId, token).ConfigureAwait(false);
            if (voyage == null) return null;

            List<MissionSummary> missions = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Missions.EnumerateSummariesByVoyageAsync(tenantId, voyage.Id, token).ConfigureAwait(false)
                : await _Database.Missions.EnumerateSummariesByVoyageAsync(voyage.Id, token).ConfigureAwait(false);

            VoyageProgress progress = VoyageProgressReader.BuildProgress(voyage, missions);
            return progress;
        }

        /// <inheritdoc />
        public Task<VoyageProgress> GetProgressAsync(Voyage voyage, CancellationToken token = default)
        {
            return VoyageProgressReader.ReadAsync(_Database, voyage, token);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<VoyageProgress>> EnumerateProgressAsync(AuthContext auth, EnumerationQuery query, CancellationToken token = default)
        {
            return VoyageProgressReader.EnumerateAsync(_Database, auth, query, token);
        }

        #endregion
    }
}
