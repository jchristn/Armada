namespace Armada.Core.BlackBox.Enrichment
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Voyage roll-up and cascade for voyage entries: missions by status, the failed missions and their black box
    /// entries, the cascade root (kept from capture, otherwise the first mission to fail), the downstream missions that
    /// were cancelled or failed after it (Related.DownstreamMissionIds), and CascadeRoot and SiblingInVoyage links.
    /// </summary>
    public class BlackBoxVoyageEnricher : BlackBoxEnricherBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name => "Voyage";

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private const int _MaxVoyageMissions = 1000;
        private const int _MaxSiblingLinks = 25;

        private static readonly HashSet<MissionStatusEnum> _FailedStatuses = new HashSet<MissionStatusEnum>
        {
            MissionStatusEnum.Failed, MissionStatusEnum.LandingFailed, MissionStatusEnum.Blocked
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxVoyageEnricher(DatabaseDriver database, LoggingModule logging) : base(logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override async Task EnrichCoreAsync(BlackBoxEnrichmentContext context, CancellationToken token)
        {
            if (context.Entry.SubjectType != BlackBoxSubjectTypeEnum.Voyage || String.IsNullOrEmpty(context.Entry.VoyageId)) return;
            BlackBoxRecord record = context.Record;
            List<Mission> missions = new List<Mission>();
            EnumerationQuery query = new EnumerationQuery { VoyageId = context.Entry.VoyageId, PageSize = 100, PageNumber = 1, Order = EnumerationOrderEnum.CreatedAscending };
            long total = 0;
            while (missions.Count < _MaxVoyageMissions)
            {
                EnumerationResult<Mission> page = await _Database.Missions.EnumerateAsync(query, token).ConfigureAwait(false);
                total = page.TotalRecords;
                missions.AddRange(page.Objects.Where(m => context.Entry.TenantId == null || m.TenantId == context.Entry.TenantId));
                if (page.Objects.Count < query.PageSize || query.PageNumber >= page.TotalPages) break;
                query.PageNumber++;
            }
            if (missions.Count == 0) return;
            if (total > missions.Count) BlackBoxRecordBounder.AddNote(record.Truncation, "The voyage roll-up counts the first " + missions.Count + " of " + total + " missions.");

            BlackBoxVoyageRollup rollup = record.Voyage ?? new BlackBoxVoyageRollup();
            record.Voyage = rollup;
            rollup.MissionTotal = missions.Count;
            rollup.MissionsByStatus = missions.GroupBy(m => m.Status).OrderBy(g => g.Key.ToString(), StringComparer.Ordinal)
                .Select(g => new BlackBoxStatusCount { Status = g.Key.ToString(), Count = g.Count() }).ToList();

            List<Mission> failed = missions.Where(m => _FailedStatuses.Contains(m.Status)).OrderBy(FailedAt).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
            if (failed.Count > BlackBoxLimits.MaxVoyageFailedMissions)
                BlackBoxRecordBounder.AddField(record.Truncation, "Voyage.FailedMissionIds", failed.Count, BlackBoxLimits.MaxVoyageFailedMissions, "items");
            rollup.FailedMissionIds = failed.Take(BlackBoxLimits.MaxVoyageFailedMissions).Select(m => m.Id).ToList();
            rollup.FailedMissionEntryIds = failed.Where(m => m.LastBlackBoxEntryId != null).Take(BlackBoxLimits.MaxVoyageFailedMissions).Select(m => m.LastBlackBoxEntryId!).ToList();

            Mission? root = rollup.CascadeRootMissionId != null ? missions.FirstOrDefault(m => m.Id == rollup.CascadeRootMissionId) : null;
            root ??= failed.FirstOrDefault();
            if (root != null)
            {
                rollup.CascadeRootMissionId = root.Id;
                rollup.CascadeRootEntryId ??= root.LastBlackBoxEntryId;
                context.Links.Add(BlackBoxForensics.LinkRow(context.Entry, root.Id, BlackBoxLinkRelationEnum.CascadeRoot));
            }

            BlackBoxRelated related = record.Related ?? new BlackBoxRelated { Fingerprint = context.Entry.Fingerprint };
            record.Related = related;
            related.CascadeRootEntryId ??= rollup.CascadeRootEntryId;
            List<string> downstream = missions
                .Where(m => root == null || m.Id != root.Id)
                .Where(m => m.Status == MissionStatusEnum.Cancelled || _FailedStatuses.Contains(m.Status))
                .Select(m => m.Id).ToList();
            if (downstream.Count > BlackBoxLimits.MaxDownstreamMissions)
                BlackBoxRecordBounder.AddField(record.Truncation, "Related.DownstreamMissionIds", downstream.Count, BlackBoxLimits.MaxDownstreamMissions, "items");
            related.DownstreamMissionIds = downstream.Take(BlackBoxLimits.MaxDownstreamMissions).ToList();

            foreach (Mission m in failed.Where(m => root == null || m.Id != root.Id).Take(_MaxSiblingLinks))
                context.Links.Add(BlackBoxForensics.LinkRow(context.Entry, m.Id, BlackBoxLinkRelationEnum.SiblingInVoyage));
        }

        private static DateTime FailedAt(Mission m)
        {
            return m.CompletedUtc ?? m.LastUpdateUtc;
        }

        #endregion
    }
}
