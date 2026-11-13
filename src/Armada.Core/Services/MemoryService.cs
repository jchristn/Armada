namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Creates, reads, updates, deletes, and searches durable agent memories. Memories are scope-aware
    /// (tenant-wide vs user-specific) exactly like other configuration entities: a caller sees tenant-wide
    /// memories in their tenant plus their own user-specific ones, and may edit/delete only what
    /// <see cref="ScopedVisibility"/> permits. Enumeration applies visibility, filters, sort, and paging in the
    /// database.
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
        /// Enumerate memories visible to the caller with optional filters, paged, in the historical relevance order
        /// (salience descending, then newest first). Kept for callers that pass the memory filters separately.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="query">Paging, date, vessel, voyage, and mission filters.</param>
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

            MemoryEnumerateRequest request = MemoryEnumerateRequest.FromQuery(query);
            if (!String.IsNullOrWhiteSpace(search)) request.Search = search.Trim();
            if (type.HasValue) request.Type = type;
            if (!String.IsNullOrWhiteSpace(topic)) request.Topic = topic.Trim();
            request.ApplyDefaultSort(false);
            return await EnumerateAsync(auth, request, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate one page of the memories visible to the caller. Visibility, filters, sort, and paging are all
        /// applied by the database. A global admin sees every tenant (optionally narrowed by TenantId/UserId); a
        /// tenant admin sees their tenant (optionally narrowed by UserId); a regular user sees tenant-wide memories
        /// in their tenant plus their own user-specific ones (UserId and TenantId are ignored).
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="request">Filters, sort column, order, and paging; null uses defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A paged result of memories.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="auth"/> is null.</exception>
        public async Task<EnumerationResult<Memory>> EnumerateAsync(AuthContext auth, MemoryEnumerateRequest? request, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (request == null) request = new MemoryEnumerateRequest();

            string? tenantId = null;
            string? ownerUserId = null;
            string? viewerUserId = null;

            if (auth.IsAdmin)
            {
                EnumerationScope scope = EnumerationScope.Resolve(auth, request.TenantId, request.UserId);
                if (!scope.All)
                {
                    tenantId = scope.TenantId;
                    ownerUserId = scope.UserId;
                }
            }
            else if (String.IsNullOrEmpty(auth.TenantId))
            {
                // Preserves the historical behavior of the in-memory enumeration for a tenantless caller.
            }
            else if (auth.IsTenantAdmin)
            {
                tenantId = auth.TenantId;
                if (!String.IsNullOrWhiteSpace(request.UserId)) ownerUserId = request.UserId.Trim();
            }
            else
            {
                tenantId = auth.TenantId;
                viewerUserId = auth.UserId ?? String.Empty;
            }

            Stopwatch sw = Stopwatch.StartNew();
            EnumerationResult<Memory> result = await _Database.Memories.EnumerateAsync(request, tenantId, ownerUserId, viewerUserId, token).ConfigureAwait(false);
            sw.Stop();
            result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
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
        /// <exception cref="CollectionLimitExceededException">Thrown when Tags holds more than MaxConfigItems entries.</exception>
        public async Task<Memory> CreateAsync(AuthContext auth, Memory memory, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            if (String.IsNullOrWhiteSpace(memory.Content)) throw new ArgumentException("Memory content is required.");
            CollectionBounds.Validate(memory);

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
        /// <exception cref="CollectionLimitExceededException">Thrown when Tags holds more than MaxConfigItems entries.</exception>
        public async Task<Memory> UpdateAsync(AuthContext auth, Memory memory, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            CollectionBounds.Validate(memory);

            Memory? existing = await _Database.Memories.ReadAsync(memory.Id, token).ConfigureAwait(false);
            if (existing == null || !ScopedVisibility.CanView(auth, existing.Scope, existing.TenantId, existing.UserId)) throw new KeyNotFoundException("Memory not found: " + memory.Id);
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
            if (existing == null || !ScopedVisibility.CanView(auth, existing.Scope, existing.TenantId, existing.UserId)) throw new KeyNotFoundException("Memory not found: " + id);
            if (!ScopedVisibility.CanEdit(auth, existing.Scope, existing.TenantId, existing.UserId)) throw new UnauthorizedAccessException("Not permitted to delete memory " + id);

            _Logging.Info(_Header + "deleting memory " + id);
            await _Database.Memories.DeleteAsync(id, token).ConfigureAwait(false);
        }

        #endregion
    }
}
