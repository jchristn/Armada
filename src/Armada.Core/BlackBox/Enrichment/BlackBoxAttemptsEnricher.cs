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
    /// Attempts: the mission's earlier black box entries, its stage outcomes and handoff artifacts (bounded, oldest
    /// first, without duplicates), its redispatch, output-repair and no-op counters (Subject), the rescue chain up
    /// ParentMissionId (Subject.RescueChain and a RescueOf link to the parent) and the rescues incidents launched for it
    /// (Incident.RescueMissionIds, ParentOfRescue links).
    /// </summary>
    public class BlackBoxAttemptsEnricher : BlackBoxEnricherBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name => "Attempts";

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxAttemptsEnricher(DatabaseDriver database, LoggingModule logging) : base(logging)
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
            List<BlackBoxAttempt> attempts = record.Attempts ?? new List<BlackBoxAttempt>();

            EnumerationResult<BlackBoxEntry> previous = await _Database.BlackBoxEntries.EnumerateByMissionAsync(mission.Id, context.Entry.TenantId, new EnumerationQuery { PageSize = BlackBoxLimits.MaxAttempts }, token).ConfigureAwait(false);
            foreach (BlackBoxEntry e in previous.Objects)
            {
                if (e.Id == context.Entry.Id || attempts.Any(a => a.EntryId == e.Id)) continue;
                attempts.Add(new BlackBoxAttempt
                {
                    Attempt = e.Attempt,
                    EntryId = e.Id,
                    MissionId = e.MissionId,
                    StageOutcomeId = e.StageOutcomeId,
                    Kind = e.FailureKind,
                    ReasonExcerpt = BlackBoxLimits.Clip(e.ReasonExcerpt, BlackBoxLimits.MaxReasonExcerptChars),
                    CreatedUtc = e.CreatedUtc,
                    Source = "BlackBox",
                    Disposition = e.Step.ToString()
                });
            }

            EnumerationResult<StageOutcome> outcomes = await _Database.StageOutcomes.EnumerateAsync(new StageOutcomeQuery { MissionId = mission.Id, TenantId = context.Entry.TenantId, PageSize = BlackBoxLimits.MaxAttempts }, token).ConfigureAwait(false);
            foreach (StageOutcome o in outcomes.Objects)
            {
                BlackBoxAttempt? existing = attempts.FirstOrDefault(a => a.StageOutcomeId == o.Id && a.Source != "Handoff");
                if (existing != null && existing.Source == "StageOutcome") continue;
                if (existing != null)
                {
                    // An earlier black box entry for this stage outcome: add what the outcome knows.
                    existing.Runtime ??= o.Runtime?.ToString();
                    existing.Model ??= o.Model;
                    existing.DurationMs ??= o.DurationMs;
                    continue;
                }
                attempts.Add(new BlackBoxAttempt
                {
                    Attempt = o.Attempt,
                    MissionId = o.MissionId,
                    StageOutcomeId = o.Id,
                    Kind = o.FailureKind,
                    ReasonExcerpt = BlackBoxLimits.Clip(o.FailureReason ?? o.CauseNote, BlackBoxLimits.MaxReasonExcerptChars),
                    Runtime = o.Runtime?.ToString(),
                    Model = o.Model,
                    DurationMs = o.DurationMs,
                    CreatedUtc = o.EndedUtc,
                    Source = "StageOutcome",
                    Disposition = o.Disposition.ToString()
                });
            }

            EnumerationResult<HandoffArtifact> handoffs = await _Database.HandoffArtifacts.EnumerateAsync(new HandoffArtifactQuery { MissionId = mission.Id, TenantId = context.Entry.TenantId, PageSize = BlackBoxLimits.MaxAttempts }, token).ConfigureAwait(false);
            foreach (HandoffArtifact h in handoffs.Objects)
            {
                if (attempts.Any(a => a.Source == "Handoff" && a.StageOutcomeId == h.StageOutcomeId && a.Attempt == h.Attempt && a.CreatedUtc == h.CreatedUtc)) continue;
                attempts.Add(new BlackBoxAttempt
                {
                    Attempt = h.Attempt,
                    MissionId = h.MissionId,
                    StageOutcomeId = h.StageOutcomeId,
                    ReasonExcerpt = BlackBoxLimits.Tail(h.Output, BlackBoxLimits.MaxReasonExcerptChars),
                    CreatedUtc = h.CreatedUtc,
                    Source = "Handoff",
                    Disposition = h.Verdict
                });
            }

            List<BlackBoxAttempt> ordered = attempts.OrderBy(a => a.CreatedUtc ?? DateTime.MinValue).ThenBy(a => a.Attempt).ToList();
            if (ordered.Count > BlackBoxLimits.MaxAttempts)
            {
                BlackBoxRecordBounder.AddField(record.Truncation, "Attempts", ordered.Count, BlackBoxLimits.MaxAttempts, "items");
                ordered = ordered.Skip(ordered.Count - BlackBoxLimits.MaxAttempts).ToList();
            }
            record.Attempts = ordered;

            if (record.Subject != null)
            {
                record.Subject.RedispatchAttempts = mission.RedispatchAttempts;
                record.Subject.OutputRepairAttempts = mission.OutputRepairAttempts;
                record.Subject.NoOpRedispatchAttempts = mission.NoOpRedispatchAttempts;
                record.Subject.IsRescue = record.Subject.IsRescue || mission.IsRescue;
                record.Subject.ParentMissionId ??= mission.ParentMissionId;
            }

            await AddRescueChainAsync(context, mission, token).ConfigureAwait(false);
            await AddRescuesAsync(context, mission, token).ConfigureAwait(false);
        }

        private async Task AddRescueChainAsync(BlackBoxEnrichmentContext context, Mission mission, CancellationToken token)
        {
            if (String.IsNullOrEmpty(mission.ParentMissionId)) return;
            List<string> chain = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal) { mission.Id };
            string? parentId = mission.ParentMissionId;
            while (!String.IsNullOrEmpty(parentId) && chain.Count < BlackBoxLimits.MaxAttempts && seen.Add(parentId!))
            {
                chain.Add(parentId!);
                Mission? parent = await _Database.Missions.ReadAsync(parentId!, token).ConfigureAwait(false);
                if (parent == null || (context.Entry.TenantId != null && parent.TenantId != context.Entry.TenantId)) break;
                parentId = parent.ParentMissionId;
            }
            if (context.Record.Subject != null) context.Record.Subject.RescueChain = chain;
            context.Links.Add(BlackBoxForensics.LinkRow(context.Entry, mission.ParentMissionId!, BlackBoxLinkRelationEnum.RescueOf));
        }

        private async Task AddRescuesAsync(BlackBoxEnrichmentContext context, Mission mission, CancellationToken token)
        {
            EnumerationResult<Incident> incidents = await _Database.Incidents.EnumerateAsync(new IncidentQuery { MissionId = mission.Id, TenantId = context.Entry.TenantId, PageSize = 25 }, token).ConfigureAwait(false);
            HashSet<string> added = new HashSet<string>(StringComparer.Ordinal);
            foreach (Incident incident in incidents.Objects)
            {
                foreach (string rescueId in incident.RescueMissionIds ?? new List<string>())
                {
                    if (String.IsNullOrWhiteSpace(rescueId) || rescueId == mission.Id || added.Count >= BlackBoxLimits.MaxAttempts || !added.Add(rescueId)) continue;
                    context.Links.Add(BlackBoxForensics.LinkRow(context.Entry, rescueId, BlackBoxLinkRelationEnum.ParentOfRescue));
                }
            }
        }

        #endregion
    }
}
