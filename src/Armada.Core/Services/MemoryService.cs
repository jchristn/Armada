namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Creates, reads, updates, deletes, and searches durable agent memories. Memories are scope-aware
    /// (tenant-wide vs user-specific) exactly like other configuration entities: a caller sees tenant-wide
    /// memories in their tenant plus their own user-specific ones, and may edit/delete only what
    /// <see cref="ScopedVisibility"/> permits. Search and paging are applied in-memory over the caller-visible
    /// set, which is appropriate for the modest per-user volume of distilled memories.
    /// </summary>
    public class MemoryService
    {
        #region Private-Members

        private readonly string _Header = "[MemoryService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        public MemoryService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Enumerate memories visible to the caller with optional filters, ordered newest first, paged.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="query">Paging + date + vessel filters.</param>
        /// <param name="search">Optional case-insensitive substring matched across content, topic, and tags.</param>
        /// <param name="type">Optional memory-type filter.</param>
        /// <param name="topic">Optional exact topic filter (case-insensitive).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A paged result of memories.</returns>
        public async Task<EnumerationResult<Memory>> EnumerateAsync(
            AuthContext auth,
            EnumerationQuery query,
            string? search = null,
            MemoryTypeEnum? type = null,
            string? topic = null,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (query == null) query = new EnumerationQuery();

            Stopwatch sw = Stopwatch.StartNew();
            List<Memory> visible = await LoadVisibleAsync(auth, token).ConfigureAwait(false);

            IEnumerable<Memory> filtered = visible;
            if (type.HasValue) filtered = filtered.Where(m => m.Type == type.Value);
            if (!String.IsNullOrWhiteSpace(topic)) filtered = filtered.Where(m => String.Equals(m.Topic, topic, StringComparison.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(query.VesselId)) filtered = filtered.Where(m => String.Equals(m.VesselId, query.VesselId, StringComparison.Ordinal) || String.Equals(m.SourceVesselId, query.VesselId, StringComparison.Ordinal));
            if (query.CreatedAfter.HasValue) filtered = filtered.Where(m => m.CreatedUtc > query.CreatedAfter.Value);
            if (query.CreatedBefore.HasValue) filtered = filtered.Where(m => m.CreatedUtc < query.CreatedBefore.Value);
            if (!String.IsNullOrWhiteSpace(query.VoyageId)) filtered = filtered.Where(m => String.Equals(m.SourceVoyageId, query.VoyageId, StringComparison.Ordinal));
            if (!String.IsNullOrWhiteSpace(query.MissionId)) filtered = filtered.Where(m => String.Equals(m.SourceMissionId, query.MissionId, StringComparison.Ordinal));

            if (!String.IsNullOrWhiteSpace(search))
            {
                string needle = search.Trim();
                filtered = filtered.Where(m => Contains(m.Content, needle) || Contains(m.Topic, needle) || m.Tags.Any(t => Contains(t, needle)));
            }

            List<Memory> ordered = filtered
                .OrderByDescending(m => m.Salience)
                .ThenByDescending(m => m.CreatedUtc)
                .ToList();

            long total = ordered.Count;
            List<Memory> page = ordered.Skip(query.Offset).Take(query.PageSize).ToList();

            EnumerationResult<Memory> result = EnumerationResult<Memory>.Create(query, page, total);
            sw.Stop();
            result.TotalMs = sw.Elapsed.TotalMilliseconds;
            return result;
        }

        /// <summary>
        /// Read a single memory within the caller scope, or null when not found or not visible.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="id">Memory identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The memory or null.</returns>
        public async Task<Memory?> ReadAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Memory? memory = await _Database.Memories.ReadAsync(id, token).ConfigureAwait(false);
            if (memory == null) return null;
            if (!ScopedVisibility.CanView(auth, memory.Scope, memory.TenantId, memory.UserId)) return null;
            return memory;
        }

        /// <summary>
        /// Create a memory owned by the caller's tenant and user. Regular users create user-specific memories;
        /// admins may create tenant-wide.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="memory">Memory to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created memory.</returns>
        public async Task<Memory> CreateAsync(AuthContext auth, Memory memory, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            if (String.IsNullOrWhiteSpace(memory.Content)) throw new ArgumentException("Memory content is required.");

            memory.TenantId = auth.TenantId;
            memory.UserId = auth.UserId;
            memory.Scope = ScopedVisibility.ResolveCreateScope(auth, memory.Scope);
            memory.Version = 1;
            memory.CreatedUtc = DateTime.UtcNow;
            memory.LastUpdateUtc = DateTime.UtcNow;

            _Logging.Info(_Header + "creating " + memory.Type + " memory " + memory.Id + (String.IsNullOrEmpty(memory.Topic) ? "" : " [" + memory.Topic + "]"));
            return await _Database.Memories.CreateAsync(memory, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a memory, or update it in place when one with the same <see cref="Memory.Key"/> already
        /// exists in the caller's tenant. This is the Recorder's consolidation primitive: re-recording an idea
        /// under a stable key augments the existing memory instead of accumulating a duplicate. When the key is
        /// empty, this is equivalent to <see cref="CreateAsync"/>.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="memory">Memory to upsert (its Key selects the record when set).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created or updated memory.</returns>
        public async Task<Memory> UpsertAsync(AuthContext auth, Memory memory, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (memory == null) throw new ArgumentNullException(nameof(memory));

            if (String.IsNullOrWhiteSpace(memory.Key))
                return await CreateAsync(auth, memory, token).ConfigureAwait(false);

            Memory? existing = await _Database.Memories.ReadByKeyAsync(auth.TenantId ?? String.Empty, memory.Key.Trim(), token).ConfigureAwait(false);
            if (existing == null || !ScopedVisibility.CanEdit(auth, existing.Scope, existing.TenantId, existing.UserId))
                return await CreateAsync(auth, memory, token).ConfigureAwait(false);

            memory.Id = existing.Id;
            return await UpdateAsync(auth, memory, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Update a memory's editable fields. The caller must be permitted to edit it.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="memory">Memory carrying updated fields (its Id selects the record).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated memory.</returns>
        public async Task<Memory> UpdateAsync(AuthContext auth, Memory memory, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (memory == null) throw new ArgumentNullException(nameof(memory));

            Memory? existing = await _Database.Memories.ReadAsync(memory.Id, token).ConfigureAwait(false);
            if (existing == null) throw new KeyNotFoundException("Memory not found: " + memory.Id);
            if (!ScopedVisibility.CanEdit(auth, existing.Scope, existing.TenantId, existing.UserId)) throw new UnauthorizedAccessException("Not permitted to modify memory " + memory.Id);

            // Preserve ownership; only an admin may change scope.
            existing.Type = memory.Type;
            existing.Topic = memory.Topic;
            existing.Key = memory.Key;
            existing.Summary = memory.Summary;
            existing.Content = memory.Content;
            existing.Salience = memory.Salience;
            existing.SourceKind = memory.SourceKind;
            existing.SourceVoyageId = memory.SourceVoyageId;
            existing.SourceMissionId = memory.SourceMissionId;
            existing.SourceVesselId = memory.SourceVesselId;
            existing.SourceDetail = memory.SourceDetail;
            existing.VesselId = memory.VesselId;
            existing.Tags = memory.Tags;
            if (auth.IsAdmin || auth.IsTenantAdmin) existing.Scope = memory.Scope;
            existing.Version = existing.Version + 1;
            existing.LastUpdateUtc = DateTime.UtcNow;

            _Logging.Info(_Header + "updating memory " + existing.Id);
            return await _Database.Memories.UpdateAsync(existing, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete a memory the caller is permitted to delete (e.g. a stale or superseded memory).
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="id">Memory identifier.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task DeleteAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Memory? existing = await _Database.Memories.ReadAsync(id, token).ConfigureAwait(false);
            if (existing == null) throw new KeyNotFoundException("Memory not found: " + id);
            if (!ScopedVisibility.CanEdit(auth, existing.Scope, existing.TenantId, existing.UserId)) throw new UnauthorizedAccessException("Not permitted to delete memory " + id);

            _Logging.Info(_Header + "deleting memory " + id);
            await _Database.Memories.DeleteAsync(id, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<List<Memory>> LoadVisibleAsync(AuthContext auth, CancellationToken token)
        {
            if (auth.IsAdmin || String.IsNullOrEmpty(auth.TenantId))
                return await _Database.Memories.EnumerateAsync(token).ConfigureAwait(false);

            List<Memory> tenantMemories = await _Database.Memories.EnumerateAsync(auth.TenantId, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin) return tenantMemories;

            List<Memory> visible = new List<Memory>();
            foreach (Memory memory in tenantMemories)
            {
                if (ScopedVisibility.CanView(auth, memory.Scope, memory.TenantId, memory.UserId)) visible.Add(memory);
            }

            return visible;
        }

        private static bool Contains(string? haystack, string needle)
        {
            return !String.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion
    }
}
