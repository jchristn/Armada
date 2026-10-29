namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Writes an entry's objects: record.json first, then the three briefs, then evidence. Redacts and bounds the record,
    /// caps evidence objects (head and tail kept), applies the storage fallback (an external failure with
    /// FallbackToDatabase moves the entry to the database; without it record.json still goes to the database and the
    /// other objects are marked missing), honors a time budget (on timeout it rewrites record.json with
    /// Truncation.TimedOut and stops), and saves the entry's totals. Used by capture and by the enrichment pipeline.
    /// Thread-safe.
    /// </summary>
    public class BlackBoxEntryWriter
    {
        #region Private-Members

        private readonly string _Header = "[BlackBoxEntryWriter] ";
        private readonly DatabaseDriver _Database;
        private readonly BlackBoxSettings _Settings;
        private readonly BlackBoxObjectRepository _Objects;
        private readonly BlackBoxBriefRenderer _Renderer;
        private readonly BlackBoxRedactor _Redactor;
        private readonly IBlackBoxEventSink _Events;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Black box settings.</param>
        /// <param name="objects">Object repository.</param>
        /// <param name="renderer">Brief renderer.</param>
        /// <param name="redactor">Redactor.</param>
        /// <param name="events">Event sink.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxEntryWriter(DatabaseDriver database, BlackBoxSettings settings, BlackBoxObjectRepository objects, BlackBoxBriefRenderer renderer, BlackBoxRedactor redactor, IBlackBoxEventSink events, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Objects = objects ?? throw new ArgumentNullException(nameof(objects));
            _Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _Redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
            _Events = events ?? throw new ArgumentNullException(nameof(events));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write an entry's record, briefs and evidence, then save the entry (totals, state, summary).
        /// </summary>
        /// <param name="entry">Entry (already inserted).</param>
        /// <param name="record">Record.</param>
        /// <param name="evidence">Evidence objects, or null.</param>
        /// <param name="budget">Time budget token: objects after record.json stop when it fires.</param>
        /// <param name="token">Cancellation token (record.json and the entry update use it).</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry or record is null.</exception>
        public async Task WriteAsync(BlackBoxEntry entry, BlackBoxRecord record, List<BlackBoxPendingObject>? evidence, CancellationToken budget, CancellationToken token)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (record == null) throw new ArgumentNullException(nameof(record));
            bool compress = _Settings.Storage.Compress;
            List<BlackBoxPendingObject> pending = PrepareEvidence(record, evidence ?? new List<BlackBoxPendingObject>());
            _Redactor.RedactRecord(record);
            BlackBoxRecordBounder.Bound(record);
            record.EntryId = entry.Id;
            record.State = entry.State;

            bool timedOut = false;
            bool objectsMissing = false;

            BlackBoxObject? recordObj = await WriteRecordAsync(entry, record, compress, budget, token).ConfigureAwait(false);
            if (recordObj == null)
            {
                timedOut = true;
                MarkTimedOut(record);
                recordObj = await _Objects.WriteAsync(entry, BlackBoxObjectNames.Record, BlackBoxObjectNames.JsonContentType, BlackBoxJson.SerializeRecord(record), compress, true, token).ConfigureAwait(false);
            }

            if (!timedOut)
            {
                List<BlackBoxPendingObject> rest = new List<BlackBoxPendingObject>();
                foreach (BlackBoxAudienceEnum audience in new[] { BlackBoxAudienceEnum.Agent, BlackBoxAudienceEnum.User, BlackBoxAudienceEnum.Maintainer })
                {
                    rest.Add(new BlackBoxPendingObject
                    {
                        Name = BlackBoxBriefRenderer.ObjectName(audience),
                        ContentType = BlackBoxObjectNames.MarkdownContentType,
                        Data = Encoding.UTF8.GetBytes(_Renderer.Render(entry, record, audience)),
                        IsText = true
                    });
                }
                rest.AddRange(pending);
                foreach (BlackBoxPendingObject obj in rest)
                {
                    if (budget.IsCancellationRequested) { timedOut = true; break; }
                    try
                    {
                        await _Objects.WriteAsync(entry, obj.Name, obj.ContentType, obj.Data, compress, false, budget).ConfigureAwait(false);
                                }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        timedOut = true;
                        break;
                    }
                    catch (Exception e) when (e is BlackBoxStoreUnavailableException || e is NotSupportedException)
                    {
                        objectsMissing = true;
                        _Logging.Warn(_Header + "object " + obj.Name + " of " + entry.Id + " was not stored: " + e.Message);
                    }
                }
                if (timedOut)
                {
                    MarkTimedOut(record);
                    await _Objects.WriteAsync(entry, BlackBoxObjectNames.Record, BlackBoxObjectNames.JsonContentType, BlackBoxJson.SerializeRecord(record), compress, true, token).ConfigureAwait(false);
                }
            }

            await ComputeTotalsAsync(entry, token).ConfigureAwait(false);
            if (objectsMissing && entry.State != BlackBoxStateEnum.Deleting) entry.State = BlackBoxStateEnum.ObjectMissing;
            entry.Summary = record.Analysis?.Summary != null ? BlackBoxLimits.Clip(record.Analysis.Summary, BlackBoxLimits.MaxSummaryChars) : entry.Summary;
            await _Database.BlackBoxEntries.UpdateAsync(entry, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<BlackBoxObject?> WriteRecordAsync(BlackBoxEntry entry, BlackBoxRecord record, bool compress, CancellationToken budget, CancellationToken token)
        {
            byte[] bytes = BlackBoxJson.SerializeRecord(record);
            try
            {
                return await _Objects.WriteAsync(entry, BlackBoxObjectNames.Record, BlackBoxObjectNames.JsonContentType, bytes, compress, false, budget).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception e) when ((e is BlackBoxStoreUnavailableException || e is NotSupportedException) && entry.StorageTarget != BlackBoxStorageTargetEnum.Database)
            {
                BlackBoxStorageTargetEnum failed = entry.StorageTarget;
                if (_Settings.Storage.FallbackToDatabase)
                {
                    entry.StorageTarget = BlackBoxStorageTargetEnum.Database;
                    entry.StoragePrefix = null;
                    BlackBoxRecordBounder.AddNote(record.Truncation, "The " + failed + " store failed; the entry was written to the database instead.");
                    try
                    {
                        await _Events.EmitAsync(BlackBoxEventTypes.StorageFallback, "Black box storage fell back to the database for " + entry.Id, entry.TenantId, entry.Id, entry.MissionId, entry.VoyageId, entry.VesselId,
                        new BlackBoxStorageFallbackPayload { EntryId = entry.Id, From = failed, To = BlackBoxStorageTargetEnum.Database, Error = _Redactor.Redact(e.Message) }, token).ConfigureAwait(false);
                    }
                    catch (Exception sinkError) when (sinkError is not OperationCanceledException)
                    {
                        _Logging.Warn(_Header + "blackbox.storage_fallback for " + entry.Id + " could not be emitted: " + sinkError.Message);
                    }
                }
                else
                {
                    BlackBoxRecordBounder.AddNote(record.Truncation, "The " + failed + " store failed and fallback is off; record.json is in the database and the other objects are missing.");
                }
                return await _Objects.WriteAsync(entry, BlackBoxObjectNames.Record, BlackBoxObjectNames.JsonContentType, BlackBoxJson.SerializeRecord(record), compress, true, token).ConfigureAwait(false);
            }
        }

        private async Task ComputeTotalsAsync(BlackBoxEntry entry, CancellationToken token)
        {
            long bytes = 0;
            int count = 0;
            int page = 1;
            while (true)
            {
                EnumerationResult<BlackBoxObject> objects = await _Database.BlackBoxObjects.EnumerateAsync(entry.Id, new EnumerationQuery { PageNumber = page, PageSize = 100 }, token).ConfigureAwait(false);
                foreach (BlackBoxObject obj in objects.Objects)
                {
                    count++;
                    if (!obj.IsMissing) bytes += obj.SizeBytes;
                }
                if (page >= objects.TotalPages) break;
                page++;
            }
            entry.TotalBytes = bytes;
            entry.ObjectCount = count;
        }

        private static void MarkTimedOut(BlackBoxRecord record)
        {
            record.Truncation.TimedOut = true;
            BlackBoxRecordBounder.AddNote(record.Truncation, "Capture ran out of its time budget; objects after record.json were not written. Enrichment re-renders them.");
        }

        private List<BlackBoxPendingObject> PrepareEvidence(BlackBoxRecord record, List<BlackBoxPendingObject> evidence)
        {
            int max = _Settings.Storage.MaxEvidenceObjectBytes;
            BlackBoxEvidenceIndex index = record.Evidence ?? new BlackBoxEvidenceIndex();
            record.Evidence = index;
            List<BlackBoxPendingObject> prepared = new List<BlackBoxPendingObject>();
            foreach (BlackBoxPendingObject obj in evidence.Take(BlackBoxLimits.MaxObjectsPerEntry - 4))
            {
                if (String.IsNullOrEmpty(obj.Name)) continue;
                byte[] data = obj.Data ?? Array.Empty<byte>();
                long original = data.LongLength;
                bool truncated = false;
                if (obj.IsText) data = _Redactor.RedactUtf8(data, record.Redaction);
                if (data.LongLength > max)
                {
                    data = HeadAndTail(data, max, original);
                    truncated = true;
                    BlackBoxRecordBounder.AddField(record.Truncation, obj.Name, original, data.LongLength, "bytes");
                }
                prepared.Add(new BlackBoxPendingObject { Name = obj.Name, ContentType = obj.ContentType, Data = data, IsText = obj.IsText });
                index.Objects.RemoveAll(i => i.Name == obj.Name);
                if (index.Objects.Count < BlackBoxLimits.MaxEvidenceItems)
                {
                    index.Objects.Add(new BlackBoxEvidenceItem
                    {
                        Name = obj.Name,
                        ContentType = obj.ContentType,
                        SizeBytes = data.LongLength,
                        UncompressedBytes = data.LongLength,
                        Excerpt = obj.IsText ? BlackBoxLimits.Tail(Encoding.UTF8.GetString(data, Math.Max(0, data.Length - 2048), Math.Min(2048, data.Length)), BlackBoxLimits.MaxEvidenceExcerptChars) : null,
                        Truncated = truncated,
                        OriginalBytes = truncated ? original : (long?)null
                    });
                }
            }
            return prepared;
        }

        private static byte[] HeadAndTail(byte[] data, int max, long original)
        {
            byte[] marker = Encoding.UTF8.GetBytes("\n\n[... cut by Armada: " + original + " bytes originally, head and tail kept ...]\n\n");
            int keep = Math.Max(0, max - marker.Length);
            int head = keep / 2;
            int tail = keep - head;
            byte[] result = new byte[head + marker.Length + tail];
            Array.Copy(data, 0, result, 0, head);
            Array.Copy(marker, 0, result, head, marker.Length);
            Array.Copy(data, data.Length - tail, result, head + marker.Length, tail);
            return result;
        }

        #endregion
    }
}
