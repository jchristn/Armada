namespace Armada.Core.Database.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Black box file row persistence shared by the four providers. Thread-safe.
    /// </summary>
    public abstract class BlackBoxFileMethodsBase : IBlackBoxFileMethods
    {
        #region Private-Members

        private readonly BlackBoxSqlDialect _Sql;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="sql">Provider dialect.</param>
        /// <exception cref="ArgumentNullException">Thrown when sql is null.</exception>
        protected BlackBoxFileMethodsBase(BlackBoxSqlDialect sql)
        {
            _Sql = sql ?? throw new ArgumentNullException(nameof(sql));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<int> CreateManyAsync(List<BlackBoxFile> files, CancellationToken token = default)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            CollectionLimits.RequireRequestItems(files, nameof(files));
            if (files.Count == 0) return 0;
            int inserted = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                foreach (BlackBoxFile f in files)
                {
                    inserted += await _Sql.ExecuteAsync(conn, tx,
                        "INSERT INTO blackbox_files (id, entry_id, tenant_id, vessel_id, path, directory, role, conflicting_mission_id, created_utc) VALUES (@id, @entry_id, @tenant_id, @vessel_id, @path, @directory, @role, @conflicting_mission_id, @created_utc);",
                        cmd =>
                        {
                            _Sql.Add(cmd, "@id", f.Id);
                            _Sql.Add(cmd, "@entry_id", f.EntryId);
                            _Sql.Add(cmd, "@tenant_id", f.TenantId);
                            _Sql.Add(cmd, "@vessel_id", f.VesselId);
                            _Sql.Add(cmd, "@path", BlackBoxLimits.Clip(f.Path, BlackBoxLimits.MaxPathChars));
                            _Sql.Add(cmd, "@directory", BlackBoxLimits.Clip(f.Directory, 512));
                            _Sql.Add(cmd, "@role", f.Role.ToString());
                            _Sql.Add(cmd, "@conflicting_mission_id", f.ConflictingMissionId);
                            _Sql.AddDate(cmd, "@created_utc", f.CreatedUtc);
                        }, token).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);
            return inserted;
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxFile>> EnumerateAsync(string entryId, BlackBoxFileRoleEnum? role, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            string where = " WHERE entry_id = @entry_id" + (role.HasValue ? " AND role = @role" : String.Empty);
            return BlackBoxSqlPaging.PageAsync(_Sql, "SELECT COUNT(*) FROM blackbox_files" + where,
                "SELECT * FROM blackbox_files" + where + " ORDER BY path ASC, id ASC",
                cmd =>
                {
                    _Sql.Add(cmd, "@entry_id", entryId);
                    if (role.HasValue) _Sql.Add(cmd, "@role", role.Value.ToString());
                }, query.PageNumber, query.PageSize, FromReader, token);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxHotspot>> EnumerateHotspotsAsync(BlackBoxHotspotEnumerateRequest query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<string> conditions = new List<string> { "e.state <> 'Deleting'" };
            if (query.TenantId != null) conditions.Add("f.tenant_id = @tenant_id");
            if (query.UserId != null) conditions.Add("e.user_id = @user_id");
            if (query.VesselId != null) conditions.Add("f.vessel_id = @vessel_id");
            if (query.Role.HasValue) conditions.Add("f.role = @role");
            if (query.CreatedAfter.HasValue) conditions.Add("f.created_utc > @created_after");
            if (query.CreatedBefore.HasValue) conditions.Add("f.created_utc < @created_before");
            string from = " FROM blackbox_files f INNER JOIN blackbox_entries e ON e.id = f.entry_id WHERE " + String.Join(" AND ", conditions);
            Action<DbCommand> bind = cmd =>
            {
                if (query.TenantId != null) _Sql.Add(cmd, "@tenant_id", query.TenantId);
                if (query.UserId != null) _Sql.Add(cmd, "@user_id", query.UserId);
                if (query.VesselId != null) _Sql.Add(cmd, "@vessel_id", query.VesselId);
                if (query.Role.HasValue) _Sql.Add(cmd, "@role", query.Role.Value.ToString());
                if (query.CreatedAfter.HasValue) _Sql.AddDate(cmd, "@created_after", query.CreatedAfter.Value);
                if (query.CreatedBefore.HasValue) _Sql.AddDate(cmd, "@created_before", query.CreatedBefore.Value);
            };
            return BlackBoxSqlPaging.PageAsync(_Sql,
                "SELECT COUNT(*) FROM (SELECT f.vessel_id, f.directory" + from + " GROUP BY f.vessel_id, f.directory) t",
                "SELECT f.vessel_id AS vessel_id, f.directory AS directory, COUNT(*) AS file_count, COUNT(DISTINCT f.entry_id) AS entry_count, MAX(f.created_utc) AS last_seen"
                + from + " GROUP BY f.vessel_id, f.directory ORDER BY COUNT(*) DESC, f.vessel_id ASC, f.directory ASC",
                bind, query.PageNumber, query.PageSize, r => new BlackBoxHotspot
                {
                    VesselId = BlackBoxSqlDialect.ReadString(r["vessel_id"]),
                    Directory = BlackBoxSqlDialect.ReadString(r["directory"]) ?? String.Empty,
                    FileCount = BlackBoxSqlDialect.ReadLong(r["file_count"], 0),
                    EntryCount = BlackBoxSqlDialect.ReadLong(r["entry_count"], 0),
                    LastSeenUtc = _Sql.ReadDate(r["last_seen"])
                }, token);
        }

        /// <inheritdoc />
        public async Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));
            int deleted = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                deleted = await _Sql.ExecuteAsync(conn, tx, "DELETE FROM blackbox_files WHERE entry_id = @entry_id;", cmd => _Sql.Add(cmd, "@entry_id", entryId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private BlackBoxFile FromReader(DbDataReader r)
        {
            BlackBoxFile f = new BlackBoxFile();
            f.Id = r["id"].ToString()!;
            f.EntryId = r["entry_id"].ToString()!;
            f.TenantId = BlackBoxSqlDialect.ReadString(r["tenant_id"]);
            f.VesselId = BlackBoxSqlDialect.ReadString(r["vessel_id"]);
            f.Path = BlackBoxSqlDialect.ReadString(r["path"]) ?? String.Empty;
            f.Directory = BlackBoxSqlDialect.ReadString(r["directory"]) ?? String.Empty;
            f.Role = BlackBoxSqlDialect.ReadEnum(r["role"], BlackBoxFileRoleEnum.Unknown);
            f.ConflictingMissionId = BlackBoxSqlDialect.ReadString(r["conflicting_mission_id"]);
            f.CreatedUtc = _Sql.ReadDate(r["created_utc"]) ?? DateTime.UtcNow;
            return f;
        }

        #endregion
    }
}
