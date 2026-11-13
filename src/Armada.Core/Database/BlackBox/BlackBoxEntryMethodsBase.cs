namespace Armada.Core.Database.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Black box entry persistence shared by the four providers. SQL is provider-neutral except what
    /// <see cref="BlackBoxSqlDialect"/> supplies (paging, timestamps, boolean literals). Provider classes subclass it.
    /// Thread-safe.
    /// </summary>
    public abstract class BlackBoxEntryMethodsBase : IBlackBoxEntryMethods
    {
        #region Private-Members

        private static readonly SemaphoreSlim _CurrentLock = new SemaphoreSlim(1, 1);

        private static readonly string[] _Columns = new string[]
        {
            "id", "tenant_id", "user_id", "subject_type", "mission_id", "voyage_id", "vessel_id", "fleet_id", "captain_id",
            "harbor_id", "dock_id", "merge_entry_id", "incident_id", "stage_outcome_id", "parent_mission_id", "mission_title",
            "vessel_name", "attempt", "is_current", "supersedes_id", "phase", "step", "failure_kind", "status_before",
            "status_after", "triage", "fingerprint", "capture_mode", "state", "resolution", "resolution_note", "resolution_link",
            "pinned", "storage_target", "storage_prefix", "total_bytes", "object_count", "schema_version", "rules_version",
            "armada_version", "armada_commit", "reason_excerpt", "summary", "enrichment_attempts", "last_error",
            "next_attempt_utc", "expires_utc", "created_utc", "enriched_utc", "analyzed_utc", "resolved_utc", "last_update_utc"
        };

        // Columns UpdateAsync writes. Operator-owned state (pin, resolution, resolved time, expiry) and attempt chaining
        // (is_current, supersedes_id) have dedicated updates, so a stale full-row update from enrichment cannot undo them.
        private static readonly string[] _MutableColumns = new string[]
        {
            "user_id", "vessel_id", "fleet_id", "captain_id", "harbor_id", "dock_id", "merge_entry_id", "incident_id",
            "stage_outcome_id", "parent_mission_id", "mission_title", "vessel_name", "attempt",
            "phase", "step", "failure_kind", "status_before", "status_after", "triage", "fingerprint", "capture_mode", "state",
            "storage_target", "storage_prefix", "total_bytes",
            "object_count", "schema_version", "rules_version", "armada_version", "armada_commit", "reason_excerpt", "summary",
            "enrichment_attempts", "last_error", "next_attempt_utc", "enriched_utc", "analyzed_utc",
            "last_update_utc"
        };

        private readonly BlackBoxSqlDialect _Sql;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="sql">Provider dialect.</param>
        /// <exception cref="ArgumentNullException">Thrown when sql is null.</exception>
        protected BlackBoxEntryMethodsBase(BlackBoxSqlDialect sql)
        {
            _Sql = sql ?? throw new ArgumentNullException(nameof(sql));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<BlackBoxEntry> CreateAsync(BlackBoxEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            entry.LastUpdateUtc = DateTime.UtcNow;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                await InsertAsync(conn, tx, entry, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return entry;
        }

        /// <inheritdoc />
        public async Task<BlackBoxEntry> CreateCurrentAsync(BlackBoxEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            bool isVoyage = entry.SubjectType == BlackBoxSubjectTypeEnum.Voyage;
            string? subjectId = isVoyage ? entry.VoyageId : entry.MissionId;
            if (String.IsNullOrEmpty(subjectId)) throw new ArgumentException("A black box entry needs a " + (isVoyage ? "voyage" : "mission") + " id to become current.", nameof(entry));
            string subjectColumn = isVoyage ? "voyage_id" : "mission_id";
            string subjectWhere = " WHERE subject_type = @subject_type AND " + subjectColumn + " = @subject_id";

            await _CurrentLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                entry.LastUpdateUtc = DateTime.UtcNow;
                await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
                {
                    Action<DbCommand> bindSubject = cmd =>
                    {
                        _Sql.Add(cmd, "@subject_type", entry.SubjectType.ToString());
                        _Sql.Add(cmd, "@subject_id", subjectId);
                    };
                    long maxAttempt = await _Sql.ScalarLongAsync(conn, tx, "SELECT MAX(attempt) FROM blackbox_entries" + subjectWhere + ";", bindSubject, token).ConfigureAwait(false);
                    List<string> current = await _Sql.QueryAsync(conn, tx,
                        "SELECT id FROM blackbox_entries" + subjectWhere + " AND is_current = " + _Sql.True + " ORDER BY created_utc DESC, id DESC;",
                        bindSubject, r => r["id"].ToString()!, token).ConfigureAwait(false);
                    entry.Attempt = (int)maxAttempt + 1;
                    entry.IsCurrent = true;
                    entry.SupersedesId = current.FirstOrDefault();
                    if (current.Count > 0)
                    {
                        await _Sql.ExecuteAsync(conn, tx,
                            "UPDATE blackbox_entries SET is_current = " + _Sql.False + ", last_update_utc = @now" + subjectWhere + " AND is_current = " + _Sql.True + ";",
                            cmd => { bindSubject(cmd); _Sql.AddDate(cmd, "@now", DateTime.UtcNow); }, token).ConfigureAwait(false);
                    }
                    await InsertAsync(conn, tx, entry, token).ConfigureAwait(false);
                }, token).ConfigureAwait(false);
            }
            finally
            {
                _CurrentLock.Release();
            }
            return entry;
        }

        /// <inheritdoc />
        public async Task<BlackBoxEntry?> ReadAsync(string id, string? tenantId = null, bool includeDeleting = false, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            string sql = "SELECT * FROM blackbox_entries WHERE id = @id"
                + (tenantId != null ? " AND tenant_id = @tenant_id" : String.Empty)
                + (includeDeleting ? String.Empty : " AND state <> 'Deleting'") + ";";
            List<BlackBoxEntry> rows = await _Sql.QueryAsync(sql, cmd =>
            {
                _Sql.Add(cmd, "@id", id);
                if (tenantId != null) _Sql.Add(cmd, "@tenant_id", tenantId);
            }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public Task<BlackBoxEntry?> ReadCurrentByMissionAsync(string missionId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            return ReadCurrentAsync("Mission", "mission_id", missionId, tenantId, token);
        }

        /// <inheritdoc />
        public Task<BlackBoxEntry?> ReadCurrentByVoyageAsync(string voyageId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(voyageId)) throw new ArgumentNullException(nameof(voyageId));
            return ReadCurrentAsync("Voyage", "voyage_id", voyageId, tenantId, token);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<BlackBoxEntry>> EnumerateAsync(BlackBoxEnumerateRequest query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<string> conditions = new List<string>();
            Action<DbCommand> bind = BuildFilter(query, conditions);
            string where = " WHERE " + String.Join(" AND ", conditions);
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";
            return await PageAsync("SELECT COUNT(*) FROM blackbox_entries e" + where,
                "SELECT e.* FROM blackbox_entries e" + where + " ORDER BY e.created_utc " + direction + ", e.id " + direction,
                bind, query.PageNumber, query.PageSize, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxEntry>> EnumerateByMissionAsync(string missionId, string? tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            BlackBoxEnumerateRequest request = CopyPage(query);
            request.MissionId = missionId;
            request.TenantId = tenantId;
            request.SubjectType = BlackBoxSubjectTypeEnum.Mission;
            return EnumerateAsync(request, token);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxEntry>> EnumerateByFingerprintAsync(string fingerprint, string? tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(fingerprint)) throw new ArgumentNullException(nameof(fingerprint));
            if (query == null) throw new ArgumentNullException(nameof(query));
            BlackBoxEnumerateRequest request = CopyPage(query);
            request.Fingerprint = fingerprint;
            request.TenantId = tenantId;
            request.VesselId = query.VesselId;
            return EnumerateAsync(request, token);
        }

        /// <inheritdoc />
        public async Task<long> CountAsync(BlackBoxEnumerateRequest query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<string> conditions = new List<string>();
            Action<DbCommand> bind = BuildFilter(query, conditions);
            return await _Sql.ScalarLongAsync("SELECT COUNT(*) FROM blackbox_entries e WHERE " + String.Join(" AND ", conditions) + ";", bind, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<BlackBoxEntry> UpdateAsync(BlackBoxEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            entry.LastUpdateUtc = DateTime.UtcNow;
            string sql = "UPDATE blackbox_entries SET " + String.Join(", ", _MutableColumns.Select(c => c + " = @" + c)) + " WHERE id = @id;";
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                await _Sql.ExecuteAsync(conn, tx, sql, cmd => Bind(cmd, entry), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return entry;
        }

        /// <inheritdoc />
        public Task<bool> MarkSupersededAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            return ExecuteOneAsync("UPDATE blackbox_entries SET is_current = " + _Sql.False + ", last_update_utc = @now WHERE id = @id;",
                cmd => _Sql.Add(cmd, "@id", id), token);
        }

        /// <inheritdoc />
        public Task<bool> UpdateStateAsync(string id, BlackBoxStateEnum state, string? lastError = null, int? enrichmentAttempts = null, DateTime? nextAttemptUtc = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            StringBuilder sql = new StringBuilder("UPDATE blackbox_entries SET state = @state, last_error = @last_error, next_attempt_utc = @next_attempt_utc, last_update_utc = @now");
            if (enrichmentAttempts.HasValue) sql.Append(", enrichment_attempts = @enrichment_attempts");
            if (state == BlackBoxStateEnum.Enriched) sql.Append(", enriched_utc = @now");
            if (state == BlackBoxStateEnum.Analyzed) sql.Append(", analyzed_utc = @now");
            sql.Append(" WHERE id = @id;");
            return ExecuteOneAsync(sql.ToString(), cmd =>
            {
                _Sql.Add(cmd, "@id", id);
                _Sql.Add(cmd, "@state", state.ToString());
                _Sql.Add(cmd, "@last_error", BlackBoxLimits.Clip(lastError, BlackBoxLimits.MaxShortTextChars));
                _Sql.AddDate(cmd, "@next_attempt_utc", nextAttemptUtc);
                if (enrichmentAttempts.HasValue) _Sql.Add(cmd, "@enrichment_attempts", enrichmentAttempts.Value);
            }, token);
        }

        /// <inheritdoc />
        public Task<bool> UpdateResolutionAsync(string id, BlackBoxResolutionEnum resolution, string? note, string? link, DateTime? resolvedUtc, DateTime? expiresUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            return ExecuteOneAsync("UPDATE blackbox_entries SET resolution = @resolution, resolution_note = @note, resolution_link = @link, resolved_utc = @resolved_utc, expires_utc = @expires_utc, last_update_utc = @now WHERE id = @id;", cmd =>
            {
                _Sql.Add(cmd, "@id", id);
                _Sql.Add(cmd, "@resolution", resolution.ToString());
                _Sql.Add(cmd, "@note", BlackBoxLimits.Clip(note, BlackBoxLimits.MaxResolutionNoteChars));
                _Sql.Add(cmd, "@link", BlackBoxLimits.Clip(link, BlackBoxLimits.MaxResolutionLinkChars));
                _Sql.AddDate(cmd, "@resolved_utc", resolvedUtc);
                _Sql.AddDate(cmd, "@expires_utc", expiresUtc);
            }, token);
        }

        /// <inheritdoc />
        public Task<bool> UpdatePinnedAsync(string id, bool pinned, DateTime? expiresUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            return ExecuteOneAsync("UPDATE blackbox_entries SET pinned = @pinned, expires_utc = @expires_utc, last_update_utc = @now WHERE id = @id;", cmd =>
            {
                _Sql.Add(cmd, "@id", id);
                _Sql.Add(cmd, "@pinned", pinned);
                _Sql.AddDate(cmd, "@expires_utc", expiresUtc);
            }, token);
        }

        /// <inheritdoc />
        public Task<bool> UpdateExpiresAsync(string id, DateTime? expiresUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            return ExecuteOneAsync("UPDATE blackbox_entries SET expires_utc = @expires_utc, last_update_utc = @now WHERE id = @id;", cmd =>
            {
                _Sql.Add(cmd, "@id", id);
                _Sql.AddDate(cmd, "@expires_utc", expiresUtc);
            }, token);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteCascadeAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int deleted = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                Action<DbCommand> bind = cmd => _Sql.Add(cmd, "@id", id);
                await _Sql.ExecuteAsync(conn, tx, "DELETE FROM blackbox_objects WHERE entry_id = @id;", bind, token).ConfigureAwait(false);
                await _Sql.ExecuteAsync(conn, tx, "DELETE FROM blackbox_files WHERE entry_id = @id;", bind, token).ConfigureAwait(false);
                await _Sql.ExecuteAsync(conn, tx, "DELETE FROM blackbox_links WHERE entry_id = @id;", bind, token).ConfigureAwait(false);
                await _Sql.ExecuteAsync(conn, tx, "DELETE FROM blackbox_notes WHERE entry_id = @id;", bind, token).ConfigureAwait(false);
                await _Sql.ExecuteAsync(conn, tx, "UPDATE missions SET last_blackbox_entry_id = NULL WHERE last_blackbox_entry_id = @id;", bind, token).ConfigureAwait(false);
                await _Sql.ExecuteAsync(conn, tx, "UPDATE voyages SET last_blackbox_entry_id = NULL WHERE last_blackbox_entry_id = @id;", bind, token).ConfigureAwait(false);
                await _Sql.ExecuteAsync(conn, tx, "UPDATE blackbox_entries SET supersedes_id = NULL WHERE supersedes_id = @id;", bind, token).ConfigureAwait(false);
                deleted = await _Sql.ExecuteAsync(conn, tx, "DELETE FROM blackbox_entries WHERE id = @id;", bind, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted > 0;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<BlackBoxEntry>> EnumerateExpiredAsync(DateTime nowUtc, bool keepLatestPerMission, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            string where = " WHERE e.expires_utc IS NOT NULL AND e.expires_utc < @now_utc AND e.pinned = " + _Sql.False + " AND e.state <> 'Deleting'";
            if (keepLatestPerMission) where += KeepLatestClause();
            Action<DbCommand> bind = cmd => _Sql.AddDate(cmd, "@now_utc", nowUtc);
            return await PageAsync("SELECT COUNT(*) FROM blackbox_entries e" + where,
                "SELECT e.* FROM blackbox_entries e" + where + " ORDER BY e.expires_utc ASC, e.id ASC",
                bind, query.PageNumber, query.PageSize, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<BlackBoxEntry>> EnumerateEvictionCandidatesAsync(BlackBoxEvictionQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            string where = " WHERE e.pinned = " + _Sql.False + " AND e.state <> 'Deleting'";
            if (query.VesselId != null) where += " AND e.vessel_id = @vessel_id";
            if (query.ResolvedOnly) where += " AND e.resolution NOT IN ('Open', 'Unknown')";
            if (query.ExcludeCurrent) where += " AND e.is_current = " + _Sql.False;
            else if (query.KeepLatestPerMission) where += KeepLatestClause();
            Action<DbCommand> bind = cmd => { if (query.VesselId != null) _Sql.Add(cmd, "@vessel_id", query.VesselId); };
            return await PageAsync("SELECT COUNT(*) FROM blackbox_entries e" + where,
                "SELECT e.* FROM blackbox_entries e" + where + " ORDER BY e.created_utc ASC, e.id ASC",
                bind, query.PageNumber, query.PageSize, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<string>> EnumerateVesselsOverQuotaAsync(int maxEntries, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            string grouped = "SELECT e.vessel_id AS vessel_id, COUNT(*) AS entry_count FROM blackbox_entries e WHERE e.vessel_id IS NOT NULL AND e.state <> 'Deleting' GROUP BY e.vessel_id HAVING COUNT(*) > @max_entries";
            Action<DbCommand> bind = cmd => _Sql.Add(cmd, "@max_entries", Math.Max(0, maxEntries));
            BlackBoxSqlDialect.NormalizePage(query.PageNumber, query.PageSize, out int number, out int size, out int offset);
            using (DbConnection conn = _Sql.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await _Sql.ScalarLongAsync(conn, null, "SELECT COUNT(*) FROM (" + grouped + ") t;", bind, token).ConfigureAwait(false);
                List<string> rows = await _Sql.QueryAsync(conn, null, grouped + " ORDER BY COUNT(*) DESC, e.vessel_id ASC" + _Sql.Page(offset, size) + ";",
                    bind, r => r["vessel_id"].ToString()!, token).ConfigureAwait(false);
                return Result(number, size, total, rows);
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<BlackBoxEntry>> EnumerateCascadeCandidatesAsync(BlackBoxCascadeReasonEnum reason, string? subjectId, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            string where;
            switch (reason)
            {
                case BlackBoxCascadeReasonEnum.MissionDeleted:
                    where = " WHERE e.subject_type = 'Mission' AND e.mission_id IS NOT NULL AND e.pinned = " + _Sql.False
                        + " AND NOT EXISTS (SELECT 1 FROM missions m WHERE m.id = e.mission_id)" + (subjectId != null ? " AND e.mission_id = @subject_id" : String.Empty);
                    break;
                case BlackBoxCascadeReasonEnum.VesselDeleted:
                    where = " WHERE e.vessel_id IS NOT NULL AND e.pinned = " + _Sql.False
                        + " AND NOT EXISTS (SELECT 1 FROM vessels v WHERE v.id = e.vessel_id)" + (subjectId != null ? " AND e.vessel_id = @subject_id" : String.Empty);
                    break;
                case BlackBoxCascadeReasonEnum.TenantDeleted:
                    if (String.IsNullOrEmpty(subjectId)) throw new ArgumentNullException(nameof(subjectId));
                    where = " WHERE e.tenant_id = @subject_id";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(reason), "Unknown cascade " + reason + ".");
            }
            where += " AND e.state <> 'Deleting'";
            Action<DbCommand> bind = cmd => { if (subjectId != null) _Sql.Add(cmd, "@subject_id", subjectId); };
            return await PageAsync("SELECT COUNT(*) FROM blackbox_entries e" + where,
                "SELECT e.* FROM blackbox_entries e" + where + " ORDER BY e.created_utc ASC, e.id ASC",
                bind, query.PageNumber, query.PageSize, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<BlackBoxEntry>> ScanAsync(BlackBoxScanQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            string where = " WHERE e.state <> 'Deleting'";
            if (query.AfterId != null) where += " AND e.id > @after_id";
            if (query.StorageTarget.HasValue) where += query.ExcludeStorageTarget ? " AND e.storage_target <> @storage_target" : " AND e.storage_target = @storage_target";
            if (query.VesselId != null) where += " AND e.vessel_id = @vessel_id";
            if (query.NeedsMigrationTo.HasValue)
            {
                where += query.NeedsMigrationTo.Value == BlackBoxStorageTargetEnum.Database
                    ? " AND e.storage_target <> @migrate_to"
                    : " AND (e.storage_target <> @migrate_to OR EXISTS (SELECT 1 FROM blackbox_objects o WHERE o.entry_id = e.id AND o.storage_key IS NULL AND o.missing = " + _Sql.False + "))";
            }
            Action<DbCommand> bind = cmd =>
            {
                if (query.NeedsMigrationTo.HasValue) _Sql.Add(cmd, "@migrate_to", query.NeedsMigrationTo.Value.ToString());
                if (query.AfterId != null) _Sql.Add(cmd, "@after_id", query.AfterId);
                if (query.StorageTarget.HasValue) _Sql.Add(cmd, "@storage_target", query.StorageTarget.Value.ToString());
                if (query.VesselId != null) _Sql.Add(cmd, "@vessel_id", query.VesselId);
            };
            return await PageAsync("SELECT COUNT(*) FROM blackbox_entries e" + where,
                "SELECT e.* FROM blackbox_entries e" + where + " ORDER BY e.id ASC",
                bind, 1, query.PageSize, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<bool> UpdateStorageAsync(string id, BlackBoxStorageTargetEnum target, string? storagePrefix, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            return ExecuteOneAsync("UPDATE blackbox_entries SET storage_target = @storage_target, storage_prefix = @storage_prefix WHERE id = @id;", cmd =>
            {
                _Sql.Add(cmd, "@id", id);
                _Sql.Add(cmd, "@storage_target", target.ToString());
                _Sql.Add(cmd, "@storage_prefix", storagePrefix);
            }, token);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<BlackBoxEntry>> EnumerateDeletingAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            string where = " WHERE e.state = 'Deleting'";
            return await PageAsync("SELECT COUNT(*) FROM blackbox_entries e" + where,
                "SELECT e.* FROM blackbox_entries e" + where + " ORDER BY e.last_update_utc ASC, e.id ASC",
                null, query.PageNumber, query.PageSize, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<BlackBoxEntry>> EnumerateRetryableAsync(DateTime nowUtc, int maxAttempts, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            string where = " WHERE e.state = 'EnrichmentFailed' AND e.enrichment_attempts < @max_attempts AND (e.next_attempt_utc IS NULL OR e.next_attempt_utc <= @now_utc)";
            Action<DbCommand> bind = cmd =>
            {
                _Sql.Add(cmd, "@max_attempts", maxAttempts);
                _Sql.AddDate(cmd, "@now_utc", nowUtc);
            };
            return await PageAsync("SELECT COUNT(*) FROM blackbox_entries e" + where,
                "SELECT e.* FROM blackbox_entries e" + where + " ORDER BY e.created_utc ASC, e.id ASC",
                bind, query.PageNumber, query.PageSize, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<BlackBoxFingerprintSummary>> EnumerateFingerprintsAsync(BlackBoxFingerprintEnumerateRequest query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<string> conditions = new List<string> { "e.fingerprint IS NOT NULL", "e.state <> 'Deleting'" };
            if (query.TenantId != null) conditions.Add("e.tenant_id = @tenant_id");
            if (query.UserId != null) conditions.Add("e.user_id = @user_id");
            if (query.VesselId != null) conditions.Add("e.vessel_id = @vessel_id");
            if (query.Resolution.HasValue) conditions.Add("e.resolution = @resolution");
            if (query.Triage.HasValue) conditions.Add("e.triage = @triage");
            if (query.ResilienceScope.HasValue) conditions.Add(ResilienceScopeClause(query.ResilienceScope.Value));
            if (query.CreatedAfter.HasValue) conditions.Add("e.created_utc > @created_after");
            if (query.CreatedBefore.HasValue) conditions.Add("e.created_utc < @created_before");
            Action<DbCommand> bind = cmd =>
            {
                if (query.TenantId != null) _Sql.Add(cmd, "@tenant_id", query.TenantId);
                if (query.UserId != null) _Sql.Add(cmd, "@user_id", query.UserId);
                if (query.VesselId != null) _Sql.Add(cmd, "@vessel_id", query.VesselId);
                if (query.Resolution.HasValue) _Sql.Add(cmd, "@resolution", query.Resolution.Value.ToString());
                if (query.Triage.HasValue) _Sql.Add(cmd, "@triage", query.Triage.Value.ToString());
                if (query.CreatedAfter.HasValue) _Sql.AddDate(cmd, "@created_after", query.CreatedAfter.Value);
                if (query.CreatedBefore.HasValue) _Sql.AddDate(cmd, "@created_before", query.CreatedBefore.Value);
            };
            string where = " WHERE " + String.Join(" AND ", conditions);
            BlackBoxSqlDialect.NormalizePage(query.PageNumber, query.PageSize, out int number, out int size, out int offset);
            using (DbConnection conn = _Sql.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await _Sql.ScalarLongAsync(conn, null, "SELECT COUNT(*) FROM (SELECT e.fingerprint FROM blackbox_entries e" + where + " GROUP BY e.fingerprint) t;", bind, token).ConfigureAwait(false);
                List<BlackBoxFingerprintSummary> rows = await _Sql.QueryAsync(conn, null,
                    "SELECT e.fingerprint AS fingerprint, COUNT(*) AS entry_count, SUM(CASE WHEN e.resolution = 'Open' THEN 1 ELSE 0 END) AS open_count, "
                    + "MIN(e.created_utc) AS first_seen, MAX(e.created_utc) AS last_seen FROM blackbox_entries e" + where
                    + " GROUP BY e.fingerprint ORDER BY COUNT(*) DESC, MAX(e.created_utc) DESC, e.fingerprint ASC" + _Sql.Page(offset, size) + ";",
                    bind, r => new BlackBoxFingerprintSummary
                    {
                        Fingerprint = r["fingerprint"].ToString()!,
                        Count = BlackBoxSqlDialect.ReadLong(r["entry_count"], 0),
                        OpenCount = BlackBoxSqlDialect.ReadLong(r["open_count"], 0),
                        FirstSeenUtc = _Sql.ReadDate(r["first_seen"]) ?? DateTime.UtcNow,
                        LastSeenUtc = _Sql.ReadDate(r["last_seen"]) ?? DateTime.UtcNow
                    }, token).ConfigureAwait(false);

                foreach (BlackBoxFingerprintSummary row in rows)
                {
                    List<BlackBoxEntry> latest = await _Sql.QueryAsync(conn, null,
                        "SELECT e.* FROM blackbox_entries e" + where + " AND e.fingerprint = @fp ORDER BY e.created_utc DESC, e.id DESC" + _Sql.Page(0, 1) + ";",
                        cmd => { bind(cmd); _Sql.Add(cmd, "@fp", row.Fingerprint); }, FromReader, token).ConfigureAwait(false);
                    BlackBoxEntry? newest = latest.FirstOrDefault();
                    if (newest == null) continue;
                    row.LatestEntryId = newest.Id;
                    row.Phase = newest.Phase;
                    row.Step = newest.Step;
                    row.FailureKind = newest.FailureKind;
                }
                return Result(number, size, total, rows);
            }
        }

        /// <inheritdoc />
        public async Task<long> SumBytesAsync(string? tenantId = null, string? vesselId = null, CancellationToken token = default)
        {
            List<string> conditions = new List<string> { "1 = 1" };
            if (tenantId != null) conditions.Add("tenant_id = @tenant_id");
            if (vesselId != null) conditions.Add("vessel_id = @vessel_id");
            return await _Sql.ScalarLongAsync("SELECT SUM(total_bytes) FROM blackbox_entries WHERE " + String.Join(" AND ", conditions) + ";", cmd =>
            {
                if (tenantId != null) _Sql.Add(cmd, "@tenant_id", tenantId);
                if (vesselId != null) _Sql.Add(cmd, "@vessel_id", vesselId);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<BlackBoxStorageTargetUsage>> SumBytesByTargetAsync(string? tenantId = null, CancellationToken token = default)
        {
            string where = tenantId != null ? " WHERE tenant_id = @tenant_id" : String.Empty;
            return await _Sql.QueryAsync("SELECT storage_target, COUNT(*) AS entry_count, SUM(total_bytes) AS bytes FROM blackbox_entries" + where + " GROUP BY storage_target ORDER BY storage_target;",
                cmd => { if (tenantId != null) _Sql.Add(cmd, "@tenant_id", tenantId); },
                r => new BlackBoxStorageTargetUsage
                {
                    Target = BlackBoxSqlDialect.ReadEnum(r["storage_target"], BlackBoxStorageTargetEnum.Unknown),
                    Entries = BlackBoxSqlDialect.ReadLong(r["entry_count"], 0),
                    Bytes = BlackBoxSqlDialect.ReadLong(r["bytes"], 0)
                }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<long> CountByVesselAsync(string vesselId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            return await _Sql.ScalarLongAsync("SELECT COUNT(*) FROM blackbox_entries WHERE vessel_id = @vessel_id" + (tenantId != null ? " AND tenant_id = @tenant_id" : String.Empty) + ";", cmd =>
            {
                _Sql.Add(cmd, "@vessel_id", vesselId);
                if (tenantId != null) _Sql.Add(cmd, "@tenant_id", tenantId);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<string>> EnumerateUncapturedMissionIdsAsync(DateTime? updatedSince, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            string where = " WHERE (m.status IN ('Failed', 'LandingFailed', 'Blocked')"
                + " OR (m.status = 'Cancelled' AND m.failure_kind IS NOT NULL AND m.failure_kind <> 'OperatorAction'))"
                + " AND (m.last_blackbox_entry_id IS NULL OR NOT EXISTS (SELECT 1 FROM blackbox_entries e WHERE e.id = m.last_blackbox_entry_id AND ("
                + "e.created_utc >= m.last_update_utc"
                // An edit after the entry is not a new failure: the entry still covers the mission unless it re-entered a
                // failure state (another status, a new attempt started or finished after the entry) or its failure kind
                // or reason changed.
                + " OR (COALESCE(e.status_after, '') = m.status"
                + " AND COALESCE(e.failure_kind, '') = COALESCE(m.failure_kind, '')"
                + " AND (m.started_utc IS NULL OR m.started_utc <= e.created_utc)"
                + " AND (m.completed_utc IS NULL OR m.completed_utc <= e.created_utc)"
                + " AND (m.failure_reason IS NULL OR COALESCE(e.reason_excerpt, '') = " + _Sql.TextPrefix("m.failure_reason", BlackBoxLimits.MaxEntryReasonExcerptChars) + ")))))"
                + (updatedSince.HasValue ? " AND m.last_update_utc >= @since" : String.Empty);
            Action<DbCommand> bind = cmd => { if (updatedSince.HasValue) _Sql.AddDate(cmd, "@since", updatedSince.Value); };
            return await PageAsync("SELECT COUNT(*) FROM missions m" + where,
                "SELECT m.id AS id FROM missions m" + where + " ORDER BY m.last_update_utc ASC, m.id ASC",
                bind, query.PageNumber, query.PageSize, r => r["id"].ToString()!, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<string>> EnumerateUncapturedVoyageIdsAsync(DateTime? updatedSince, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            string where = " WHERE v.status IN ('Failed', 'Cancelled')"
                + " AND NOT EXISTS (SELECT 1 FROM blackbox_entries e WHERE e.voyage_id = v.id AND e.subject_type = 'Voyage')"
                + (updatedSince.HasValue ? " AND v.last_update_utc >= @since" : String.Empty);
            Action<DbCommand> bind = cmd => { if (updatedSince.HasValue) _Sql.AddDate(cmd, "@since", updatedSince.Value); };
            return await PageAsync("SELECT COUNT(*) FROM voyages v" + where,
                "SELECT v.id AS id FROM voyages v" + where + " ORDER BY v.last_update_utc ASC, v.id ASC",
                bind, query.PageNumber, query.PageSize, r => r["id"].ToString()!, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> SetMissionLastEntryAsync(string missionId, string? entryId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            int changed = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                changed = await _Sql.ExecuteAsync(conn, tx, "UPDATE missions SET last_blackbox_entry_id = @entry_id WHERE id = @id;", cmd =>
                {
                    _Sql.Add(cmd, "@id", missionId);
                    _Sql.Add(cmd, "@entry_id", entryId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return changed > 0;
        }

        /// <inheritdoc />
        public async Task<bool> SetVoyageLastEntryAsync(string voyageId, string? entryId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(voyageId)) throw new ArgumentNullException(nameof(voyageId));
            int changed = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                changed = await _Sql.ExecuteAsync(conn, tx, "UPDATE voyages SET last_blackbox_entry_id = @entry_id WHERE id = @id;", cmd =>
                {
                    _Sql.Add(cmd, "@id", voyageId);
                    _Sql.Add(cmd, "@entry_id", entryId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return changed > 0;
        }

        #endregion

        #region Private-Methods

        private async Task<BlackBoxEntry?> ReadCurrentAsync(string subjectType, string column, string subjectId, string? tenantId, CancellationToken token)
        {
            string sql = "SELECT * FROM blackbox_entries WHERE subject_type = @subject_type AND " + column + " = @subject_id AND is_current = " + _Sql.True
                + " AND state <> 'Deleting'" + (tenantId != null ? " AND tenant_id = @tenant_id" : String.Empty)
                + " ORDER BY created_utc DESC, id DESC" + _Sql.Page(0, 1) + ";";
            List<BlackBoxEntry> rows = await _Sql.QueryAsync(sql, cmd =>
            {
                _Sql.Add(cmd, "@subject_type", subjectType);
                _Sql.Add(cmd, "@subject_id", subjectId);
                if (tenantId != null) _Sql.Add(cmd, "@tenant_id", tenantId);
            }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        private Action<DbCommand> BuildFilter(BlackBoxEnumerateRequest q, List<string> conditions)
        {
            conditions.Add("1 = 1");
            if (q.TenantId != null) conditions.Add("e.tenant_id = @tenant_id");
            if (q.UserId != null) conditions.Add("e.user_id = @user_id");
            if (q.SubjectType.HasValue) conditions.Add("e.subject_type = @subject_type");
            if (q.MissionId != null) conditions.Add("e.mission_id = @mission_id");
            if (q.VoyageId != null) conditions.Add("e.voyage_id = @voyage_id");
            if (q.VesselId != null) conditions.Add("e.vessel_id = @vessel_id");
            if (q.FleetId != null) conditions.Add("e.fleet_id = @fleet_id");
            if (q.CaptainId != null) conditions.Add("e.captain_id = @captain_id");
            if (q.HarborId != null) conditions.Add("e.harbor_id = @harbor_id");
            if (q.Phase.HasValue) conditions.Add("e.phase = @phase");
            if (q.Step.HasValue) conditions.Add("e.step = @step");
            if (q.Kind.HasValue) conditions.Add("e.failure_kind = @failure_kind");
            if (q.Triage.HasValue) conditions.Add("e.triage = @triage");
            if (q.Resolution.HasValue) conditions.Add("e.resolution = @resolution");
            if (q.State.HasValue) conditions.Add("e.state = @state");
            else conditions.Add("e.state <> 'Deleting'");
            if (q.CaptureMode.HasValue) conditions.Add("e.capture_mode = @capture_mode");
            if (q.Fingerprint != null) conditions.Add("e.fingerprint = @fingerprint");
            if (q.Pinned.HasValue) conditions.Add("e.pinned = " + (q.Pinned.Value ? _Sql.True : _Sql.False));
            if (q.IsCurrent.HasValue) conditions.Add("e.is_current = " + (q.IsCurrent.Value ? _Sql.True : _Sql.False));
            if (q.Path != null) conditions.Add("EXISTS (SELECT 1 FROM blackbox_files f WHERE f.entry_id = e.id AND f.path = @path)");
            if (q.ResilienceScope.HasValue) conditions.Add(ResilienceScopeClause(q.ResilienceScope.Value));
            if (q.CreatedAfter.HasValue) conditions.Add("e.created_utc > @created_after");
            if (q.CreatedBefore.HasValue) conditions.Add("e.created_utc < @created_before");
            if (!String.IsNullOrEmpty(q.SearchText))
            {
                conditions.Add("(LOWER(e.reason_excerpt) LIKE @search ESCAPE '!' OR LOWER(e.summary) LIKE @search ESCAPE '!' OR LOWER(e.mission_title) LIKE @search ESCAPE '!'"
                    + " OR EXISTS (SELECT 1 FROM blackbox_notes n WHERE n.entry_id = e.id AND LOWER(n.body) LIKE @search ESCAPE '!'))");
            }
            return cmd =>
            {
                if (q.TenantId != null) _Sql.Add(cmd, "@tenant_id", q.TenantId);
                if (q.UserId != null) _Sql.Add(cmd, "@user_id", q.UserId);
                if (q.SubjectType.HasValue) _Sql.Add(cmd, "@subject_type", q.SubjectType.Value.ToString());
                if (q.MissionId != null) _Sql.Add(cmd, "@mission_id", q.MissionId);
                if (q.VoyageId != null) _Sql.Add(cmd, "@voyage_id", q.VoyageId);
                if (q.VesselId != null) _Sql.Add(cmd, "@vessel_id", q.VesselId);
                if (q.FleetId != null) _Sql.Add(cmd, "@fleet_id", q.FleetId);
                if (q.CaptainId != null) _Sql.Add(cmd, "@captain_id", q.CaptainId);
                if (q.HarborId != null) _Sql.Add(cmd, "@harbor_id", q.HarborId);
                if (q.Phase.HasValue) _Sql.Add(cmd, "@phase", q.Phase.Value.ToString());
                if (q.Step.HasValue) _Sql.Add(cmd, "@step", q.Step.Value.ToString());
                if (q.Kind.HasValue) _Sql.Add(cmd, "@failure_kind", q.Kind.Value.ToString());
                if (q.Triage.HasValue) _Sql.Add(cmd, "@triage", q.Triage.Value.ToString());
                if (q.Resolution.HasValue) _Sql.Add(cmd, "@resolution", q.Resolution.Value.ToString());
                if (q.State.HasValue) _Sql.Add(cmd, "@state", q.State.Value.ToString());
                if (q.CaptureMode.HasValue) _Sql.Add(cmd, "@capture_mode", q.CaptureMode.Value.ToString());
                if (q.Fingerprint != null) _Sql.Add(cmd, "@fingerprint", q.Fingerprint);
                if (q.Path != null) _Sql.Add(cmd, "@path", q.Path);
                if (q.CreatedAfter.HasValue) _Sql.AddDate(cmd, "@created_after", q.CreatedAfter.Value);
                if (q.CreatedBefore.HasValue) _Sql.AddDate(cmd, "@created_before", q.CreatedBefore.Value);
                if (!String.IsNullOrEmpty(q.SearchText)) _Sql.Add(cmd, "@search", "%" + BlackBoxSqlDialect.EscapeLike(q.SearchText!.ToLowerInvariant()) + "%");
            };
        }

        private static string ResilienceScopeClause(bool inScope)
        {
            // The index-level handoff and landing scope (BlackBoxResilienceScope.IndexInScope): enum names only, no input.
            List<string> phases = BlackBoxResilienceScope.Phases.Select(p => "'" + p.ToString() + "'").ToList();
            List<string> steps = BlackBoxResilienceScope.Phases.SelectMany(BlackBoxTaxonomy.StepsOf).Concat(BlackBoxResilienceScope.Steps)
                .Distinct().Select(st => "'" + st.ToString() + "'").ToList();
            List<string> kinds = BlackBoxResilienceScope.Kinds.Select(k => "'" + k.ToString() + "'").ToList();
            string clause = "(e.phase IN (" + String.Join(", ", phases) + ") OR e.step IN (" + String.Join(", ", steps) + ") OR COALESCE(e.failure_kind, '') IN (" + String.Join(", ", kinds) + "))";
            return inScope ? clause : "NOT " + clause;
        }

        private string KeepLatestClause()
        {
            return " AND NOT (e.is_current = " + _Sql.True + " AND ("
                + "(e.subject_type = 'Mission' AND EXISTS (SELECT 1 FROM missions m WHERE m.id = e.mission_id))"
                + " OR (e.subject_type = 'Voyage' AND EXISTS (SELECT 1 FROM voyages v WHERE v.id = e.voyage_id))))";
        }

        private async Task<EnumerationResult<T>> PageAsync<T>(string countSql, string selectSql, Action<DbCommand>? bind, int pageNumber, int pageSize, Func<DbDataReader, T> map, CancellationToken token)
        {
            BlackBoxSqlDialect.NormalizePage(pageNumber, pageSize, out int number, out int size, out int offset);
            using (DbConnection conn = _Sql.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await _Sql.ScalarLongAsync(conn, null, countSql + ";", bind, token).ConfigureAwait(false);
                List<T> rows = await _Sql.QueryAsync(conn, null, selectSql + _Sql.Page(offset, size) + ";", bind, map, token).ConfigureAwait(false);
                return Result(number, size, total, rows);
            }
        }

        private static EnumerationResult<T> Result<T>(int number, int size, long total, List<T> rows)
        {
            EnumerationResult<T> result = new EnumerationResult<T>();
            result.PageNumber = number;
            result.PageSize = size;
            result.TotalRecords = total;
            result.TotalPages = (int)Math.Ceiling((double)total / size);
            result.Objects = rows;
            return result;
        }

        private static BlackBoxEnumerateRequest CopyPage(EnumerationQuery query)
        {
            BlackBoxEnumerateRequest request = new BlackBoxEnumerateRequest();
            request.PageNumber = query.PageNumber;
            request.PageSize = query.PageSize;
            request.Order = query.Order;
            request.CreatedAfter = query.CreatedAfter;
            request.CreatedBefore = query.CreatedBefore;
            return request;
        }

        private async Task<bool> ExecuteOneAsync(string sql, Action<DbCommand> bind, CancellationToken token)
        {
            int changed = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                changed = await _Sql.ExecuteAsync(conn, tx, sql, cmd => { bind(cmd); _Sql.AddDate(cmd, "@now", DateTime.UtcNow); }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return changed > 0;
        }

        private async Task InsertAsync(DbConnection conn, DbTransaction tx, BlackBoxEntry entry, CancellationToken token)
        {
            string sql = "INSERT INTO blackbox_entries (" + String.Join(", ", _Columns) + ") VALUES (" + String.Join(", ", _Columns.Select(c => "@" + c)) + ");";
            await _Sql.ExecuteAsync(conn, tx, sql, cmd =>
            {
                Bind(cmd, entry);
                _Sql.Add(cmd, "@tenant_id", entry.TenantId);
                _Sql.Add(cmd, "@subject_type", entry.SubjectType.ToString());
                _Sql.Add(cmd, "@mission_id", entry.MissionId);
                _Sql.Add(cmd, "@voyage_id", entry.VoyageId);
                _Sql.AddDate(cmd, "@created_utc", entry.CreatedUtc);
            }, token).ConfigureAwait(false);
        }

        private void Bind(DbCommand cmd, BlackBoxEntry e)
        {
            _Sql.Add(cmd, "@id", e.Id);
            _Sql.Add(cmd, "@user_id", e.UserId);
            _Sql.Add(cmd, "@vessel_id", e.VesselId);
            _Sql.Add(cmd, "@fleet_id", e.FleetId);
            _Sql.Add(cmd, "@captain_id", e.CaptainId);
            _Sql.Add(cmd, "@harbor_id", e.HarborId);
            _Sql.Add(cmd, "@dock_id", e.DockId);
            _Sql.Add(cmd, "@merge_entry_id", e.MergeEntryId);
            _Sql.Add(cmd, "@incident_id", e.IncidentId);
            _Sql.Add(cmd, "@stage_outcome_id", e.StageOutcomeId);
            _Sql.Add(cmd, "@parent_mission_id", e.ParentMissionId);
            _Sql.Add(cmd, "@mission_title", BlackBoxLimits.Clip(e.MissionTitle, BlackBoxLimits.MaxTitleChars));
            _Sql.Add(cmd, "@vessel_name", BlackBoxLimits.Clip(e.VesselName, BlackBoxLimits.MaxTitleChars));
            _Sql.Add(cmd, "@attempt", e.Attempt);
            _Sql.Add(cmd, "@is_current", e.IsCurrent);
            _Sql.Add(cmd, "@supersedes_id", e.SupersedesId);
            _Sql.Add(cmd, "@phase", e.Phase.ToString());
            _Sql.Add(cmd, "@step", e.Step.ToString());
            _Sql.Add(cmd, "@failure_kind", e.FailureKind.HasValue ? e.FailureKind.Value.ToString() : null);
            _Sql.Add(cmd, "@status_before", e.StatusBefore);
            _Sql.Add(cmd, "@status_after", e.StatusAfter);
            _Sql.Add(cmd, "@triage", e.Triage.ToString());
            _Sql.Add(cmd, "@fingerprint", e.Fingerprint);
            _Sql.Add(cmd, "@capture_mode", e.CaptureMode.ToString());
            _Sql.Add(cmd, "@state", e.State.ToString());
            _Sql.Add(cmd, "@resolution", e.Resolution.ToString());
            _Sql.Add(cmd, "@resolution_note", BlackBoxLimits.Clip(e.ResolutionNote, BlackBoxLimits.MaxResolutionNoteChars));
            _Sql.Add(cmd, "@resolution_link", BlackBoxLimits.Clip(e.ResolutionLink, BlackBoxLimits.MaxResolutionLinkChars));
            _Sql.Add(cmd, "@pinned", e.Pinned);
            _Sql.Add(cmd, "@storage_target", e.StorageTarget.ToString());
            _Sql.Add(cmd, "@storage_prefix", e.StoragePrefix);
            _Sql.Add(cmd, "@total_bytes", e.TotalBytes);
            _Sql.Add(cmd, "@object_count", e.ObjectCount);
            _Sql.Add(cmd, "@schema_version", e.SchemaVersion);
            _Sql.Add(cmd, "@rules_version", e.RulesVersion);
            _Sql.Add(cmd, "@armada_version", e.ArmadaVersion);
            _Sql.Add(cmd, "@armada_commit", e.ArmadaCommit);
            _Sql.Add(cmd, "@reason_excerpt", BlackBoxLimits.Clip(e.ReasonExcerpt, BlackBoxLimits.MaxEntryReasonExcerptChars));
            _Sql.Add(cmd, "@summary", BlackBoxLimits.Clip(e.Summary, BlackBoxLimits.MaxSummaryChars));
            _Sql.Add(cmd, "@enrichment_attempts", e.EnrichmentAttempts);
            _Sql.Add(cmd, "@last_error", BlackBoxLimits.Clip(e.LastError, BlackBoxLimits.MaxShortTextChars));
            _Sql.AddDate(cmd, "@next_attempt_utc", e.NextAttemptUtc);
            _Sql.AddDate(cmd, "@expires_utc", e.ExpiresUtc);
            _Sql.AddDate(cmd, "@enriched_utc", e.EnrichedUtc);
            _Sql.AddDate(cmd, "@analyzed_utc", e.AnalyzedUtc);
            _Sql.AddDate(cmd, "@resolved_utc", e.ResolvedUtc);
            _Sql.AddDate(cmd, "@last_update_utc", e.LastUpdateUtc);
        }

        private BlackBoxEntry FromReader(DbDataReader r)
        {
            BlackBoxEntry e = new BlackBoxEntry();
            e.Id = r["id"].ToString()!;
            e.TenantId = BlackBoxSqlDialect.ReadString(r["tenant_id"]);
            e.UserId = BlackBoxSqlDialect.ReadString(r["user_id"]);
            e.SubjectType = BlackBoxSqlDialect.ReadEnum(r["subject_type"], BlackBoxSubjectTypeEnum.Unknown);
            e.MissionId = BlackBoxSqlDialect.ReadString(r["mission_id"]);
            e.VoyageId = BlackBoxSqlDialect.ReadString(r["voyage_id"]);
            e.VesselId = BlackBoxSqlDialect.ReadString(r["vessel_id"]);
            e.FleetId = BlackBoxSqlDialect.ReadString(r["fleet_id"]);
            e.CaptainId = BlackBoxSqlDialect.ReadString(r["captain_id"]);
            e.HarborId = BlackBoxSqlDialect.ReadString(r["harbor_id"]);
            e.DockId = BlackBoxSqlDialect.ReadString(r["dock_id"]);
            e.MergeEntryId = BlackBoxSqlDialect.ReadString(r["merge_entry_id"]);
            e.IncidentId = BlackBoxSqlDialect.ReadString(r["incident_id"]);
            e.StageOutcomeId = BlackBoxSqlDialect.ReadString(r["stage_outcome_id"]);
            e.ParentMissionId = BlackBoxSqlDialect.ReadString(r["parent_mission_id"]);
            e.MissionTitle = BlackBoxSqlDialect.ReadString(r["mission_title"]);
            e.VesselName = BlackBoxSqlDialect.ReadString(r["vessel_name"]);
            e.Attempt = (int)BlackBoxSqlDialect.ReadLong(r["attempt"], 1);
            e.IsCurrent = BlackBoxSqlDialect.ReadBool(r["is_current"], false);
            e.SupersedesId = BlackBoxSqlDialect.ReadString(r["supersedes_id"]);
            e.Phase = BlackBoxSqlDialect.ReadEnum(r["phase"], BlackBoxPhaseEnum.Unknown);
            e.Step = BlackBoxSqlDialect.ReadEnum(r["step"], BlackBoxStepEnum.Unknown);
            e.FailureKind = BlackBoxSqlDialect.ReadNullableEnum<MissionFailureKindEnum>(r["failure_kind"]);
            e.StatusBefore = BlackBoxSqlDialect.ReadString(r["status_before"]);
            e.StatusAfter = BlackBoxSqlDialect.ReadString(r["status_after"]);
            e.Triage = BlackBoxSqlDialect.ReadEnum(r["triage"], BlackBoxTriageEnum.Unknown);
            e.Fingerprint = BlackBoxSqlDialect.ReadString(r["fingerprint"]);
            e.CaptureMode = BlackBoxSqlDialect.ReadEnum(r["capture_mode"], BlackBoxCaptureModeEnum.Unknown);
            e.State = BlackBoxSqlDialect.ReadEnum(r["state"], BlackBoxStateEnum.Unknown);
            e.Resolution = BlackBoxSqlDialect.ReadEnum(r["resolution"], BlackBoxResolutionEnum.Unknown);
            e.ResolutionNote = BlackBoxSqlDialect.ReadString(r["resolution_note"]);
            e.ResolutionLink = BlackBoxSqlDialect.ReadString(r["resolution_link"]);
            e.Pinned = BlackBoxSqlDialect.ReadBool(r["pinned"], false);
            e.StorageTarget = BlackBoxSqlDialect.ReadEnum(r["storage_target"], BlackBoxStorageTargetEnum.Unknown);
            e.StoragePrefix = BlackBoxSqlDialect.ReadString(r["storage_prefix"]);
            e.TotalBytes = BlackBoxSqlDialect.ReadLong(r["total_bytes"], 0);
            e.ObjectCount = (int)BlackBoxSqlDialect.ReadLong(r["object_count"], 0);
            e.SchemaVersion = (int)BlackBoxSqlDialect.ReadLong(r["schema_version"], BlackBoxLimits.SchemaVersion);
            e.RulesVersion = (int)BlackBoxSqlDialect.ReadLong(r["rules_version"], 0);
            e.ArmadaVersion = BlackBoxSqlDialect.ReadString(r["armada_version"]);
            e.ArmadaCommit = BlackBoxSqlDialect.ReadString(r["armada_commit"]);
            e.ReasonExcerpt = BlackBoxSqlDialect.ReadString(r["reason_excerpt"]);
            e.Summary = BlackBoxSqlDialect.ReadString(r["summary"]);
            e.EnrichmentAttempts = (int)BlackBoxSqlDialect.ReadLong(r["enrichment_attempts"], 0);
            e.LastError = BlackBoxSqlDialect.ReadString(r["last_error"]);
            e.NextAttemptUtc = _Sql.ReadDate(r["next_attempt_utc"]);
            e.ExpiresUtc = _Sql.ReadDate(r["expires_utc"]);
            e.CreatedUtc = _Sql.ReadDate(r["created_utc"]) ?? DateTime.UtcNow;
            e.EnrichedUtc = _Sql.ReadDate(r["enriched_utc"]);
            e.AnalyzedUtc = _Sql.ReadDate(r["analyzed_utc"]);
            e.ResolvedUtc = _Sql.ReadDate(r["resolved_utc"]);
            e.LastUpdateUtc = _Sql.ReadDate(r["last_update_utc"]) ?? DateTime.UtcNow;
            return e;
        }

        #endregion
    }
}
