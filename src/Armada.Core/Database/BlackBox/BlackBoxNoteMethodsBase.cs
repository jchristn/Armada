namespace Armada.Core.Database.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Black box note persistence shared by the four providers. Thread-safe.
    /// </summary>
    public abstract class BlackBoxNoteMethodsBase : IBlackBoxNoteMethods
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
        protected BlackBoxNoteMethodsBase(BlackBoxSqlDialect sql)
        {
            _Sql = sql ?? throw new ArgumentNullException(nameof(sql));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<BlackBoxNote> CreateAsync(BlackBoxNote note, CancellationToken token = default)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                await _Sql.ExecuteAsync(conn, tx,
                    "INSERT INTO blackbox_notes (id, entry_id, tenant_id, user_id, author_kind, body, created_utc) VALUES (@id, @entry_id, @tenant_id, @user_id, @author_kind, @body, @created_utc);",
                    cmd =>
                    {
                        _Sql.Add(cmd, "@id", note.Id);
                        _Sql.Add(cmd, "@entry_id", note.EntryId);
                        _Sql.Add(cmd, "@tenant_id", note.TenantId);
                        _Sql.Add(cmd, "@user_id", note.UserId);
                        _Sql.Add(cmd, "@author_kind", note.AuthorKind.ToString());
                        _Sql.Add(cmd, "@body", BlackBoxLimits.Clip(note.Body, BlackBoxLimits.MaxNoteChars));
                        _Sql.AddDate(cmd, "@created_utc", note.CreatedUtc);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return note;
        }

        /// <inheritdoc />
        public async Task<BlackBoxNote?> ReadAsync(string id, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<BlackBoxNote> rows = await _Sql.QueryAsync("SELECT * FROM blackbox_notes WHERE id = @id" + (tenantId != null ? " AND tenant_id = @tenant_id" : String.Empty) + ";",
                cmd =>
                {
                    _Sql.Add(cmd, "@id", id);
                    if (tenantId != null) _Sql.Add(cmd, "@tenant_id", tenantId);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxNote>> EnumerateAsync(string entryId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";
            return BlackBoxSqlPaging.PageAsync(_Sql, "SELECT COUNT(*) FROM blackbox_notes WHERE entry_id = @entry_id",
                "SELECT * FROM blackbox_notes WHERE entry_id = @entry_id ORDER BY created_utc " + direction + ", id " + direction,
                cmd => _Sql.Add(cmd, "@entry_id", entryId), query.PageNumber, query.PageSize, FromReader, token);
        }

        /// <inheritdoc />
        public async Task<long> CountByEntryAsync(string entryId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));
            return await _Sql.ScalarLongAsync("SELECT COUNT(*) FROM blackbox_notes WHERE entry_id = @entry_id;", cmd => _Sql.Add(cmd, "@entry_id", entryId), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int deleted = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                deleted = await _Sql.ExecuteAsync(conn, tx, "DELETE FROM blackbox_notes WHERE id = @id;", cmd => _Sql.Add(cmd, "@id", id), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted > 0;
        }

        /// <inheritdoc />
        public async Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));
            int deleted = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                deleted = await _Sql.ExecuteAsync(conn, tx, "DELETE FROM blackbox_notes WHERE entry_id = @entry_id;", cmd => _Sql.Add(cmd, "@entry_id", entryId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private BlackBoxNote FromReader(DbDataReader r)
        {
            BlackBoxNote n = new BlackBoxNote();
            n.Id = r["id"].ToString()!;
            n.EntryId = r["entry_id"].ToString()!;
            n.TenantId = BlackBoxSqlDialect.ReadString(r["tenant_id"]);
            n.UserId = BlackBoxSqlDialect.ReadString(r["user_id"]);
            n.AuthorKind = BlackBoxSqlDialect.ReadEnum(r["author_kind"], BlackBoxNoteAuthorKindEnum.Unknown);
            n.Body = BlackBoxSqlDialect.ReadString(r["body"]) ?? String.Empty;
            n.CreatedUtc = _Sql.ReadDate(r["created_utc"]) ?? DateTime.UtcNow;
            return n;
        }

        #endregion
    }
}
