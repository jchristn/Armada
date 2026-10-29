namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// The black box enrichment pipeline for one entry: load the record, run every <see cref="IBlackBoxEnricher"/>, add
    /// related entries (fingerprint matches), run the <see cref="IBlackBoxAdvisor"/>, then write the record, the briefs
    /// (re-rendered), evidence, file and link rows, and update the entry's state (Enriched, then Analyzed). An enricher
    /// failure marks the entry EnrichmentFailed with exponential backoff (2, 4, 8, ... minutes). Emits blackbox.analyzed.
    /// Re-running it is safe (reanalyze). Thread-safe.
    /// </summary>
    public class BlackBoxPipeline
    {
        #region Private-Members

        private readonly string _Header = "[BlackBoxPipeline] ";
        private readonly DatabaseDriver _Database;
        private readonly BlackBoxSettings _Settings;
        private readonly BlackBoxObjectRepository _Objects;
        private readonly BlackBoxEntryWriter _Writer;
        private readonly IBlackBoxEventSink _Events;
        private readonly LoggingModule _Logging;
        private readonly List<IBlackBoxEnricher> _Enrichers;
        private IBlackBoxAdvisor _Advisor;

        #endregion

        #region Public-Members

        /// <summary>
        /// The advisor (default <see cref="NoOpBlackBoxAdvisor"/>). Setting null restores the default.
        /// </summary>
        public IBlackBoxAdvisor Advisor
        {
            get => _Advisor;
            set => _Advisor = value ?? new NoOpBlackBoxAdvisor();
        }

        /// <summary>
        /// Registered enrichers, in the order they run.
        /// </summary>
        public IReadOnlyList<IBlackBoxEnricher> Enrichers => _Enrichers;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Black box settings.</param>
        /// <param name="objects">Object repository.</param>
        /// <param name="writer">Entry writer.</param>
        /// <param name="events">Event sink.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxPipeline(DatabaseDriver database, BlackBoxSettings settings, BlackBoxObjectRepository objects, BlackBoxEntryWriter writer, IBlackBoxEventSink events, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Objects = objects ?? throw new ArgumentNullException(nameof(objects));
            _Writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _Events = events ?? throw new ArgumentNullException(nameof(events));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Enrichers = new List<IBlackBoxEnricher>();
            _Advisor = new NoOpBlackBoxAdvisor();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add an enricher (runs after those already registered). Call during startup, before the queue starts.
        /// </summary>
        /// <param name="enricher">Enricher.</param>
        /// <exception cref="ArgumentNullException">Thrown when enricher is null.</exception>
        public void AddEnricher(IBlackBoxEnricher enricher)
        {
            if (enricher == null) throw new ArgumentNullException(nameof(enricher));
            lock (_Enrichers) _Enrichers.Add(enricher);
        }

        /// <summary>
        /// Run the pipeline for an entry.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated entry, or null when it does not exist or is being deleted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty.</exception>
        public async Task<BlackBoxEntry?> ProcessAsync(string entryId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));
            BlackBoxEntry? entry = await _Database.BlackBoxEntries.ReadAsync(entryId, null, false, token).ConfigureAwait(false);
            if (entry == null) return null;
            BlackBoxRecord record = await LoadRecordAsync(entry, token).ConfigureAwait(false);

            BlackBoxEnrichmentContext context = new BlackBoxEnrichmentContext { Entry = entry, Record = record };
            if (entry.MissionId != null) context.Mission = await _Database.Missions.ReadAsync(entry.MissionId, token).ConfigureAwait(false);
            if (entry.VoyageId != null) context.Voyage = await _Database.Voyages.ReadAsync(entry.VoyageId, token).ConfigureAwait(false);
            if (entry.VesselId != null) context.Vessel = await _Database.Vessels.ReadAsync(entry.VesselId, token).ConfigureAwait(false);

            List<IBlackBoxEnricher> enrichers;
            lock (_Enrichers) enrichers = _Enrichers.ToList();
            foreach (IBlackBoxEnricher enricher in enrichers)
            {
                try
                {
                    await enricher.EnrichAsync(context, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    int attempts = entry.EnrichmentAttempts + 1;
                    DateTime next = DateTime.UtcNow.AddMinutes(Math.Pow(2, Math.Min(attempts, 10)));
                    await _Database.BlackBoxEntries.UpdateStateAsync(entry.Id, BlackBoxStateEnum.EnrichmentFailed, enricher.Name + ": " + e.Message, attempts, next, token).ConfigureAwait(false);
                    _Logging.Warn(_Header + "enricher " + enricher.Name + " failed on " + entry.Id + " (attempt " + attempts + "): " + e.Message);
                    return await _Database.BlackBoxEntries.ReadAsync(entry.Id, null, false, token).ConfigureAwait(false);
                }
            }
            entry.State = BlackBoxStateEnum.Enriched;
            entry.EnrichedUtc = DateTime.UtcNow;
            entry.LastError = null;
            entry.NextAttemptUtc = null;

            await AddRelatedAsync(entry, record, token).ConfigureAwait(false);

            BlackBoxAnalysis? analysis = null;
            try
            {
                analysis = _Advisor.Analyze(record);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "advisor failed on " + entry.Id + ": " + e.Message);
            }
            if (analysis != null)
            {
                record.Analysis = analysis;
                if (analysis.Triage != null && analysis.Triage.Verdict != BlackBoxTriageEnum.Unknown) entry.Triage = analysis.Triage.Verdict;
                entry.Summary = BlackBoxLimits.Clip(analysis.Summary, BlackBoxLimits.MaxSummaryChars);
            }
            record.RulesVersion = _Advisor.RulesVersion;
            entry.RulesVersion = _Advisor.RulesVersion;
            entry.State = BlackBoxStateEnum.Analyzed;
            entry.AnalyzedUtc = DateTime.UtcNow;
            record.State = entry.State;

            await _Writer.WriteAsync(entry, record, context.Objects, CancellationToken.None, token).ConfigureAwait(false);
            if (context.Files.Count > 0) await _Database.BlackBoxFiles.CreateManyAsync(context.Files.Take(Armada.Core.Services.CollectionLimits.MaxRequestItems).ToList(), token).ConfigureAwait(false);
            if (context.Links.Count > 0) await _Database.BlackBoxLinks.CreateManyAsync(context.Links.Take(Armada.Core.Services.CollectionLimits.MaxRequestItems).ToList(), token).ConfigureAwait(false);

            await _Events.EmitAsync(BlackBoxEventTypes.Analyzed, "Black box analyzed: " + entry.Id, entry.TenantId, entry.Id, entry.MissionId, entry.VoyageId, entry.VesselId,
                new BlackBoxAnalyzedPayload { EntryId = entry.Id, MissionId = entry.MissionId, VoyageId = entry.VoyageId, State = entry.State, Triage = entry.Triage, RulesVersion = entry.RulesVersion }, token).ConfigureAwait(false);
            return entry;
        }

        /// <summary>
        /// Load an entry's record.json, or an empty record when it is missing or unreadable (the pipeline rebuilds what it
        /// can and the brief says what is missing).
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        public async Task<BlackBoxRecord> LoadRecordAsync(BlackBoxEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            try
            {
                BlackBoxObjectReadResult read = await _Objects.ReadAsync(entry, BlackBoxObjectNames.Record, 0, null, token).ConfigureAwait(false);
                return BlackBoxJson.DeserializeRecord(read.Data);
            }
            catch (Exception e) when (e is BlackBoxException || e is JsonException || e is NotSupportedException)
            {
                _Logging.Warn(_Header + "record.json of " + entry.Id + " unreadable: " + e.Message);
                BlackBoxRecord empty = new BlackBoxRecord { EntryId = entry.Id, CaptureMode = entry.CaptureMode, CreatedUtc = entry.CreatedUtc };
                BlackBoxRecordBounder.AddNote(empty.Truncation, "record.json could not be read (" + e.GetType().Name + "); this record was rebuilt from the index row.");
                return empty;
            }
        }

        #endregion

        #region Private-Methods

        private async Task AddRelatedAsync(BlackBoxEntry entry, BlackBoxRecord record, CancellationToken token)
        {
            if (String.IsNullOrEmpty(entry.Fingerprint)) return;
            BlackBoxRelated related = record.Related ?? new BlackBoxRelated();
            related.Fingerprint = entry.Fingerprint;
            BlackBoxEnumerateRequest recent = new BlackBoxEnumerateRequest { Fingerprint = entry.Fingerprint, TenantId = entry.TenantId, CreatedAfter = DateTime.UtcNow.AddDays(-30), PageSize = 1 };
            related.OccurrencesLast30Days = (int)await _Database.BlackBoxEntries.CountAsync(recent, token).ConfigureAwait(false);
            EnumerationResult<BlackBoxEntry> similar = await _Database.BlackBoxEntries.EnumerateByFingerprintAsync(entry.Fingerprint!, entry.TenantId, new EnumerationQuery { PageSize = BlackBoxLimits.MaxSimilarEntries + 1 }, token).ConfigureAwait(false);
            related.SimilarEntries = similar.Objects.Where(e => e.Id != entry.Id).Take(BlackBoxLimits.MaxSimilarEntries).Select(e => new BlackBoxSimilarEntry
            {
                EntryId = e.Id,
                MissionId = e.MissionId,
                MissionTitle = e.MissionTitle,
                CreatedUtc = e.CreatedUtc,
                Resolution = e.Resolution,
                ResolutionNote = BlackBoxLimits.Clip(e.ResolutionNote, BlackBoxLimits.MaxReasonExcerptChars)
            }).ToList();
            record.Related = related;
        }

        #endregion
    }
}
