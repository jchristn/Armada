namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Authorization;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Implementation of <see cref="IBlackBoxService"/>. Thread-safe.
    /// </summary>
    public class BlackBoxService : IBlackBoxService
    {
        #region Public-Members

        /// <summary>
        /// Runs recorded recovery and recommendation actions as the caller (the Admiral wires one that invokes the MCP tool
        /// handler), or null; confirmed runs then fail with NotImplementedException.
        /// </summary>
        public IBlackBoxActionExecutor? ActionExecutor { get; set; } = null;

        /// <summary>
        /// Authorization service used to check every recorded action against its tool's declared requirement
        /// (McpToolAuthorizationRegistry) before anything runs.
        /// </summary>
        public IAuthorizationService Authorization
        {
            get => _Authorization;
            set => _Authorization = value ?? new AuthorizationService();
        }

        /// <summary>
        /// Confirm tokens of dry runs (10-minute, single-use).
        /// </summary>
        public BlackBoxConfirmationStore Confirmations { get; } = new BlackBoxConfirmationStore();

        /// <summary>
        /// Redactor used for exported fixtures (the runtime's, with registered secrets).
        /// </summary>
        public BlackBoxRedactor Redactor
        {
            get => _Redactor;
            set => _Redactor = value ?? new BlackBoxRedactor();
        }

        #endregion

        #region Private-Members

        private readonly string _Header = "[BlackBoxService] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly BlackBoxObjectRepository _Objects;
        private readonly BlackBoxBriefRenderer _Renderer;
        private readonly BlackBoxPipeline _Pipeline;
        private readonly BlackBoxGarbageCollector _Gc;
        private readonly BlackBoxSweeper _Sweeper;
        private readonly BlackBoxStoreFactory _Stores;
        private readonly IBlackBoxEventSink _Events;
        private readonly LoggingModule _Logging;
        private IAuthorizationService _Authorization = new AuthorizationService();
        private BlackBoxRedactor _Redactor = new BlackBoxRedactor();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Armada settings.</param>
        /// <param name="objects">Object repository.</param>
        /// <param name="renderer">Brief renderer.</param>
        /// <param name="pipeline">Enrichment pipeline.</param>
        /// <param name="gc">Garbage collector.</param>
        /// <param name="sweeper">Sweeper.</param>
        /// <param name="stores">Store factory.</param>
        /// <param name="events">Event sink.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxService(DatabaseDriver database, ArmadaSettings settings, BlackBoxObjectRepository objects, BlackBoxBriefRenderer renderer, BlackBoxPipeline pipeline,
            BlackBoxGarbageCollector gc, BlackBoxSweeper sweeper, BlackBoxStoreFactory stores, IBlackBoxEventSink events, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Objects = objects ?? throw new ArgumentNullException(nameof(objects));
            _Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _Gc = gc ?? throw new ArgumentNullException(nameof(gc));
            _Sweeper = sweeper ?? throw new ArgumentNullException(nameof(sweeper));
            _Stores = stores ?? throw new ArgumentNullException(nameof(stores));
            _Events = events ?? throw new ArgumentNullException(nameof(events));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<BlackBoxEntry> GetMissionEntryAsync(AuthContext caller, string missionId, bool includeRecord = true, CancellationToken token = default)
        {
            RequireCaller(caller);
            if (String.IsNullOrWhiteSpace(missionId)) throw new BlackBoxNotFoundException("No black box entry for that mission.");
            BlackBoxEntry? entry = await _Database.BlackBoxEntries.ReadCurrentByMissionAsync(missionId.Trim(), caller.IsAdmin ? null : caller.TenantId, token).ConfigureAwait(false);
            if (entry == null || !Visible(caller, entry)) throw new BlackBoxNotFoundException("No black box entry for mission " + missionId + ".");
            if (includeRecord) entry.Record = await _Pipeline.LoadRecordAsync(entry, token).ConfigureAwait(false);
            return entry;
        }

        /// <inheritdoc />
        public async Task<BlackBoxEntry> GetVoyageEntryAsync(AuthContext caller, string voyageId, bool includeRecord = true, CancellationToken token = default)
        {
            RequireCaller(caller);
            if (String.IsNullOrWhiteSpace(voyageId)) throw new BlackBoxNotFoundException("No black box entry for that voyage.");
            BlackBoxEntry? entry = await _Database.BlackBoxEntries.ReadCurrentByVoyageAsync(voyageId.Trim(), caller.IsAdmin ? null : caller.TenantId, token).ConfigureAwait(false);
            if (entry == null || !Visible(caller, entry)) throw new BlackBoxNotFoundException("No black box entry for voyage " + voyageId + ".");
            if (includeRecord) entry.Record = await _Pipeline.LoadRecordAsync(entry, token).ConfigureAwait(false);
            return entry;
        }

        /// <inheritdoc />
        public async Task<BlackBoxEntry> GetEntryAsync(AuthContext caller, string entryId, bool includeRecord = true, CancellationToken token = default)
        {
            BlackBoxEntry entry = await ReadVisibleAsync(caller, entryId, token).ConfigureAwait(false);
            if (includeRecord) entry.Record = await _Pipeline.LoadRecordAsync(entry, token).ConfigureAwait(false);
            return entry;
        }

        /// <inheritdoc />
        public async Task<string> ResolveEntryIdAsync(AuthContext caller, string? missionId, string? voyageId, string? entryId, CancellationToken token = default)
        {
            if (!String.IsNullOrWhiteSpace(entryId)) return (await ReadVisibleAsync(caller, entryId!, token).ConfigureAwait(false)).Id;
            if (!String.IsNullOrWhiteSpace(missionId)) return (await GetMissionEntryAsync(caller, missionId!, false, token).ConfigureAwait(false)).Id;
            if (!String.IsNullOrWhiteSpace(voyageId)) return (await GetVoyageEntryAsync(caller, voyageId!, false, token).ConfigureAwait(false)).Id;
            throw new ArgumentException("Give a missionId, voyageId or entryId.");
        }

        /// <inheritdoc />
        public async Task<BlackBoxDigest> GetDigestAsync(AuthContext caller, string entryId, CancellationToken token = default)
        {
            BlackBoxEntry entry = await GetEntryAsync(caller, entryId, true, token).ConfigureAwait(false);
            BlackBoxRecord r = entry.Record!;
            BlackBoxMechanism? m = r.Mechanism;
            string? mechanism = m == null ? null
                : m.ExceptionType != null ? m.ExceptionType + ": " + BlackBoxLimits.Clip(m.ExceptionMessage, 200)
                : m.FailingCommand != null ? "Command `" + m.FailingCommand + "` exited " + (m.CommandExitCode?.ToString() ?? "?")
                : m.GitOperation != null ? "git " + m.GitOperation + " failed"
                : m.ExitCode.HasValue ? "Agent exited " + m.ExitCode.Value
                : null;
            return new BlackBoxDigest
            {
                EntryId = entry.Id,
                SubjectType = entry.SubjectType,
                MissionId = entry.MissionId,
                VoyageId = entry.VoyageId,
                Title = entry.MissionTitle,
                VesselName = entry.VesselName,
                Attempt = entry.Attempt,
                CaptureMode = entry.CaptureMode,
                State = entry.State,
                Phase = entry.Phase,
                Step = entry.Step,
                Host = r.Site?.Host,
                SourceFile = r.Site?.SourceFile,
                SourceLine = r.Site?.SourceLine,
                Method = r.Site?.Method,
                Kind = entry.FailureKind,
                Reason = BlackBoxLimits.Clip(r.Classification?.RawReason ?? entry.ReasonExcerpt, BlackBoxLimits.MaxReasonExcerptChars),
                Mechanism = BlackBoxLimits.Clip(mechanism, BlackBoxLimits.MaxReasonExcerptChars),
                StatusBefore = entry.StatusBefore,
                StatusAfter = entry.StatusAfter,
                Triage = r.Analysis?.Triage?.Verdict ?? entry.Triage,
                TriageConfidence = r.Analysis?.Triage?.Confidence,
                Summary = r.Analysis?.Summary,
                Why = r.Analysis?.Why.Take(3).ToList() ?? new List<BlackBoxFinding>(),
                RecoveryPaths = r.Analysis?.RecoveryPaths.OrderBy(p => p.Rank).Take(3).ToList() ?? new List<BlackBoxRecoveryPath>(),
                ConflictedFileTotal = r.Conflict == null ? 0 : Math.Max(r.Conflict.ConflictedFileTotal, r.Conflict.ConflictedFiles.Count),
                ObjectCount = entry.ObjectCount,
                Resolution = entry.Resolution,
                CreatedUtc = entry.CreatedUtc
            };
        }

        /// <inheritdoc />
        public async Task<BlackBoxRecord> GetRecordAsync(AuthContext caller, string entryId, CancellationToken token = default)
        {
            BlackBoxEntry entry = await ReadVisibleAsync(caller, entryId, token).ConfigureAwait(false);
            return await _Pipeline.LoadRecordAsync(entry, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<BlackBoxSectionResult> GetSectionAsync(AuthContext caller, string entryId, BlackBoxSectionEnum section, CancellationToken token = default)
        {
            if (section == BlackBoxSectionEnum.Unknown) throw new ArgumentException("Unknown section.", nameof(section));
            BlackBoxRecord full = await GetRecordAsync(caller, entryId, token).ConfigureAwait(false);
            BlackBoxRecord part = new BlackBoxRecord
            {
                EntryId = full.EntryId,
                SchemaVersion = full.SchemaVersion,
                RulesVersion = full.RulesVersion,
                CaptureMode = full.CaptureMode,
                State = full.State,
                CreatedUtc = full.CreatedUtc
            };
            switch (section)
            {
                case BlackBoxSectionEnum.Subject: part.Subject = full.Subject; break;
                case BlackBoxSectionEnum.Status: part.Status = full.Status; break;
                case BlackBoxSectionEnum.Site: part.Site = full.Site; break;
                case BlackBoxSectionEnum.Mechanism: part.Mechanism = full.Mechanism; break;
                case BlackBoxSectionEnum.Classification: part.Classification = full.Classification; break;
                case BlackBoxSectionEnum.Workspace: part.Workspace = full.Workspace; break;
                case BlackBoxSectionEnum.Conflict: part.Conflict = full.Conflict; break;
                case BlackBoxSectionEnum.Scope: part.Scope = full.Scope; break;
                case BlackBoxSectionEnum.Configuration: part.Configuration = full.Configuration; break;
                case BlackBoxSectionEnum.Attempts: part.Attempts = full.Attempts; break;
                case BlackBoxSectionEnum.Evidence: part.Evidence = full.Evidence; break;
                case BlackBoxSectionEnum.Analysis: part.Analysis = full.Analysis; break;
                case BlackBoxSectionEnum.Related: part.Related = full.Related; break;
                case BlackBoxSectionEnum.Voyage: part.Voyage = full.Voyage; break;
                case BlackBoxSectionEnum.Redaction: part.Redaction = full.Redaction; break;
                case BlackBoxSectionEnum.Truncation: part.Truncation = full.Truncation; break;
            }
            return new BlackBoxSectionResult { EntryId = full.EntryId, Section = section, Record = part };
        }

        /// <inheritdoc />
        public async Task<string> GetBriefAsync(AuthContext caller, string entryId, BlackBoxAudienceEnum audience = BlackBoxAudienceEnum.Agent, bool redactHosts = false, CancellationToken token = default)
        {
            BlackBoxEntry entry = await GetEntryAsync(caller, entryId, true, token).ConfigureAwait(false);
            if (audience == BlackBoxAudienceEnum.Unknown) audience = BlackBoxAudienceEnum.Agent;
            List<string> notes = await NotesForBriefAsync(entry, token).ConfigureAwait(false);
            string brief = _Renderer.Render(entry, entry.Record, audience, redactHosts);
            if (notes.Count == 0) return brief;
            StringBuilder sb = new StringBuilder(brief.TrimEnd()).Append("\n\n## Notes\n\n");
            foreach (string note in notes) sb.Append("- ").Append(note.Replace('\n', ' ')).Append('\n');
            string withNotes = sb.ToString();
            return Encoding.UTF8.GetByteCount(withNotes) <= _Renderer.MaxBytes ? withNotes : brief;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<BlackBoxEntrySummary>> EnumerateMissionEntriesAsync(AuthContext caller, string missionId, EnumerationQuery? query, CancellationToken token = default)
        {
            RequireCaller(caller);
            if (String.IsNullOrWhiteSpace(missionId)) throw new ArgumentNullException(nameof(missionId));
            EnumerationQuery page = query ?? new EnumerationQuery();
            BlackBoxEnumerateRequest request = new BlackBoxEnumerateRequest
            {
                MissionId = missionId.Trim(),
                SubjectType = BlackBoxSubjectTypeEnum.Mission,
                PageNumber = page.PageNumber,
                PageSize = page.PageSize,
                Order = page.Order
            };
            return await EnumerateAsync(caller, request, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<BlackBoxEntrySummary>> EnumerateAsync(AuthContext caller, BlackBoxEnumerateRequest? query, CancellationToken token = default)
        {
            RequireCaller(caller);
            BlackBoxEnumerateRequest scoped = Scope(caller, query ?? new BlackBoxEnumerateRequest());
            scoped.SearchText = query?.SearchText;
            EnumerationResult<BlackBoxEntry> page = await _Database.BlackBoxEntries.EnumerateAsync(scoped, token).ConfigureAwait(false);
            return Map(page, Summarize);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxEntrySummary>> SearchAsync(AuthContext caller, BlackBoxSearchRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string q = (request.Query ?? String.Empty).Trim();
            if (q.Length == 0) throw new ArgumentException("Query is required.", nameof(request));
            if (q.Length > BlackBoxLimits.MaxSearchQueryChars) throw new ArgumentException("Query must be at most " + BlackBoxLimits.MaxSearchQueryChars + " characters.", nameof(request));
            request.SearchText = q;
            return EnumerateAsync(caller, request, token);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<BlackBoxObjectSummary>> EnumerateObjectsAsync(AuthContext caller, string entryId, EnumerationQuery? query, CancellationToken token = default)
        {
            BlackBoxEntry entry = await ReadVisibleAsync(caller, entryId, token).ConfigureAwait(false);
            EnumerationResult<BlackBoxObject> page = await _Database.BlackBoxObjects.EnumerateAsync(entry.Id, query ?? new EnumerationQuery(), token).ConfigureAwait(false);
            return Map(page, SummarizeObject);
        }

        /// <inheritdoc />
        public async Task<BlackBoxObjectReadResult> ReadObjectAsync(AuthContext caller, string entryId, string name, long offset = 0, long? length = null, CancellationToken token = default)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "offset must not be negative.");
            if (length.HasValue && (length.Value < 1 || length.Value > BlackBoxLimits.MaxRestChunkBytes)) throw new ArgumentOutOfRangeException(nameof(length), "length must be 1 to " + BlackBoxLimits.MaxRestChunkBytes + ".");
            if (String.IsNullOrWhiteSpace(name)) throw new BlackBoxNotFoundException("Object name is required.");
            BlackBoxEntry entry = await ReadVisibleAsync(caller, entryId, token).ConfigureAwait(false);
            return await _Objects.ReadAsync(entry, name.Trim(), offset, length ?? BlackBoxLimits.MaxRestChunkBytes, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<BlackBoxObjectChunk> ReadObjectChunkAsync(AuthContext caller, string entryId, string name, long offset = 0, int? length = null, CancellationToken token = default)
        {
            if (length.HasValue && (length.Value < 1 || length.Value > BlackBoxLimits.MaxMcpChunkBytes)) throw new ArgumentOutOfRangeException(nameof(length), "length must be 1 to " + BlackBoxLimits.MaxMcpChunkBytes + ".");
            BlackBoxObjectReadResult read = await ReadObjectAsync(caller, entryId, name, offset, length ?? BlackBoxLimits.MaxMcpChunkBytes, token).ConfigureAwait(false);
            bool text = IsText(read.Object.ContentType);
            byte[] data = read.Data;
            if (text && offset + data.LongLength < read.TotalBytes) data = TrimToUtf8Boundary(data);
            long next = offset + data.LongLength;
            return new BlackBoxObjectChunk
            {
                EntryId = read.Object.EntryId,
                Name = read.Object.Name,
                ContentType = read.Object.ContentType,
                Offset = offset,
                Length = data.LongLength,
                TotalBytes = read.TotalBytes,
                NextOffset = next < read.TotalBytes && data.LongLength > 0 ? next : (long?)null,
                Encoding = text ? "utf-8" : "base64",
                Content = text ? Encoding.UTF8.GetString(data) : Convert.ToBase64String(data),
                Sha256 = read.Object.Sha256
            };
        }

        /// <inheritdoc />
        public async Task<byte[]> DownloadAsync(AuthContext caller, string entryId, CancellationToken token = default)
        {
            BlackBoxEntry entry = await ReadVisibleAsync(caller, entryId, token).ConfigureAwait(false);
            BlackBoxDownloadManifest manifest = new BlackBoxDownloadManifest
            {
                EntryId = entry.Id,
                SchemaVersion = entry.SchemaVersion,
                MissionId = entry.MissionId,
                VoyageId = entry.VoyageId,
                ArmadaVersion = entry.ArmadaVersion,
                CreatedUtc = entry.CreatedUtc,
                GeneratedUtc = DateTime.UtcNow
            };
            using (MemoryStream buffer = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
                {
                    int page = 1;
                    while (manifest.Objects.Count + manifest.MissingObjects.Count < BlackBoxLimits.MaxObjectsPerEntry)
                    {
                        EnumerationResult<BlackBoxObject> objects = await _Database.BlackBoxObjects.EnumerateAsync(entry.Id, new EnumerationQuery { PageNumber = page, PageSize = 100 }, token).ConfigureAwait(false);
                        foreach (BlackBoxObject obj in objects.Objects)
                        {
                            try
                            {
                                BlackBoxObjectReadResult read = await _Objects.ReadAsync(entry, obj.Name, 0, null, token).ConfigureAwait(false);
                                ZipArchiveEntry file = zip.CreateEntry(obj.Name, CompressionLevel.Optimal);
                                using (Stream stream = file.Open()) await stream.WriteAsync(read.Data, 0, read.Data.Length, token).ConfigureAwait(false);
                                if (manifest.Objects.Count < BlackBoxLimits.MaxObjectsPerEntry) manifest.Objects.Add(SummarizeObject(obj));
                            }
                            catch (BlackBoxException e)
                            {
                                _Logging.Warn(_Header + "download of " + entry.Id + " skipped " + obj.Name + ": " + e.Message);
                                if (manifest.MissingObjects.Count < BlackBoxLimits.MaxObjectsPerEntry) manifest.MissingObjects.Add(obj.Name);
                            }
                        }
                        if (page >= objects.TotalPages) break;
                        page++;
                    }
                    ZipArchiveEntry manifestEntry = zip.CreateEntry(BlackBoxObjectNames.Manifest, CompressionLevel.Optimal);
                    byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, BlackBoxJson.Options));
                    using (Stream stream = manifestEntry.Open()) await stream.WriteAsync(manifestBytes, 0, manifestBytes.Length, token).ConfigureAwait(false);
                }
                return buffer.ToArray();
            }
        }

        /// <inheritdoc />
        public async Task<BlackBoxNote> AddNoteAsync(AuthContext caller, string entryId, BlackBoxNoteAddRequest request, BlackBoxNoteAuthorKindEnum defaultAuthor = BlackBoxNoteAuthorKindEnum.Operator, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string body = (request.Body ?? String.Empty).Trim();
            if (body.Length == 0) throw new ArgumentException("Body is required.", nameof(request));
            if (body.Length > BlackBoxLimits.MaxNoteChars) throw new ArgumentException("Body must be at most " + BlackBoxLimits.MaxNoteChars + " characters.", nameof(request));
            BlackBoxEntry entry = await ReadVisibleAsync(caller, entryId, token).ConfigureAwait(false);
            BlackBoxNoteAuthorKindEnum author = request.AuthorKind.HasValue && request.AuthorKind.Value != BlackBoxNoteAuthorKindEnum.Unknown ? request.AuthorKind.Value : defaultAuthor;
            BlackBoxNote note = new BlackBoxNote { EntryId = entry.Id, TenantId = entry.TenantId, UserId = caller.UserId, AuthorKind = author, Body = body, CreatedUtc = DateTime.UtcNow };
            return await _Database.BlackBoxNotes.CreateAsync(note, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<BlackBoxNote>> EnumerateNotesAsync(AuthContext caller, string entryId, EnumerationQuery? query, CancellationToken token = default)
        {
            BlackBoxEntry entry = await ReadVisibleAsync(caller, entryId, token).ConfigureAwait(false);
            return await _Database.BlackBoxNotes.EnumerateAsync(entry.Id, query ?? new EnumerationQuery(), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<BlackBoxEntry> ResolveAsync(AuthContext caller, string entryId, BlackBoxResolveRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Resolution == BlackBoxResolutionEnum.Unknown) throw new ArgumentException("Resolution must be Open, Resolved, Duplicate, WontFix or ArmadaFixed.", nameof(request));
            if (request.Note != null && request.Note.Length > BlackBoxLimits.MaxResolutionNoteChars) throw new ArgumentException("Note must be at most " + BlackBoxLimits.MaxResolutionNoteChars + " characters.", nameof(request));
            if (request.Link != null && request.Link.Length > BlackBoxLimits.MaxResolutionLinkChars) throw new ArgumentException("Link must be at most " + BlackBoxLimits.MaxResolutionLinkChars + " characters.", nameof(request));
            BlackBoxEntry entry = await ReadVisibleAsync(caller, entryId, token).ConfigureAwait(false);
            entry.Resolution = request.Resolution;
            entry.ResolutionNote = String.IsNullOrWhiteSpace(request.Note) ? null : request.Note!.Trim();
            entry.ResolutionLink = String.IsNullOrWhiteSpace(request.Link) ? null : request.Link!.Trim();
            entry.ResolvedUtc = request.Resolution == BlackBoxResolutionEnum.Open ? null : DateTime.UtcNow;
            entry.ExpiresUtc = await ExpiryAsync(entry, token).ConfigureAwait(false);
            await _Database.BlackBoxEntries.UpdateResolutionAsync(entry.Id, entry.Resolution, entry.ResolutionNote, entry.ResolutionLink, entry.ResolvedUtc, entry.ExpiresUtc, token).ConfigureAwait(false);
            await _Events.EmitAsync(BlackBoxEventTypes.Resolved, "Black box entry " + entry.Id + " set to " + entry.Resolution, entry.TenantId, entry.Id, entry.MissionId, entry.VoyageId, entry.VesselId,
                new BlackBoxResolvedPayload { EntryId = entry.Id, MissionId = entry.MissionId, VoyageId = entry.VoyageId, Resolution = entry.Resolution }, token).ConfigureAwait(false);
            return (await _Database.BlackBoxEntries.ReadAsync(entry.Id, null, false, token).ConfigureAwait(false)) ?? entry;
        }

        /// <inheritdoc />
        public async Task<BlackBoxEntry> PinAsync(AuthContext caller, string entryId, bool pinned, CancellationToken token = default)
        {
            BlackBoxEntry entry = await ReadVisibleAsync(caller, entryId, token).ConfigureAwait(false);
            entry.Pinned = pinned;
            entry.ExpiresUtc = await ExpiryAsync(entry, token).ConfigureAwait(false);
            await _Database.BlackBoxEntries.UpdatePinnedAsync(entry.Id, pinned, entry.ExpiresUtc, token).ConfigureAwait(false);
            return (await _Database.BlackBoxEntries.ReadAsync(entry.Id, null, false, token).ConfigureAwait(false)) ?? entry;
        }

        /// <inheritdoc />
        public async Task<BlackBoxDeleteResult> DeleteAsync(AuthContext caller, string entryId, CancellationToken token = default)
        {
            BlackBoxDeleteManyRequest request = new BlackBoxDeleteManyRequest();
            request.Ids.Add(entryId);
            BlackBoxDeleteResult result = await DeleteManyAsync(caller, request, token).ConfigureAwait(false);
            if (result.Deleted == 0 && result.Pending == 0) throw new BlackBoxNotFoundException("Black box entry " + entryId + " not found.");
            return result;
        }

        /// <inheritdoc />
        public async Task<BlackBoxDeleteResult> DeleteManyAsync(AuthContext caller, BlackBoxDeleteManyRequest request, CancellationToken token = default)
        {
            RequireCaller(caller);
            if (request == null) throw new ArgumentNullException(nameof(request));
            CollectionLimits.RequireRequestItems(request.Ids, "Ids");
            BlackBoxDeleteResult result = new BlackBoxDeleteResult { Requested = request.Ids.Count };
            foreach (string id in request.Ids.Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct(StringComparer.Ordinal))
            {
                BlackBoxEntry? entry = await _Database.BlackBoxEntries.ReadAsync(id, caller.IsAdmin ? null : caller.TenantId, true, token).ConfigureAwait(false);
                if (entry == null || !Visible(caller, entry))
                {
                    result.Skipped.Add(id);
                    continue;
                }
                BlackBoxEntryDeleteResult deleted = await _Gc.DeleteEntryAsync(entry, "Manual", token).ConfigureAwait(false);
                if (deleted.Deleted)
                {
                    result.Deleted++;
                    result.BytesFreed += deleted.BytesFreed;
                }
                else
                {
                    result.Pending++;
                    result.Skipped.Add(id);
                }
            }
            return result;
        }

        /// <inheritdoc />
        public async Task<BlackBoxPurgeResult> PurgeAsync(AuthContext caller, BlackBoxPurgeRequest request, CancellationToken token = default)
        {
            RequireCaller(caller);
            if (request == null) throw new ArgumentNullException(nameof(request));
            int max = Math.Clamp(request.MaxEntries, 1, 100000);
            BlackBoxEnumerateRequest filter = Scope(caller, request);
            filter.Order = EnumerationOrderEnum.CreatedAscending;
            if (!request.IncludePinned) filter.Pinned = false;
            BlackBoxPurgeResult result = new BlackBoxPurgeResult { DryRun = request.DryRun };
            result.Matched = await _Database.BlackBoxEntries.CountAsync(filter, token).ConfigureAwait(false);
            if (!request.IncludePinned)
            {
                BlackBoxEnumerateRequest pinned = Scope(caller, request);
                pinned.Pinned = true;
                result.SkippedPinned = (int)Math.Min(Int32.MaxValue, await _Database.BlackBoxEntries.CountAsync(pinned, token).ConfigureAwait(false));
            }
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int page = 1;
            int processed = 0;
            while (processed < max)
            {
                filter.PageNumber = page;
                filter.PageSize = Math.Min(200, max - processed);
                EnumerationResult<BlackBoxEntry> batch = await _Database.BlackBoxEntries.EnumerateAsync(filter, token).ConfigureAwait(false);
                List<BlackBoxEntry> fresh = batch.Objects.Where(e => seen.Add(e.Id)).ToList();
                if (fresh.Count == 0) break;
                foreach (BlackBoxEntry entry in fresh)
                {
                    if (processed >= max) break;
                    processed++;
                    if (result.SampleEntryIds.Count < BlackBoxLimits.MaxSampleIds) result.SampleEntryIds.Add(entry.Id);
                    if (request.DryRun)
                    {
                        result.BytesFreed += entry.TotalBytes;
                        continue;
                    }
                    BlackBoxEntryDeleteResult deleted = await _Gc.DeleteEntryAsync(entry, "Purge", token).ConfigureAwait(false);
                    if (deleted.Deleted) { result.Deleted++; result.BytesFreed += deleted.BytesFreed; }
                    else result.Pending++;
                }
                if (request.DryRun) page++;
            }
            return result;
        }

        /// <inheritdoc />
        public Task<BlackBoxGcReport> RunGcAsync(AuthContext caller, BlackBoxGcRunRequest? request, CancellationToken token = default)
        {
            RequireCaller(caller);
            return _Gc.RunAsync(request?.DryRun ?? false, token);
        }

        /// <inheritdoc />
        public async Task<BlackBoxRetentionStatus> GetRetentionStatusAsync(AuthContext caller, CancellationToken token = default)
        {
            RequireCaller(caller);
            string? tenant = caller.IsAdmin ? null : caller.TenantId;
            BlackBoxRetentionStatus status = new BlackBoxRetentionStatus { Policy = _Settings.BlackBox.Retention, LastRun = _Gc.LastReport, NextRunUtc = _Gc.NextRunUtc };
            status.TotalEntries = await _Database.BlackBoxEntries.CountAsync(new BlackBoxEnumerateRequest { TenantId = tenant }, token).ConfigureAwait(false);
            status.PinnedEntries = await _Database.BlackBoxEntries.CountAsync(new BlackBoxEnumerateRequest { TenantId = tenant, Pinned = true }, token).ConfigureAwait(false);
            status.DeletingEntries = await _Database.BlackBoxEntries.CountAsync(new BlackBoxEnumerateRequest { TenantId = tenant, State = BlackBoxStateEnum.Deleting }, token).ConfigureAwait(false);
            status.TotalBytes = await _Database.BlackBoxEntries.SumBytesAsync(tenant, null, token).ConfigureAwait(false);
            EnumerationResult<BlackBoxEntry> expired = await _Database.BlackBoxEntries.EnumerateExpiredAsync(DateTime.UtcNow, _Settings.BlackBox.Retention.KeepLatestPerMission, new EnumerationQuery { PageSize = 1 }, token).ConfigureAwait(false);
            status.ExpiredEntries = expired.TotalRecords;
            return status;
        }

        /// <inheritdoc />
        public async Task<BlackBoxStorageStatus> GetStorageStatusAsync(AuthContext caller, CancellationToken token = default)
        {
            RequireCaller(caller);
            BlackBoxStorageStatus status = new BlackBoxStorageStatus { Target = _Settings.BlackBox.Storage.Target, FallbackToDatabase = _Settings.BlackBox.Storage.FallbackToDatabase };
            try
            {
                status.Health = await _Stores.GetConfigured().CheckHealthAsync(token).ConfigureAwait(false);
            }
            catch (NotSupportedException e)
            {
                status.Health = new BlackBoxStoreHealth { Target = status.Target, Healthy = false, Message = e.Message, CheckedUtc = DateTime.UtcNow };
            }
            status.Usage = (await _Database.BlackBoxEntries.SumBytesByTargetAsync(caller.IsAdmin ? null : caller.TenantId, token).ConfigureAwait(false)).Take(7).ToList();
            return status;
        }

        /// <inheritdoc />
        public Task<BlackBoxStorageMigrationJob> StartStorageMigrationAsync(AuthContext caller, BlackBoxStorageMigrationRequest request, CancellationToken token = default)
        {
            // TODO(blackbox-wave2: storage): resumable, paged copy-verify-switch-delete job (plan 4.5).
            throw new NotImplementedException("Black box storage migration is not implemented yet. TODO(blackbox-wave2: storage)");
        }

        /// <inheritdoc />
        public Task<BlackBoxStorageMigrationJob> GetStorageMigrationAsync(AuthContext caller, string migrationId, CancellationToken token = default)
        {
            // TODO(blackbox-wave2: storage): read the job by id.
            throw new NotImplementedException("Black box storage migration is not implemented yet. TODO(blackbox-wave2: storage)");
        }

        /// <inheritdoc />
        public Task<BlackBoxBackfillResult> BackfillAsync(AuthContext caller, BlackBoxBackfillRequest? request, CancellationToken token = default)
        {
            RequireCaller(caller);
            BlackBoxBackfillRequest r = request ?? new BlackBoxBackfillRequest();
            return _Sweeper.BackfillAsync(r.Since, r.MaxSubjects, token);
        }

        /// <inheritdoc />
        public async Task<BlackBoxEntry> ReanalyzeAsync(AuthContext caller, string entryId, CancellationToken token = default)
        {
            BlackBoxEntry entry = await ReadVisibleAsync(caller, entryId, token).ConfigureAwait(false);
            BlackBoxEntry? updated = await _Pipeline.ProcessAsync(entry.Id, token).ConfigureAwait(false);
            return updated ?? throw new BlackBoxNotFoundException("Black box entry " + entryId + " not found.");
        }

        /// <inheritdoc />
        public async Task<BlackBoxRecoveryRunResult> RunRecoveryAsync(AuthContext caller, string entryId, string pathId, BlackBoxRecoveryRunRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(pathId)) throw new ArgumentException("pathId is required.", nameof(pathId));
            BlackBoxEntry entry = await ReadVisibleAsync(caller, entryId, token).ConfigureAwait(false);
            BlackBoxRecord record = await _Pipeline.LoadRecordAsync(entry, token).ConfigureAwait(false);
            BlackBoxRecoveryPath path = record.Analysis?.RecoveryPaths.FirstOrDefault(p => String.Equals(p.Id, pathId.Trim(), StringComparison.Ordinal))
                ?? throw new BlackBoxNotFoundException("Recovery path " + pathId + " is not recorded on black box entry " + entry.Id + ".");
            List<BlackBoxRecoveryStep> plan = path.Steps.OrderBy(s => s.Order).Take(BlackBoxLimits.MaxRecoverySteps).ToList();
            foreach (BlackBoxRecoveryStep step in plan.Where(s => s.Action != null)) ValidateAction(entry, record, step.Action!, "Recovery path " + path.Id + " step " + step.Order);
            foreach (BlackBoxRecoveryStep step in plan.Where(s => s.Action != null)) AuthorizeAction(caller, step.Action!, "Recovery path " + path.Id + " step " + step.Order);
            string version = await SubjectVersionAsync(entry, false, token).ConfigureAwait(false);
            string planHash = PlanHash(plan.Select(s => s.Action != null ? BlackBoxRuleTemplate.McpCommand(s.Action) : "manual:" + s.Description));
            bool dataLoss = path.DataLossRisk || plan.Any(s => s.DataLossRisk);

            BlackBoxRecoveryRunResult result = new BlackBoxRecoveryRunResult { EntryId = entry.Id, PathId = path.Id, Plan = plan };
            if (request.DryRun)
            {
                BlackBoxPendingConfirmation pending = Confirmations.Issue(BlackBoxConfirmationStore.RecoveryKind, entry.Id, path.Id, version, planHash, caller);
                result.DryRun = true;
                result.Succeeded = true;
                result.ConfirmToken = pending.Token;
                result.ConfirmTokenExpiresUtc = pending.ExpiresUtc;
                result.Message = "Would run: " + DescribePlan(plan) + ". Confirm with confirmToken before " + pending.ExpiresUtc.ToString("u") + (dataLoss ? "; this path has data-loss risk, so confirm with acknowledgeDataLoss=true only after the user agrees." : ".");
                await EmitRecoveryAsync(entry, path.Id, null, true, true, 0, 0, caller, token).ConfigureAwait(false);
                return result;
            }

            if (String.IsNullOrWhiteSpace(request.ConfirmToken)) throw new BlackBoxConfirmationException("A confirmToken is required: run run_blackbox_recovery with dryRun=true first and confirm with the token it returns.");
            if (dataLoss && !request.AcknowledgeDataLoss) throw new BlackBoxConfirmationException("Recovery path " + path.Id + " can lose work; confirm again with acknowledgeDataLoss=true after the user agrees.");
            BlackBoxPendingConfirmation confirmed = Confirmations.Consume(request.ConfirmToken, BlackBoxConfirmationStore.RecoveryKind, entry.Id, path.Id, caller);
            if (!String.Equals(confirmed.SubjectVersion, version, StringComparison.Ordinal))
                throw new BlackBoxConfirmationException("The " + (entry.MissionId != null ? "mission" : "voyage") + " changed since the dry run; check its state and run a new dry run.");
            if (!String.Equals(confirmed.PlanHash, planHash, StringComparison.Ordinal))
                throw new BlackBoxConfirmationException("Recovery path " + path.Id + " changed since the dry run (the entry was re-analyzed); run a new dry run.");
            IBlackBoxActionExecutor executor = ActionExecutor ?? throw new NotImplementedException("Running black box recovery actions is not available on this host (no action executor is wired).");

            bool failed = false;
            int ran = 0;
            int failures = 0;
            foreach (BlackBoxRecoveryStep step in plan)
            {
                BlackBoxRecoveryStepResult stepResult;
                if (failed)
                {
                    stepResult = new BlackBoxRecoveryStepResult { Order = step.Order, Tool = step.Action?.Tool, Succeeded = false, Message = "Not run: an earlier step failed." };
                }
                else if (step.Action == null)
                {
                    stepResult = new BlackBoxRecoveryStepResult { Order = step.Order, Succeeded = true, Message = "Manual step, not run by Armada: " + step.Description };
                }
                else
                {
                    stepResult = await ExecuteAsync(executor, caller, step.Action, token).ConfigureAwait(false);
                    stepResult.Order = step.Order;
                    ran++;
                    if (!stepResult.Succeeded) { failed = true; failures++; }
                }
                result.StepResults.Add(stepResult);
            }
            result.Succeeded = !failed;
            result.Message = (failed ? "Recovery path " + path.Id + " stopped at a failed step." : "Recovery path " + path.Id + " ran.") + " " + ran + " of " + plan.Count(s => s.Action != null) + " Armada steps ran.";
            string note = "Ran recovery path " + path.Id + " (" + path.Title + "): " + (result.Succeeded ? "succeeded" : "failed") + ". "
                + String.Join(" ", result.StepResults.Select(r => r.Order + ". " + (r.Tool ?? "manual") + ": " + (r.Succeeded ? "ok" : "failed") + (String.IsNullOrWhiteSpace(r.Message) ? String.Empty : " (" + BlackBoxLimits.Clip(r.Message!.Replace('\n', ' '), 300) + ")")));
            await AnnotateAsync(caller, entry, note, request.AuthorKind, token).ConfigureAwait(false);
            await EmitRecoveryAsync(entry, path.Id, null, false, result.Succeeded, ran, failures, caller, token).ConfigureAwait(false);
            return result;
        }

        /// <inheritdoc />
        public async Task<BlackBoxRecommendationApplyResult> ApplyRecommendationAsync(AuthContext caller, string entryId, string recommendationId, BlackBoxRecommendationApplyRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(recommendationId)) throw new ArgumentException("recommendationId is required.", nameof(recommendationId));
            BlackBoxEntry entry = await ReadVisibleAsync(caller, entryId, token).ConfigureAwait(false);
            BlackBoxRecord record = await _Pipeline.LoadRecordAsync(entry, token).ConfigureAwait(false);
            BlackBoxConfigRecommendation rec = record.Analysis?.ConfigRecommendations.FirstOrDefault(r => String.Equals(r.Id, recommendationId.Trim(), StringComparison.Ordinal))
                ?? throw new BlackBoxNotFoundException("Recommendation " + recommendationId + " is not recorded on black box entry " + entry.Id + ".");
            if (rec.ApplyAction == null) throw new ArgumentException("Recommendation " + rec.Id + " is advice only and cannot be applied automatically: " + (rec.HowToApply ?? rec.Rationale ?? "apply it by hand."), nameof(recommendationId));
            ValidateAction(entry, record, rec.ApplyAction, "Recommendation " + rec.Id);
            AuthorizeAction(caller, rec.ApplyAction, "Recommendation " + rec.Id);
            string version = await SubjectVersionAsync(entry, true, token).ConfigureAwait(false);
            string planHash = PlanHash(new[] { BlackBoxRuleTemplate.McpCommand(rec.ApplyAction) });

            BlackBoxRecommendationApplyResult result = new BlackBoxRecommendationApplyResult { EntryId = entry.Id, RecommendationId = rec.Id, Action = rec.ApplyAction };
            if (request.DryRun)
            {
                BlackBoxPendingConfirmation pending = Confirmations.Issue(BlackBoxConfirmationStore.RecommendationKind, entry.Id, rec.Id, version, planHash, caller);
                result.DryRun = true;
                result.Succeeded = true;
                result.ConfirmToken = pending.Token;
                result.ConfirmTokenExpiresUtc = pending.ExpiresUtc;
                result.Message = "Would run " + BlackBoxRuleTemplate.McpCommand(rec.ApplyAction) + " (set " + rec.Setting + " from " + (rec.CurrentValue ?? "unset") + " to " + (rec.SuggestedValue ?? "?") + "). Confirm with confirmToken before " + pending.ExpiresUtc.ToString("u") + ".";
                await EmitRecoveryAsync(entry, rec.Id, rec.Id, true, true, 0, 0, caller, token).ConfigureAwait(false);
                return result;
            }

            if (String.IsNullOrWhiteSpace(request.ConfirmToken)) throw new BlackBoxConfirmationException("A confirmToken is required: run apply_blackbox_recommendation with dryRun=true first and confirm with the token it returns.");
            BlackBoxPendingConfirmation confirmed = Confirmations.Consume(request.ConfirmToken, BlackBoxConfirmationStore.RecommendationKind, entry.Id, rec.Id, caller);
            if (!String.Equals(confirmed.SubjectVersion, version, StringComparison.Ordinal))
                throw new BlackBoxConfirmationException("The mission or vessel changed since the dry run; check the current configuration and run a new dry run.");
            if (!String.Equals(confirmed.PlanHash, planHash, StringComparison.Ordinal))
                throw new BlackBoxConfirmationException("Recommendation " + rec.Id + " changed since the dry run (the entry was re-analyzed); run a new dry run.");
            IBlackBoxActionExecutor executor = ActionExecutor ?? throw new NotImplementedException("Applying black box recommendations is not available on this host (no action executor is wired).");

            BlackBoxRecoveryStepResult outcome = await ExecuteAsync(executor, caller, rec.ApplyAction, token).ConfigureAwait(false);
            result.Succeeded = outcome.Succeeded;
            result.Message = outcome.Message;
            string note = "Applied recommendation " + rec.Id + " (" + rec.Setting + " to " + (rec.SuggestedValue ?? "?") + " on the " + rec.Scope.ToString().ToLowerInvariant() + "): " + (outcome.Succeeded ? "succeeded" : "failed")
                + (String.IsNullOrWhiteSpace(outcome.Message) ? "." : " (" + BlackBoxLimits.Clip(outcome.Message!.Replace('\n', ' '), 500) + ").");
            await AnnotateAsync(caller, entry, note, request.AuthorKind, token).ConfigureAwait(false);
            await EmitRecoveryAsync(entry, rec.Id, rec.Id, false, outcome.Succeeded, 1, outcome.Succeeded ? 0 : 1, caller, token).ConfigureAwait(false);
            return result;
        }

        /// <inheritdoc />
        public async Task<BlackBoxBundleResult> GetBundleAsync(AuthContext caller, BlackBoxBundleRequest? request, CancellationToken token = default)
        {
            RequireCaller(caller);
            BlackBoxBundleRequest r = request ?? new BlackBoxBundleRequest();
            int max = Math.Clamp(r.Max, 1, BlackBoxLimits.MaxBundleEntries);
            BlackBoxEnumerateRequest filter = Scope(caller, r);
            filter.PageNumber = 1;
            filter.PageSize = max;
            filter.Order = EnumerationOrderEnum.CreatedDescending;
            EnumerationResult<BlackBoxEntry> page = await _Database.BlackBoxEntries.EnumerateAsync(filter, token).ConfigureAwait(false);
            List<BlackBoxEntry> entries = page.Objects.Take(max).ToList();
            List<List<BlackBoxEntry>> groups = entries.GroupBy(e => e.Fingerprint ?? e.Id, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(e => e.CreatedUtc).ToList())
                .OrderByDescending(g => g.Count).ThenByDescending(g => g[0].CreatedUtc).ToList();
            BlackBoxBundleResult result = new BlackBoxBundleResult
            {
                Format = r.Format == BlackBoxBundleFormatEnum.Json ? BlackBoxBundleFormatEnum.Json : BlackBoxBundleFormatEnum.Markdown,
                EntryCount = entries.Count,
                FingerprintCount = groups.Count,
                Truncated = page.TotalRecords > entries.Count
            };

            if (result.Format == BlackBoxBundleFormatEnum.Json)
            {
                List<BlackBoxBundleGroup> jsonGroups = new List<BlackBoxBundleGroup>();
                foreach (List<BlackBoxEntry> group in groups)
                {
                    BlackBoxEntry newest = group[0];
                    BlackBoxRecord record = await _Pipeline.LoadRecordAsync(newest, token).ConfigureAwait(false);
                    jsonGroups.Add(new BlackBoxBundleGroup
                    {
                        Fingerprint = newest.Fingerprint ?? newest.Id,
                        Count = group.Count,
                        FirstSeenUtc = group[group.Count - 1].CreatedUtc,
                        LastSeenUtc = newest.CreatedUtc,
                        ArmadaVersions = group.Select(e => e.ArmadaVersion ?? "unknown").Distinct(StringComparer.Ordinal).Take(BlackBoxLimits.MaxBundleVersions).ToList(),
                        Triage = record.Analysis?.Triage?.Verdict ?? newest.Triage,
                        RuleIds = record.Analysis?.Why.Select(w => w.RuleId).Distinct(StringComparer.Ordinal).Take(BlackBoxLimits.MaxFindings).ToList() ?? new List<string>(),
                        Newest = Summarize(newest),
                        Improvements = record.Analysis?.ArmadaImprovements.Take(BlackBoxLimits.MaxImprovements).ToList() ?? new List<BlackBoxImprovement>(),
                        Site = record.Site,
                        Others = group.Skip(1).Select(Summarize).ToList()
                    });
                }
                BlackBoxBundleDocument doc = new BlackBoxBundleDocument { Preamble = BundlePreamble(entries.Count, groups.Count), EntryCount = entries.Count, FingerprintCount = groups.Count, Groups = jsonGroups };
                result.Content = JsonSerializer.Serialize(doc, BlackBoxJson.Options).Replace('—', '-');
                return result;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("# Armada black box maintainer bundle\n\n");
            sb.Append(BundlePreamble(entries.Count, groups.Count)).Append("\n\n");
            foreach (List<BlackBoxEntry> group in groups)
            {
                BlackBoxEntry newest = group[0];
                BlackBoxRecord record = await _Pipeline.LoadRecordAsync(newest, token).ConfigureAwait(false);
                sb.Append("## ").Append(newest.Phase).Append(" / ").Append(newest.Step).Append(newest.FailureKind.HasValue ? " / " + newest.FailureKind.Value : String.Empty).Append(" (").Append(group.Count).Append(group.Count == 1 ? " entry" : " entries").Append(")\n\n");
                sb.Append("- Fingerprint: ").Append(newest.Fingerprint ?? "none").Append('\n');
                sb.Append("- First seen ").Append(group[group.Count - 1].CreatedUtc.ToString("u")).Append(", last seen ").Append(newest.CreatedUtc.ToString("u")).Append('\n');
                sb.Append("- Armada versions: ").Append(String.Join(", ", group.Select(e => e.ArmadaVersion ?? "unknown").Distinct(StringComparer.Ordinal).Take(BlackBoxLimits.MaxBundleVersions))).Append('\n');
                if (record.Analysis != null) sb.Append("- Triage: ").Append(record.Analysis.Triage?.Verdict ?? newest.Triage).Append("; rules: ").Append(String.Join(", ", record.Analysis.Why.Select(w => w.RuleId).Distinct(StringComparer.Ordinal))).Append('\n');
                sb.Append('\n');
                sb.Append(_Renderer.Render(newest, record, BlackBoxAudienceEnum.Maintainer, r.RedactHosts).Replace("\n# ", "\n### ").Replace("\n## ", "\n#### ").Replace("# Armada maintainer brief", "### Armada maintainer brief")).Append('\n');
                if (group.Count > 1)
                {
                    sb.Append("Other entries with this fingerprint:\n\n");
                    foreach (BlackBoxEntry other in group.Skip(1)) sb.Append("- ").Append(other.Id).Append(' ').Append(other.CreatedUtc.ToString("u")).Append(" (").Append(other.ArmadaVersion ?? "unknown").Append("): ").Append(OneLine(BlackBoxLimits.Clip(other.ReasonExcerpt, 200) ?? String.Empty)).Append('\n');
                    sb.Append('\n');
                }
                if (Encoding.UTF8.GetByteCount(sb.ToString()) > BlackBoxLimits.MaxBundleBytes) { result.Truncated = true; break; }
            }
            string content = sb.ToString().Replace('—', '-').Replace('–', '-');
            if (r.RedactHosts) content = BlackBoxBriefRenderer.RedactHosts(content);
            result.Content = content;
            return result;
        }

        /// <inheritdoc />
        public async Task<BlackBoxFingerprintTrend> GetFingerprintTrendAsync(AuthContext caller, string fingerprint, CancellationToken token = default)
        {
            RequireCaller(caller);
            if (String.IsNullOrWhiteSpace(fingerprint)) throw new ArgumentException("fingerprint is required.", nameof(fingerprint));
            BlackBoxFingerprintTrend trend = new BlackBoxFingerprintTrend { Fingerprint = fingerprint.Trim() };
            List<BlackBoxEntry> entries = new List<BlackBoxEntry>();
            int pageNumber = 1;
            while (entries.Count < BlackBoxLimits.MaxTrendEntries)
            {
                BlackBoxEnumerateRequest query = Scope(caller, new BlackBoxEnumerateRequest { Fingerprint = trend.Fingerprint, PageNumber = pageNumber, PageSize = 100, Order = EnumerationOrderEnum.CreatedDescending });
                EnumerationResult<BlackBoxEntry> page = await _Database.BlackBoxEntries.EnumerateAsync(query, token).ConfigureAwait(false);
                entries.AddRange(page.Objects.Take(BlackBoxLimits.MaxTrendEntries - entries.Count));
                if (page.Objects.Count == 0 || pageNumber >= page.TotalPages) break;
                if (entries.Count >= BlackBoxLimits.MaxTrendEntries) { trend.Truncated = page.TotalRecords > entries.Count; break; }
                pageNumber++;
            }
            trend.Total = entries.Count;
            if (entries.Count > 0)
            {
                trend.Phase = entries[0].Phase;
                trend.Step = entries[0].Step;
            }
            trend.Versions = entries.GroupBy(e => String.IsNullOrWhiteSpace(e.ArmadaVersion) ? "unknown" : e.ArmadaVersion!, StringComparer.Ordinal)
                .Select(g => new BlackBoxVersionCount
                {
                    ArmadaVersion = g.Key,
                    Count = g.Count(),
                    OpenCount = g.Count(e => e.Resolution == BlackBoxResolutionEnum.Open),
                    ArmadaFixedCount = g.Count(e => e.Resolution == BlackBoxResolutionEnum.ArmadaFixed),
                    FirstSeenUtc = g.Min(e => e.CreatedUtc),
                    LastSeenUtc = g.Max(e => e.CreatedUtc)
                })
                .OrderBy(v => v.FirstSeenUtc).Take(BlackBoxLimits.MaxTrendVersions).ToList();
            return trend;
        }

        /// <inheritdoc />
        public async Task<BlackBoxFixture> ExportFixtureAsync(AuthContext caller, string entryId, string? name = null, CancellationToken token = default)
        {
            BlackBoxEntry entry = await ReadVisibleAsync(caller, entryId, token).ConfigureAwait(false);
            BlackBoxRecord record = await _Pipeline.LoadRecordAsync(entry, token).ConfigureAwait(false);
            return BlackBoxFixtureExporter.Build(record, _Pipeline.Advisor, name, null, entry.MissionId ?? entry.VoyageId ?? entry.Id, _Redactor);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxHotspot>> EnumerateHotspotsAsync(AuthContext caller, BlackBoxHotspotEnumerateRequest? request, CancellationToken token = default)
        {
            RequireCaller(caller);
            BlackBoxHotspotEnumerateRequest r = request ?? new BlackBoxHotspotEnumerateRequest();
            ScopeBase(caller, r);
            return _Database.BlackBoxFiles.EnumerateHotspotsAsync(r, token);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxFingerprintSummary>> EnumerateFingerprintsAsync(AuthContext caller, BlackBoxFingerprintEnumerateRequest? request, CancellationToken token = default)
        {
            RequireCaller(caller);
            BlackBoxFingerprintEnumerateRequest r = request ?? new BlackBoxFingerprintEnumerateRequest();
            ScopeBase(caller, r);
            return _Database.BlackBoxEntries.EnumerateFingerprintsAsync(r, token);
        }

        /// <summary>
        /// Short form of an entry.
        /// </summary>
        /// <param name="e">Entry.</param>
        /// <returns>The summary.</returns>
        /// <exception cref="ArgumentNullException">Thrown when e is null.</exception>
        public static BlackBoxEntrySummary Summarize(BlackBoxEntry e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            return new BlackBoxEntrySummary
            {
                Id = e.Id,
                SubjectType = e.SubjectType,
                MissionId = e.MissionId,
                VoyageId = e.VoyageId,
                VesselId = e.VesselId,
                CaptainId = e.CaptainId,
                MissionTitle = e.MissionTitle,
                VesselName = e.VesselName,
                Attempt = e.Attempt,
                IsCurrent = e.IsCurrent,
                Phase = e.Phase,
                Step = e.Step,
                FailureKind = e.FailureKind,
                StatusBefore = e.StatusBefore,
                StatusAfter = e.StatusAfter,
                Triage = e.Triage,
                Fingerprint = e.Fingerprint,
                CaptureMode = e.CaptureMode,
                State = e.State,
                Resolution = e.Resolution,
                Pinned = e.Pinned,
                ReasonExcerpt = BlackBoxLimits.Clip(e.ReasonExcerpt, 300),
                TotalBytes = e.TotalBytes,
                ObjectCount = e.ObjectCount,
                CreatedUtc = e.CreatedUtc,
                ExpiresUtc = e.ExpiresUtc
            };
        }

        /// <summary>
        /// Short form of an object.
        /// </summary>
        /// <param name="o">Object.</param>
        /// <returns>The summary.</returns>
        /// <exception cref="ArgumentNullException">Thrown when o is null.</exception>
        public static BlackBoxObjectSummary SummarizeObject(BlackBoxObject o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            return new BlackBoxObjectSummary
            {
                Name = o.Name,
                ContentType = o.ContentType,
                ContentEncoding = o.ContentEncoding,
                SizeBytes = o.SizeBytes,
                UncompressedBytes = o.UncompressedBytes,
                Sha256 = o.Sha256,
                IsMissing = o.IsMissing,
                CreatedUtc = o.CreatedUtc
            };
        }

        #endregion

        #region Private-Methods

        private static void ValidateAction(BlackBoxEntry entry, BlackBoxRecord record, BlackBoxAction action, string label)
        {
            if (!BlackBoxActionCommands.IsSupported(action.Tool)) throw new ArgumentException(label + " names tool " + action.Tool + ", which recovery runs do not allow.");
            if (action.Arguments.Count > BlackBoxLimits.MaxActionArguments) throw new ArgumentException(label + " has too many arguments.");
            string? missionId = entry.MissionId ?? record.Subject?.MissionId;
            string? vesselId = entry.VesselId ?? record.Subject?.VesselId;
            string? dockId = record.Workspace?.DockId ?? record.Subject?.DockId ?? entry.DockId;
            if (action.Arguments.TryGetValue("missionId", out string? m) && !String.Equals(m, missionId, StringComparison.Ordinal))
                throw new ArgumentException(label + " targets mission " + m + ", not this entry's mission " + (missionId ?? "(none)") + ".");
            if (action.Arguments.TryGetValue("vesselId", out string? v) && !String.Equals(v, vesselId, StringComparison.Ordinal))
                throw new ArgumentException(label + " targets vessel " + v + ", not this entry's vessel " + (vesselId ?? "(none)") + ".");
            if (action.Arguments.TryGetValue("dockId", out string? d) && !String.Equals(d, dockId, StringComparison.Ordinal))
                throw new ArgumentException(label + " targets dock " + d + ", not this entry's dock " + (dockId ?? "(none)") + ".");
        }

        private void AuthorizeAction(AuthContext caller, BlackBoxAction action, string label)
        {
            AuthorizationRequirement requirement = McpToolAuthorizationRegistry.GetOrDefault(action.Tool);
            if (_Authorization.IsAuthorized(caller, requirement)) return;
            string needed = requirement.Level == PermissionLevel.AdminOnly ? "an admin" : (requirement.Level == PermissionLevel.TenantAdmin ? "a tenant admin or admin" : "an authenticated caller");
            throw new UnauthorizedAccessException(label + " runs " + action.Tool + ", which requires " + needed + "; you could not call it directly, so the black box cannot run it for you.");
        }

        private async Task<string> SubjectVersionAsync(BlackBoxEntry entry, bool includeVessel, CancellationToken token)
        {
            StringBuilder sb = new StringBuilder();
            if (!String.IsNullOrEmpty(entry.MissionId))
            {
                Mission? mission = await _Database.Missions.ReadAsync(entry.MissionId!, token).ConfigureAwait(false);
                sb.Append("msn:").Append(mission == null ? "missing" : mission.Status + "@" + mission.LastUpdateUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (!String.IsNullOrEmpty(entry.VoyageId))
            {
                Voyage? voyage = await _Database.Voyages.ReadAsync(entry.VoyageId!, token).ConfigureAwait(false);
                sb.Append("vyg:").Append(voyage == null ? "missing" : voyage.Status + "@" + voyage.LastUpdateUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (includeVessel && !String.IsNullOrEmpty(entry.VesselId))
            {
                Vessel? vessel = await _Database.Vessels.ReadAsync(entry.VesselId!, token).ConfigureAwait(false);
                sb.Append("|vsl:").Append(vessel == null ? "missing" : vessel.LastUpdateUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static string PlanHash(IEnumerable<string> steps)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(String.Join("\n", steps));
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }

        private static string DescribePlan(List<BlackBoxRecoveryStep> plan)
        {
            if (plan.Count == 0) return "nothing (the path has no steps)";
            return String.Join(", then ", plan.Select(s => s.Action != null ? BlackBoxRuleTemplate.McpCommand(s.Action) : "(manual) " + OneLine(s.Description)));
        }

        private static async Task<BlackBoxRecoveryStepResult> ExecuteAsync(IBlackBoxActionExecutor executor, AuthContext caller, BlackBoxAction action, CancellationToken token)
        {
            try
            {
                BlackBoxRecoveryStepResult result = await executor.ExecuteAsync(caller, action, token).ConfigureAwait(false) ?? new BlackBoxRecoveryStepResult { Succeeded = false, Message = "The executor returned no result." };
                if (String.IsNullOrEmpty(result.Tool)) result.Tool = action.Tool;
                result.Message = BlackBoxLimits.Clip(result.Message, BlackBoxLimits.MaxReasonExcerptChars);
                return result;
            }
            catch (Exception e) when (!(e is OperationCanceledException && token.IsCancellationRequested))
            {
                return new BlackBoxRecoveryStepResult { Tool = action.Tool, Succeeded = false, Message = e.GetType().Name + ": " + BlackBoxLimits.Clip(e.Message, BlackBoxLimits.MaxReasonExcerptChars) };
            }
        }

        private async Task AnnotateAsync(AuthContext caller, BlackBoxEntry entry, string body, BlackBoxNoteAuthorKindEnum? authorKind, CancellationToken token)
        {
            BlackBoxNoteAuthorKindEnum author = authorKind.HasValue && authorKind.Value != BlackBoxNoteAuthorKindEnum.Unknown ? authorKind.Value : BlackBoxNoteAuthorKindEnum.Agent;
            BlackBoxNote note = new BlackBoxNote
            {
                EntryId = entry.Id,
                TenantId = entry.TenantId,
                UserId = caller.UserId,
                AuthorKind = author,
                Body = BlackBoxLimits.Clip(body.Replace('\u2014', '-'), BlackBoxLimits.MaxNoteChars) ?? String.Empty,
                CreatedUtc = DateTime.UtcNow
            };
            try
            {
                await _Database.BlackBoxNotes.CreateAsync(note, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not annotate " + entry.Id + " with the recovery outcome: " + e.Message);
            }
        }

        private async Task EmitRecoveryAsync(BlackBoxEntry entry, string pathId, string? recommendationId, bool dryRun, bool succeeded, int stepsRun, int stepsFailed, AuthContext caller, CancellationToken token)
        {
            string what = recommendationId != null ? "Recommendation " + recommendationId : "Recovery path " + pathId;
            string message = what + (dryRun ? " dry run" : (succeeded ? " ran" : " failed")) + " on black box entry " + entry.Id;
            await _Events.EmitAsync(BlackBoxEventTypes.RecoveryRun, message, entry.TenantId, entry.Id, entry.MissionId, entry.VoyageId, entry.VesselId,
                new BlackBoxRecoveryRunPayload { EntryId = entry.Id, PathId = pathId, RecommendationId = recommendationId, DryRun = dryRun, Succeeded = succeeded, StepsRun = stepsRun, StepsFailed = stepsFailed, UserId = caller.UserId }, token).ConfigureAwait(false);
        }

        private static string BundlePreamble(int entries, int groups)
        {
            return "These are " + entries + " Armada black box entries in " + groups + " failure modes, grouped by fingerprint (most frequent first). "
                + "You are asked to improve Armada itself. Rank the Armada improvements these failures suggest by impact (occurrences, severity, triage ArmadaDefect first), "
                + "check each hypothesis against the Armada source at the build commits listed, and discard the ones the evidence does not support. "
                + "Write each accepted improvement as an Armada mission brief: a title, the failure modes and entry ids it fixes, the code sites (file and method), "
                + "the change, and a test that reproduces the failure (a black box fixture under src/Test.Shared/Fixtures/BlackBox when it fits). "
                + "If a failure needs a new advisor rule, write it in BlackBoxRuleCatalog format (id, match, why, recommendation, recovery paths, triage vote).";
        }

        private static string OneLine(string value)
        {
            return (value ?? String.Empty).Replace("\r", " ").Replace("\n", " ");
        }

        private static void RequireCaller(AuthContext caller)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (!caller.IsAuthenticated) throw new UnauthorizedAccessException("Authentication required.");
            if (!caller.IsAdmin && String.IsNullOrEmpty(caller.TenantId)) throw new UnauthorizedAccessException("A tenant is required.");
        }

        private static bool Visible(AuthContext caller, BlackBoxEntry entry)
        {
            if (caller.IsAdmin) return true;
            if (String.IsNullOrEmpty(entry.TenantId) || !String.Equals(entry.TenantId, caller.TenantId, StringComparison.Ordinal)) return false;
            if (caller.IsTenantAdmin) return true;
            return String.Equals(entry.UserId, caller.UserId, StringComparison.Ordinal);
        }

        private async Task<BlackBoxEntry> ReadVisibleAsync(AuthContext caller, string entryId, CancellationToken token)
        {
            RequireCaller(caller);
            if (String.IsNullOrWhiteSpace(entryId)) throw new BlackBoxNotFoundException("Black box entry id is required.");
            BlackBoxEntry? entry = await _Database.BlackBoxEntries.ReadAsync(entryId.Trim(), caller.IsAdmin ? null : caller.TenantId, true, token).ConfigureAwait(false);
            if (entry == null || !Visible(caller, entry)) throw new BlackBoxNotFoundException("Black box entry " + entryId + " not found.");
            if (entry.State == BlackBoxStateEnum.Deleting) throw new BlackBoxDeletingException("Black box entry " + entryId + " is being deleted.");
            return entry;
        }

        private static void ScopeBase(AuthContext caller, EnumerationQuery query)
        {
            if (caller.IsAdmin) return;
            query.TenantId = caller.TenantId;
            if (!caller.IsTenantAdmin) query.UserId = caller.UserId;
        }

        private static BlackBoxEnumerateRequest Scope(AuthContext caller, BlackBoxEnumerateRequest source)
        {
            if (source.CreatedAfter.HasValue && source.CreatedBefore.HasValue && source.CreatedAfter.Value > source.CreatedBefore.Value)
                throw new ArgumentException("createdAfter must not be after createdBefore.");
            BlackBoxEnumerateRequest q = new BlackBoxEnumerateRequest
            {
                PageNumber = source.PageNumber,
                PageSize = source.PageSize,
                Order = source.Order,
                CreatedAfter = source.CreatedAfter,
                CreatedBefore = source.CreatedBefore,
                FleetId = Blank(source.FleetId),
                VesselId = Blank(source.VesselId),
                CaptainId = Blank(source.CaptainId),
                VoyageId = Blank(source.VoyageId),
                MissionId = Blank(source.MissionId),
                TenantId = Blank(source.TenantId),
                UserId = Blank(source.UserId),
                SubjectType = source.SubjectType,
                Phase = source.Phase,
                Step = source.Step,
                Kind = source.Kind,
                Triage = source.Triage,
                Resolution = source.Resolution,
                State = source.State,
                CaptureMode = source.CaptureMode,
                Fingerprint = Blank(source.Fingerprint),
                Pinned = source.Pinned,
                IsCurrent = source.IsCurrent,
                HarborId = Blank(source.HarborId),
                Path = Blank(source.Path)
            };
            ScopeBase(caller, q);
            return q;
        }

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private async Task<DateTime?> ExpiryAsync(BlackBoxEntry entry, CancellationToken token)
        {
            int? vesselDays = null;
            if (!String.IsNullOrEmpty(entry.VesselId))
            {
                Vessel? vessel = await _Database.Vessels.ReadAsync(entry.VesselId, token).ConfigureAwait(false);
                vesselDays = vessel?.BlackBoxRetentionDays;
            }
            return BlackBoxRetentionPolicy.ComputeExpiry(entry, _Settings.BlackBox.Retention, vesselDays);
        }

        private async Task<List<string>> NotesForBriefAsync(BlackBoxEntry entry, CancellationToken token)
        {
            EnumerationResult<BlackBoxNote> notes = await _Database.BlackBoxNotes.EnumerateAsync(entry.Id, new EnumerationQuery { PageSize = 10 }, token).ConfigureAwait(false);
            return notes.Objects.Select(n => n.CreatedUtc.ToString("u") + " " + n.AuthorKind + ": " + (BlackBoxLimits.Clip(n.Body, 500) ?? String.Empty)).ToList();
        }

        private static EnumerationResult<TOut> Map<TIn, TOut>(EnumerationResult<TIn> page, Func<TIn, TOut> map)
        {
            EnumerationResult<TOut> result = new EnumerationResult<TOut>();
            result.PageNumber = page.PageNumber;
            result.PageSize = page.PageSize;
            result.TotalPages = page.TotalPages;
            result.TotalRecords = page.TotalRecords;
            result.Objects = page.Objects.Select(map).ToList();
            return result;
        }

        private static bool IsText(string? contentType)
        {
            if (String.IsNullOrEmpty(contentType)) return false;
            return contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
        }

        private static byte[] TrimToUtf8Boundary(byte[] data)
        {
            int i = data.Length - 1;
            int continuation = 0;
            while (i >= 0 && continuation < 3 && (data[i] & 0xC0) == 0x80) { i--; continuation++; }
            if (i <= 0) return data;
            byte lead = data[i];
            int needed = lead < 0x80 ? 1 : (lead & 0xE0) == 0xC0 ? 2 : (lead & 0xF0) == 0xE0 ? 3 : (lead & 0xF8) == 0xF0 ? 4 : 1;
            if (needed == continuation + 1) return data;
            byte[] trimmed = new byte[i];
            Array.Copy(data, trimmed, i);
            return trimmed;
        }

        #endregion
    }
}
