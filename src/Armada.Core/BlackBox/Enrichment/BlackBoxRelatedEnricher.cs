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
    /// Related work of a mission entry: the mission it depends on (DependencyOf link) and the cascade root (the furthest
    /// failed mission up the DependsOnMissionId chain, Related.CascadeRootEntryId), the downstream missions that depend
    /// on it directly or transitively within the voyage (Related.DownstreamMissionIds), failed siblings in the voyage
    /// (SiblingInVoyage links) and missions that shared its dock (SharedDock links). Fingerprint matches, similar entries
    /// with their resolution and occurrencesLast30Days are added by the pipeline after the enrichers.
    /// </summary>
    public class BlackBoxRelatedEnricher : BlackBoxEnricherBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name => "Related";

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private const int _MaxSiblingLinks = 25;
        private const int _MaxVoyageMissions = 1000;

        private static readonly HashSet<MissionStatusEnum> _FailedStatuses = new HashSet<MissionStatusEnum>
        {
            MissionStatusEnum.Failed, MissionStatusEnum.LandingFailed
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxRelatedEnricher(DatabaseDriver database, LoggingModule logging) : base(logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override async Task EnrichCoreAsync(BlackBoxEnrichmentContext context, CancellationToken token)
        {
            Mission? mission = context.Mission;
            if (context.Entry.SubjectType != BlackBoxSubjectTypeEnum.Mission || mission == null) return;
            BlackBoxRecord record = context.Record;
            BlackBoxRelated related = record.Related ?? new BlackBoxRelated { Fingerprint = context.Entry.Fingerprint };
            record.Related = related;

            await AddUpstreamAsync(context, mission, related, token).ConfigureAwait(false);
            if (String.IsNullOrEmpty(mission.VoyageId)) return;

            List<Mission> voyage = await ReadVoyageMissionsAsync(mission.VoyageId!, token).ConfigureAwait(false);
            voyage = voyage.Where(m => context.Entry.TenantId == null || m.TenantId == context.Entry.TenantId).ToList();

            // Downstream: breadth-first over DependsOnMissionId within the voyage.
            List<string> downstream = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal) { mission.Id };
            Queue<string> frontier = new Queue<string>();
            frontier.Enqueue(mission.Id);
            int total = 0;
            while (frontier.Count > 0)
            {
                string current = frontier.Dequeue();
                foreach (Mission child in voyage.Where(m => m.DependsOnMissionId == current))
                {
                    if (!seen.Add(child.Id)) continue;
                    total++;
                    if (downstream.Count < BlackBoxLimits.MaxDownstreamMissions) downstream.Add(child.Id);
                    frontier.Enqueue(child.Id);
                }
            }
            if (total > downstream.Count) BlackBoxRecordBounder.AddField(record.Truncation, "Related.DownstreamMissionIds", total, downstream.Count, "items");
            if (downstream.Count > 0 || related.DownstreamMissionIds.Count == 0) related.DownstreamMissionIds = downstream;

            int siblings = 0;
            foreach (Mission sibling in voyage.Where(m => m.Id != mission.Id && _FailedStatuses.Contains(m.Status)).OrderBy(m => m.CreatedUtc))
            {
                if (siblings++ >= _MaxSiblingLinks) break;
                context.Links.Add(BlackBoxForensics.LinkRow(context.Entry, sibling.Id, BlackBoxLinkRelationEnum.SiblingInVoyage));
            }
            if (!String.IsNullOrEmpty(mission.DockId))
            {
                foreach (Mission sharer in voyage.Where(m => m.Id != mission.Id && m.DockId == mission.DockId).Take(_MaxSiblingLinks))
                    context.Links.Add(BlackBoxForensics.LinkRow(context.Entry, sharer.Id, BlackBoxLinkRelationEnum.SharedDock));
            }
        }

        private async Task AddUpstreamAsync(BlackBoxEnrichmentContext context, Mission mission, BlackBoxRelated related, CancellationToken token)
        {
            if (String.IsNullOrEmpty(mission.DependsOnMissionId)) return;
            context.Links.Add(BlackBoxForensics.LinkRow(context.Entry, mission.DependsOnMissionId!, BlackBoxLinkRelationEnum.DependencyOf));

            Mission? root = null;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal) { mission.Id };
            string? upstreamId = mission.DependsOnMissionId;
            for (int hops = 0; hops < BlackBoxLimits.MaxAttempts && !String.IsNullOrEmpty(upstreamId) && seen.Add(upstreamId!); hops++)
            {
                Mission? upstream = await _Database.Missions.ReadAsync(upstreamId!, token).ConfigureAwait(false);
                if (upstream == null || (context.Entry.TenantId != null && upstream.TenantId != context.Entry.TenantId)) break;
                if (_FailedStatuses.Contains(upstream.Status) || upstream.Status == MissionStatusEnum.Cancelled) root = upstream;
                upstreamId = upstream.DependsOnMissionId;
            }
            if (root != null && related.CascadeRootEntryId == null) related.CascadeRootEntryId = root.LastBlackBoxEntryId;
            if (root != null) context.Links.Add(BlackBoxForensics.LinkRow(context.Entry, root.Id, BlackBoxLinkRelationEnum.CascadeRoot));
        }

        private async Task<List<Mission>> ReadVoyageMissionsAsync(string voyageId, CancellationToken token)
        {
            List<Mission> missions = new List<Mission>();
            EnumerationQuery query = new EnumerationQuery { VoyageId = voyageId, PageSize = 100, PageNumber = 1, Order = EnumerationOrderEnum.CreatedAscending };
            while (missions.Count < _MaxVoyageMissions)
            {
                EnumerationResult<Mission> page = await _Database.Missions.EnumerateAsync(query, token).ConfigureAwait(false);
                missions.AddRange(page.Objects);
                if (page.Objects.Count < query.PageSize || query.PageNumber >= page.TotalPages) break;
                query.PageNumber++;
            }
            return missions;
        }

        #endregion
    }
}
