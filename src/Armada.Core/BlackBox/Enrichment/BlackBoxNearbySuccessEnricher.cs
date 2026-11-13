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
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Nearby successes: successful landings on the same vessel within blackBox.nearbySuccessWindowHours of the failure
    /// (success flight recorder) that changed some of the files the failure touched (its file rows, conflicted files,
    /// out-of-scope files and dirty paths). The first ten, most overlap first, go into Related.NearbySuccesses with the
    /// total in Related.NearbySuccessTotal.
    /// </summary>
    public class BlackBoxNearbySuccessEnricher : BlackBoxEnricherBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name => "NearbySuccess";

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxNearbySuccessEnricher(DatabaseDriver database, ArmadaSettings settings, LoggingModule logging) : base(logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override async Task EnrichCoreAsync(BlackBoxEnrichmentContext context, CancellationToken token)
        {
            BlackBoxEntry entry = context.Entry;
            if (entry.SubjectType != BlackBoxSubjectTypeEnum.Mission || String.IsNullOrEmpty(entry.VesselId) || _Settings.BlackBox.SuccessRetentionDays <= 0) return;
            HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BlackBoxFile f in context.Files.Where(x => x.Role != BlackBoxFileRoleEnum.ChangedByConflictingMission)) files.Add(f.Path);
            BlackBoxRecord record = context.Record;
            foreach (string f in record.Conflict?.ConflictedFiles ?? new List<string>()) files.Add(f);
            foreach (string f in record.Scope?.OutOfScopeFiles ?? new List<string>()) files.Add(f);
            foreach (string f in record.Workspace?.DirtyPaths ?? new List<string>()) files.Add(f);
            if (files.Count == 0) return;

            int hours = _Settings.BlackBox.NearbySuccessWindowHours;
            EnumerationResult<BlackBoxSuccess> candidates = await _Database.BlackBoxSuccesses.EnumerateByVesselAsync(entry.VesselId!, entry.TenantId, null,
                entry.CreatedUtc.AddHours(-hours), entry.CreatedUtc.AddHours(hours), new EnumerationQuery { PageNumber = 1, PageSize = BlackBoxInsightLimits.MaxNearbyCandidates }, token).ConfigureAwait(false);
            List<BlackBoxNearbySuccess> nearby = BlackBoxInsightService.Nearby(entry.CreatedUtc, files, candidates.Objects);
            BlackBoxRelated related = record.Related ?? new BlackBoxRelated();
            related.NearbySuccessTotal = nearby.Count;
            related.NearbySuccesses = nearby.Take(BlackBoxInsightLimits.MaxNearbySuccesses).ToList();
            record.Related = related;
            if (candidates.TotalRecords > candidates.Objects.Count)
                BlackBoxRecordBounder.AddNote(record.Truncation, "Nearby successes compared the newest " + candidates.Objects.Count + " of " + candidates.TotalRecords + " successful landings in the window.");
        }

        #endregion
    }
}
