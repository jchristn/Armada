namespace Armada.Core.Database.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Black box link row persistence shared by the four providers. Thread-safe.
    /// </summary>
    public abstract class BlackBoxLinkMethodsBase : IBlackBoxLinkMethods
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
        protected BlackBoxLinkMethodsBase(BlackBoxSqlDialect sql)
        {
            _Sql = sql ?? throw new ArgumentNullException(nameof(sql));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<int> CreateManyAsync(List<BlackBoxLink> links, CancellationToken token = default)
        {
            if (links == null) throw new ArgumentNullException(nameof(links));
            CollectionLimits.RequireRequestItems(links, nameof(links));
            if (links.Count == 0) return 0;
            int inserted = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                foreach (BlackBoxLink l in links)
                {
                    inserted += await _Sql.ExecuteAsync(conn, tx,
                        "INSERT INTO blackbox_links (id, entry_id, tenant_id, mission_id, relation, commit_hash, overlap_file_count, created_utc) VALUES (@id, @entry_id, @tenant_id, @mission_id, @relation, @commit_hash, @overlap_file_count, @created_utc);",
                        cmd =>
                        {
                            _Sql.Add(cmd, "@id", l.Id);
                            _Sql.Add(cmd, "@entry_id", l.EntryId);
                            _Sql.Add(cmd, "@tenant_id", l.TenantId);
                            _Sql.Add(cmd, "@mission_id", l.MissionId);
                            _Sql.Add(cmd, "@relation", l.Relation.ToString());
                            _Sql.Add(cmd, "@commit_hash", l.CommitHash);
                            _Sql.Add(cmd, "@overlap_file_count", l.OverlapFileCount);
                            _Sql.AddDate(cmd, "@created_utc", l.CreatedUtc);
                        }, token).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);
            return inserted;
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxLink>> EnumerateAsync(string entryId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            return BlackBoxSqlPaging.PageAsync(_Sql, "SELECT COUNT(*) FROM blackbox_links WHERE entry_id = @entry_id",
                "SELECT * FROM blackbox_links WHERE entry_id = @entry_id ORDER BY created_utc ASC, id ASC",
                cmd => _Sql.Add(cmd, "@entry_id", entryId), query.PageNumber, query.PageSize, FromReader, token);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxLink>> EnumerateByMissionAsync(string missionId, string? tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            string where = " WHERE mission_id = @mission_id" + (tenantId != null ? " AND tenant_id = @tenant_id" : String.Empty);
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";
            return BlackBoxSqlPaging.PageAsync(_Sql, "SELECT COUNT(*) FROM blackbox_links" + where,
                "SELECT * FROM blackbox_links" + where + " ORDER BY created_utc " + direction + ", id " + direction,
                cmd =>
                {
                    _Sql.Add(cmd, "@mission_id", missionId);
                    if (tenantId != null) _Sql.Add(cmd, "@tenant_id", tenantId);
                }, query.PageNumber, query.PageSize, FromReader, token);
        }

        /// <inheritdoc />
        public async Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));
            int deleted = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                deleted = await _Sql.ExecuteAsync(conn, tx, "DELETE FROM blackbox_links WHERE entry_id = @entry_id;", cmd => _Sql.Add(cmd, "@entry_id", entryId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private BlackBoxLink FromReader(DbDataReader r)
        {
            BlackBoxLink l = new BlackBoxLink();
            l.Id = r["id"].ToString()!;
            l.EntryId = r["entry_id"].ToString()!;
            l.TenantId = BlackBoxSqlDialect.ReadString(r["tenant_id"]);
            l.MissionId = BlackBoxSqlDialect.ReadString(r["mission_id"]) ?? String.Empty;
            l.Relation = BlackBoxSqlDialect.ReadEnum(r["relation"], BlackBoxLinkRelationEnum.Unknown);
            l.CommitHash = BlackBoxSqlDialect.ReadString(r["commit_hash"]);
            l.OverlapFileCount = (int)BlackBoxSqlDialect.ReadLong(r["overlap_file_count"], 0);
            l.CreatedUtc = _Sql.ReadDate(r["created_utc"]) ?? DateTime.UtcNow;
            return l;
        }

        #endregion
    }
}
