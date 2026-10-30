namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for black box entries (blackbox_entries). Every list is paged in SQL with a COUNT over the same
    /// filter, ordered deterministically with the id as the tie-break. A null tenant id means any tenant (internal callers
    /// and global admins); a non-null tenant id scopes the read. Entries in the Deleting state are hidden from reads and
    /// lists unless asked for.
    /// </summary>
    public interface IBlackBoxEntryMethods
    {
        /// <summary>
        /// Insert an entry as given (no attempt numbering).
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        Task<BlackBoxEntry> CreateAsync(BlackBoxEntry entry, CancellationToken token = default);

        /// <summary>
        /// Insert an entry as the current entry of its subject (mission, or voyage for a voyage entry) in one transaction:
        /// numbers the attempt (highest attempt plus one), marks the previous current entry not current, and sets
        /// SupersedesId. Concurrent calls for the same subject in one process are serialized, so a subject has exactly one
        /// current entry.
        /// </summary>
        /// <param name="entry">Entry; Attempt, IsCurrent and SupersedesId are set by this method.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry with Attempt, IsCurrent and SupersedesId set.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the entry has no subject id.</exception>
        Task<BlackBoxEntry> CreateCurrentAsync(BlackBoxEntry entry, CancellationToken token = default);

        /// <summary>
        /// Read an entry by id.
        /// </summary>
        /// <param name="id">Entry id.</param>
        /// <param name="tenantId">Tenant scope, or null for any tenant.</param>
        /// <param name="includeDeleting">Whether an entry in the Deleting state is returned.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<BlackBoxEntry?> ReadAsync(string id, string? tenantId = null, bool includeDeleting = false, CancellationToken token = default);

        /// <summary>
        /// Read the current entry of a mission (mission entries only).
        /// </summary>
        /// <param name="missionId">Mission id.</param>
        /// <param name="tenantId">Tenant scope, or null for any tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The current entry, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when missionId is null or empty.</exception>
        Task<BlackBoxEntry?> ReadCurrentByMissionAsync(string missionId, string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// Read the current entry of a voyage (voyage entries only).
        /// </summary>
        /// <param name="voyageId">Voyage id.</param>
        /// <param name="tenantId">Tenant scope, or null for any tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The current entry, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when voyageId is null or empty.</exception>
        Task<BlackBoxEntry?> ReadCurrentByVoyageAsync(string voyageId, string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// Enumerate entries with every filter of the request (TenantId and UserId scope it), paged in SQL. Order is
        /// created_utc then id, per Order (CreatedDescending by default).
        /// </summary>
        /// <param name="query">Filters and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page with exact totals.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<BlackBoxEntry>> EnumerateAsync(BlackBoxEnumerateRequest query, CancellationToken token = default);

        /// <summary>
        /// Enumerate every attempt of a mission (mission entries), newest first by default.
        /// </summary>
        /// <param name="missionId">Mission id.</param>
        /// <param name="tenantId">Tenant scope, or null for any tenant.</param>
        /// <param name="query">Paging and order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when missionId is null or empty, or query is null.</exception>
        Task<EnumerationResult<BlackBoxEntry>> EnumerateByMissionAsync(string missionId, string? tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate entries with a fingerprint, newest first by default.
        /// </summary>
        /// <param name="fingerprint">Fingerprint.</param>
        /// <param name="tenantId">Tenant scope, or null for any tenant.</param>
        /// <param name="query">Paging and order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when fingerprint is null or empty, or query is null.</exception>
        Task<EnumerationResult<BlackBoxEntry>> EnumerateByFingerprintAsync(string fingerprint, string? tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Count entries matching the filters of the request (paging ignored).
        /// </summary>
        /// <param name="query">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The count.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<long> CountAsync(BlackBoxEnumerateRequest query, CancellationToken token = default);

        /// <summary>
        /// Update every mutable column of an entry (not id, tenant, subject ids or creation time); sets LastUpdateUtc.
        /// is_current and supersedes_id are owned by <see cref="CreateCurrentAsync"/> and <see cref="MarkSupersededAsync"/>
        /// and never written here, so a writer holding a stale copy of a superseded entry cannot make it current again.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        Task<BlackBoxEntry> UpdateAsync(BlackBoxEntry entry, CancellationToken token = default);

        /// <summary>
        /// Mark an entry not current.
        /// </summary>
        /// <param name="id">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<bool> MarkSupersededAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Set an entry's state and last error (and, for EnrichmentFailed, the attempt count and next attempt time).
        /// </summary>
        /// <param name="id">Entry id.</param>
        /// <param name="state">State.</param>
        /// <param name="lastError">Last error (redacted), or null.</param>
        /// <param name="enrichmentAttempts">Enrichment attempts, or null to keep.</param>
        /// <param name="nextAttemptUtc">Next attempt time, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<bool> UpdateStateAsync(string id, BlackBoxStateEnum state, string? lastError = null, int? enrichmentAttempts = null, DateTime? nextAttemptUtc = null, CancellationToken token = default);

        /// <summary>
        /// Set an entry's resolution, note, link, resolved time and recomputed expiry.
        /// </summary>
        /// <param name="id">Entry id.</param>
        /// <param name="resolution">Resolution.</param>
        /// <param name="note">Note, or null.</param>
        /// <param name="link">Link, or null.</param>
        /// <param name="resolvedUtc">Resolved time, or null when reopened.</param>
        /// <param name="expiresUtc">Recomputed expiry, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<bool> UpdateResolutionAsync(string id, BlackBoxResolutionEnum resolution, string? note, string? link, DateTime? resolvedUtc, DateTime? expiresUtc, CancellationToken token = default);

        /// <summary>
        /// Pin or unpin an entry and set its recomputed expiry.
        /// </summary>
        /// <param name="id">Entry id.</param>
        /// <param name="pinned">Pinned.</param>
        /// <param name="expiresUtc">Recomputed expiry (null when pinned).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<bool> UpdatePinnedAsync(string id, bool pinned, DateTime? expiresUtc, CancellationToken token = default);

        /// <summary>
        /// Set an entry's expiry.
        /// </summary>
        /// <param name="id">Entry id.</param>
        /// <param name="expiresUtc">Expiry, or null for never.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<bool> UpdateExpiresAsync(string id, DateTime? expiresUtc, CancellationToken token = default);

        /// <summary>
        /// Delete an entry and every row that belongs to it (objects, files, links, notes) in one transaction. The caller
        /// deletes external object bytes first (two-phase delete).
        /// </summary>
        /// <param name="id">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the entry row was deleted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<bool> DeleteCascadeAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate expiry candidates: expires_utc before now, not pinned, not Deleting, and (when keepLatestPerMission)
        /// not the current entry of a mission or voyage that still exists. Oldest expiry first, id as the tie-break.
        /// </summary>
        /// <param name="nowUtc">Now (UTC).</param>
        /// <param name="keepLatestPerMission">Protect current entries of existing subjects.</param>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<BlackBoxEntry>> EnumerateExpiredAsync(DateTime nowUtc, bool keepLatestPerMission, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate entries in the Deleting state (left by a failed store delete), oldest first.
        /// </summary>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<BlackBoxEntry>> EnumerateDeletingAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate EnrichmentFailed entries due for a retry: fewer than maxAttempts attempts and next_attempt_utc null or
        /// at or before now. Oldest first.
        /// </summary>
        /// <param name="nowUtc">Now (UTC).</param>
        /// <param name="maxAttempts">Most attempts.</param>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<BlackBoxEntry>> EnumerateRetryableAsync(DateTime nowUtc, int maxAttempts, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Aggregate entries by fingerprint (entries without one are skipped), paged in SQL: count descending, last seen
        /// descending, fingerprint. TenantId and VesselId of the request scope it.
        /// </summary>
        /// <param name="query">Filters and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<BlackBoxFingerprintSummary>> EnumerateFingerprintsAsync(BlackBoxFingerprintEnumerateRequest query, CancellationToken token = default);

        /// <summary>
        /// Sum stored bytes of entries.
        /// </summary>
        /// <param name="tenantId">Tenant scope, or null for all.</param>
        /// <param name="vesselId">Vessel, or null for all.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Bytes.</returns>
        Task<long> SumBytesAsync(string? tenantId = null, string? vesselId = null, CancellationToken token = default);

        /// <summary>
        /// Entry count and stored bytes per storage target (at most one row per BlackBoxStorageTargetEnum value).
        /// </summary>
        /// <param name="tenantId">Tenant scope, or null for all.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Usage rows.</returns>
        Task<List<BlackBoxStorageTargetUsage>> SumBytesByTargetAsync(string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// Count entries of a vessel.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="tenantId">Tenant scope, or null for all.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The count.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vesselId is null or empty.</exception>
        Task<long> CountByVesselAsync(string vesselId, string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// Enumerate ids of missions in a terminal failure state (Failed, LandingFailed, Blocked, or Cancelled with a
        /// failure kind other than OperatorAction) whose last entry is missing, or older than their last update and no
        /// longer covering them: the mission re-entered a failure state (another status, an attempt started or finished
        /// after the entry) or its FailureKind or FailureReason changed. Any other edit is not a new failure. Oldest
        /// update first, id as the tie-break.
        /// </summary>
        /// <param name="updatedSince">Only missions updated at or after this time, or null.</param>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page of mission ids.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<string>> EnumerateUncapturedMissionIdsAsync(DateTime? updatedSince, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate ids of failed or cancelled voyages with no voyage entry. Oldest update first.
        /// </summary>
        /// <param name="updatedSince">Only voyages updated at or after this time, or null.</param>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page of voyage ids.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<string>> EnumerateUncapturedVoyageIdsAsync(DateTime? updatedSince, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Set missions.last_blackbox_entry_id without touching any other column (last_update_utc unchanged).
        /// </summary>
        /// <param name="missionId">Mission id.</param>
        /// <param name="entryId">Entry id, or null to clear.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when missionId is null or empty.</exception>
        Task<bool> SetMissionLastEntryAsync(string missionId, string? entryId, CancellationToken token = default);

        /// <summary>
        /// Set voyages.last_blackbox_entry_id without touching any other column.
        /// </summary>
        /// <param name="voyageId">Voyage id.</param>
        /// <param name="entryId">Entry id, or null to clear.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when voyageId is null or empty.</exception>
        Task<bool> SetVoyageLastEntryAsync(string voyageId, string? entryId, CancellationToken token = default);

        /// <summary>
        /// Quota eviction candidates, oldest first (created, then id).
        /// </summary>
        /// <param name="query">Filter and page size.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The first page of candidates and the total count.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<BlackBoxEntry>> EnumerateEvictionCandidatesAsync(BlackBoxEvictionQuery query, CancellationToken token = default);

        /// <summary>
        /// Vessels holding more than maxEntries entries (Deleting excluded), most entries first.
        /// </summary>
        /// <param name="maxEntries">Per-vessel limit.</param>
        /// <param name="query">Page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of vessel ids.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<string>> EnumerateVesselsOverQuotaAsync(int maxEntries, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Entries to delete with their subject: MissionDeleted (mission entries whose mission row is gone), VesselDeleted
        /// (entries whose vessel row is gone) and TenantDeleted (every entry of subjectId's tenant). Pinned entries are
        /// excluded except for TenantDeleted; entries in the Deleting state are excluded.
        /// </summary>
        /// <param name="reason">Cascade.</param>
        /// <param name="subjectId">Limit to this mission, vessel or tenant id (required for TenantDeleted), or null.</param>
        /// <param name="query">Page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of entries, oldest first.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null, or subjectId is null for TenantDeleted.</exception>
        Task<EnumerationResult<BlackBoxEntry>> EnumerateCascadeCandidatesAsync(BlackBoxCascadeReasonEnum reason, string? subjectId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Keyset scan in id order (entries after query.AfterId), filtered by storage target and vessel.
        /// </summary>
        /// <param name="query">Scan.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The next page; TotalRecords counts the entries remaining after the cursor.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<BlackBoxEntry>> ScanAsync(BlackBoxScanQuery query, CancellationToken token = default);

        /// <summary>
        /// Switch an entry's storage target and key prefix (storage migration), without touching last_update_utc.
        /// </summary>
        /// <param name="id">Entry id.</param>
        /// <param name="target">Target.</param>
        /// <param name="storagePrefix">Key prefix for an external target, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the row was updated.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<bool> UpdateStorageAsync(string id, BlackBoxStorageTargetEnum target, string? storagePrefix, CancellationToken token = default);
    }
}
