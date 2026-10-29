namespace Armada.Core.BlackBox
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The single black box facade every surface calls (REST, MCP, WebSocket, Armada.Client, the dashboard, TUI, Helm).
    /// Authorization (which roles may call what) is done by the routes and tool registries; tenant and owner scoping is
    /// done here: a global admin sees every tenant, a tenant admin their tenant, a user their own entries. An entry that
    /// is missing, in another tenant, or not the caller's reads as <see cref="BlackBoxNotFoundException"/> (no existence
    /// leak). Entries being deleted read as <see cref="BlackBoxDeletingException"/>. Thread-safe.
    /// </summary>
    public interface IBlackBoxService
    {
        /// <summary>Current entry of a mission.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="missionId">Mission id.</param>
        /// <param name="includeRecord">Populate Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry.</returns>
        /// <exception cref="BlackBoxNotFoundException">Thrown when there is no visible entry.</exception>
        Task<BlackBoxEntry> GetMissionEntryAsync(AuthContext caller, string missionId, bool includeRecord = true, CancellationToken token = default);

        /// <summary>Current entry of a voyage.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="voyageId">Voyage id.</param>
        /// <param name="includeRecord">Populate Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry.</returns>
        /// <exception cref="BlackBoxNotFoundException">Thrown when there is no visible entry.</exception>
        Task<BlackBoxEntry> GetVoyageEntryAsync(AuthContext caller, string voyageId, bool includeRecord = true, CancellationToken token = default);

        /// <summary>An entry by id.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="includeRecord">Populate Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry.</returns>
        /// <exception cref="BlackBoxNotFoundException">Thrown when there is no visible entry.</exception>
        /// <exception cref="BlackBoxDeletingException">Thrown when the entry is being deleted.</exception>
        Task<BlackBoxEntry> GetEntryAsync(AuthContext caller, string entryId, bool includeRecord = true, CancellationToken token = default);

        /// <summary>Resolve whichever of mission id, voyage id or entry id is given to an entry id (entry id wins).</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="missionId">Mission id, or null.</param>
        /// <param name="voyageId">Voyage id, or null.</param>
        /// <param name="entryId">Entry id, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry id.</returns>
        /// <exception cref="ArgumentException">Thrown when none is given.</exception>
        /// <exception cref="BlackBoxNotFoundException">Thrown when there is no visible entry.</exception>
        Task<string> ResolveEntryIdAsync(AuthContext caller, string? missionId, string? voyageId, string? entryId, CancellationToken token = default);

        /// <summary>Compact view (about 2 KB) for MCP get_blackbox format=summary.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The digest.</returns>
        /// <exception cref="BlackBoxNotFoundException">Thrown when there is no visible entry.</exception>
        Task<BlackBoxDigest> GetDigestAsync(AuthContext caller, string entryId, CancellationToken token = default);

        /// <summary>An entry's record (record.json).</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record.</returns>
        /// <exception cref="BlackBoxNotFoundException">Thrown when there is no visible entry.</exception>
        Task<BlackBoxRecord> GetRecordAsync(AuthContext caller, string entryId, CancellationToken token = default);

        /// <summary>One section of an entry's record.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="section">Section.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The section result.</returns>
        /// <exception cref="BlackBoxNotFoundException">Thrown when there is no visible entry.</exception>
        Task<BlackBoxSectionResult> GetSectionAsync(AuthContext caller, string entryId, BlackBoxSectionEnum section, CancellationToken token = default);

        /// <summary>The FIX THIS brief, rendered from the record for an audience (Markdown).</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="audience">Audience (Agent by default).</param>
        /// <param name="redactHosts">Strip absolute paths and host names.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Markdown.</returns>
        /// <exception cref="BlackBoxNotFoundException">Thrown when there is no visible entry.</exception>
        Task<string> GetBriefAsync(AuthContext caller, string entryId, BlackBoxAudienceEnum audience = BlackBoxAudienceEnum.Agent, bool redactHosts = false, CancellationToken token = default);

        /// <summary>Every attempt of a mission, newest first by default.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="missionId">Mission id.</param>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        Task<EnumerationResult<BlackBoxEntrySummary>> EnumerateMissionEntriesAsync(AuthContext caller, string missionId, EnumerationQuery? query, CancellationToken token = default);

        /// <summary>Enumerate entries with filters.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="query">Filters and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        Task<EnumerationResult<BlackBoxEntrySummary>> EnumerateAsync(AuthContext caller, BlackBoxEnumerateRequest? query, CancellationToken token = default);

        /// <summary>Full-text search over reason, summary, title and notes.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Query, filters and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentException">Thrown when the query is empty or longer than 200 characters.</exception>
        Task<EnumerationResult<BlackBoxEntrySummary>> SearchAsync(AuthContext caller, BlackBoxSearchRequest request, CancellationToken token = default);

        /// <summary>An entry's objects.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        Task<EnumerationResult<BlackBoxObjectSummary>> EnumerateObjectsAsync(AuthContext caller, string entryId, EnumerationQuery? query, CancellationToken token = default);

        /// <summary>Read an object's uncompressed bytes (REST: at most 1 MB per read).</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="name">Object name.</param>
        /// <param name="offset">Offset (at least 0).</param>
        /// <param name="length">Length (1 to 1048576), or null for up to 1 MB.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The bytes read and the object's metadata.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for a negative offset or a length out of range.</exception>
        /// <exception cref="BlackBoxObjectMissingException">Thrown when the bytes are missing.</exception>
        /// <exception cref="BlackBoxObjectCorruptException">Thrown when the SHA-256 does not match.</exception>
        Task<Armada.Core.BlackBox.Storage.BlackBoxObjectReadResult> ReadObjectAsync(AuthContext caller, string entryId, string name, long offset = 0, long? length = null, CancellationToken token = default);

        /// <summary>Read a chunk of an object for MCP (at most 64 KB), with nextOffset and totalBytes.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="name">Object name.</param>
        /// <param name="offset">Offset (at least 0).</param>
        /// <param name="length">Length (1 to 65536), or null for 64 KB.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The chunk.</returns>
        Task<BlackBoxObjectChunk> ReadObjectChunkAsync(AuthContext caller, string entryId, string name, long offset = 0, int? length = null, CancellationToken token = default);

        /// <summary>A .zip of every object plus manifest.json.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Zip bytes.</returns>
        Task<byte[]> DownloadAsync(AuthContext caller, string entryId, CancellationToken token = default);

        /// <summary>Add a note.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="request">Body and author kind.</param>
        /// <param name="defaultAuthor">Author kind when the request has none (Operator for REST, Agent for MCP).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The note.</returns>
        /// <exception cref="ArgumentException">Thrown when the body is empty or longer than 8000 characters.</exception>
        Task<BlackBoxNote> AddNoteAsync(AuthContext caller, string entryId, BlackBoxNoteAddRequest request, BlackBoxNoteAuthorKindEnum defaultAuthor = BlackBoxNoteAuthorKindEnum.Operator, CancellationToken token = default);

        /// <summary>An entry's notes.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        Task<EnumerationResult<BlackBoxNote>> EnumerateNotesAsync(AuthContext caller, string entryId, EnumerationQuery? query, CancellationToken token = default);

        /// <summary>Resolve or reopen an entry (expiry recomputed; emits blackbox.resolved).</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="request">Resolution, note, link.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry.</returns>
        Task<BlackBoxEntry> ResolveAsync(AuthContext caller, string entryId, BlackBoxResolveRequest request, CancellationToken token = default);

        /// <summary>Pin or unpin an entry (expiry recomputed).</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="pinned">Pinned.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry.</returns>
        Task<BlackBoxEntry> PinAsync(AuthContext caller, string entryId, bool pinned, CancellationToken token = default);

        /// <summary>Delete one entry (two-phase).</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        Task<BlackBoxDeleteResult> DeleteAsync(AuthContext caller, string entryId, CancellationToken token = default);

        /// <summary>Delete up to 1000 entries.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        /// <exception cref="Armada.Core.Services.CollectionLimitExceededException">Thrown for more than 1000 ids.</exception>
        Task<BlackBoxDeleteResult> DeleteManyAsync(AuthContext caller, BlackBoxDeleteManyRequest request, CancellationToken token = default);

        /// <summary>Purge entries by filter (dry run by default; pinned skipped unless IncludePinned).</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Filter and options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        Task<BlackBoxPurgeResult> PurgeAsync(AuthContext caller, BlackBoxPurgeRequest request, CancellationToken token = default);

        /// <summary>Run garbage collection now (dry run returns what would go).</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The report.</returns>
        Task<BlackBoxGcReport> RunGcAsync(AuthContext caller, BlackBoxGcRunRequest? request, CancellationToken token = default);

        /// <summary>Retention policy, usage, last and next run.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The status.</returns>
        Task<BlackBoxRetentionStatus> GetRetentionStatusAsync(AuthContext caller, CancellationToken token = default);

        /// <summary>Storage target, health and usage by target.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The status.</returns>
        Task<BlackBoxStorageStatus> GetStorageStatusAsync(AuthContext caller, CancellationToken token = default);

        /// <summary>Start a storage migration job.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Target and options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job.</returns>
        /// <exception cref="NotImplementedException">Thrown until external targets land (TODO(blackbox-wave2: storage)).</exception>
        Task<BlackBoxStorageMigrationJob> StartStorageMigrationAsync(AuthContext caller, BlackBoxStorageMigrationRequest request, CancellationToken token = default);

        /// <summary>A storage migration job.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="migrationId">Job id (bbm_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job.</returns>
        /// <exception cref="NotImplementedException">Thrown until external targets land (TODO(blackbox-wave2: storage)).</exception>
        Task<BlackBoxStorageMigrationJob> GetStorageMigrationAsync(AuthContext caller, string migrationId, CancellationToken token = default);

        /// <summary>Backfill historical failures (synchronous, bounded by MaxSubjects).</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Since and maximum.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        Task<BlackBoxBackfillResult> BackfillAsync(AuthContext caller, BlackBoxBackfillRequest? request, CancellationToken token = default);

        /// <summary>Re-run enrichment and analysis for an entry now.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated entry.</returns>
        Task<BlackBoxEntry> ReanalyzeAsync(AuthContext caller, string entryId, CancellationToken token = default);

        /// <summary>Run (or dry run) a recovery path recorded on the entry.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="pathId">Recovery path id.</param>
        /// <param name="request">Dry run, confirm token, acknowledgeDataLoss.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        /// <exception cref="NotImplementedException">Thrown until recovery actions land (TODO(blackbox-wave2: recovery)).</exception>
        Task<BlackBoxRecoveryRunResult> RunRecoveryAsync(AuthContext caller, string entryId, string pathId, BlackBoxRecoveryRunRequest request, CancellationToken token = default);

        /// <summary>Apply (or dry run) a configuration recommendation recorded on the entry.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="recommendationId">Recommendation id.</param>
        /// <param name="request">Dry run and confirm token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        /// <exception cref="NotImplementedException">Thrown until recovery actions land (TODO(blackbox-wave2: recovery)).</exception>
        Task<BlackBoxRecommendationApplyResult> ApplyRecommendationAsync(AuthContext caller, string entryId, string recommendationId, BlackBoxRecommendationApplyRequest request, CancellationToken token = default);

        /// <summary>Maintainer bundle of up to 50 entries grouped by fingerprint.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Filters, maximum, format.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The bundle.</returns>
        Task<BlackBoxBundleResult> GetBundleAsync(AuthContext caller, BlackBoxBundleRequest? request, CancellationToken token = default);

        /// <summary>Conflict hot spots (directories by vessel), paged.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Filters and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        Task<EnumerationResult<BlackBoxHotspot>> EnumerateHotspotsAsync(AuthContext caller, BlackBoxHotspotEnumerateRequest? request, CancellationToken token = default);

        /// <summary>Failure-mode fingerprints, paged.</summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Filters and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        Task<EnumerationResult<BlackBoxFingerprintSummary>> EnumerateFingerprintsAsync(AuthContext caller, BlackBoxFingerprintEnumerateRequest? request, CancellationToken token = default);
    }
}
