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
    /// Shared SQL for black box storage migration jobs (blackbox_migrations) over a provider dialect.
    /// </summary>
    public abstract class BlackBoxMigrationMethodsBase : IBlackBoxMigrationMethods
    {
        #region Private-Members

        private const string _Columns = "id, state, from_target, to_target, delete_source, batch_size, total_entries, migrated_entries, failed_entries, bytes_copied, last_entry_id, last_error, created_utc, started_utc, finished_utc, last_update_utc";
        private readonly BlackBoxSqlDialect _Sql;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="sql">Provider dialect.</param>
        /// <exception cref="ArgumentNullException">Thrown when sql is null.</exception>
        protected BlackBoxMigrationMethodsBase(BlackBoxSqlDialect sql)
        {
            _Sql = sql ?? throw new ArgumentNullException(nameof(sql));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<BlackBoxStorageMigrationJob> CreateAsync(BlackBoxStorageMigrationJob job, CancellationToken token = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            job.LastUpdateUtc = DateTime.UtcNow;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                await _Sql.ExecuteAsync(conn, tx,
                    "INSERT INTO blackbox_migrations (" + _Columns + ") VALUES (@id, @state, @from_target, @to_target, @delete_source, @batch_size, @total_entries, @migrated_entries, @failed_entries, @bytes_copied, @last_entry_id, @last_error, @created_utc, @started_utc, @finished_utc, @last_update_utc);",
                    cmd => Bind(cmd, job), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return job;
        }

        /// <inheritdoc />
        public async Task<BlackBoxStorageMigrationJob?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<BlackBoxStorageMigrationJob> rows = await _Sql.QueryAsync("SELECT " + _Columns + " FROM blackbox_migrations WHERE id = @id;",
                cmd => _Sql.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<bool> UpdateAsync(BlackBoxStorageMigrationJob job, CancellationToken token = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            job.LastUpdateUtc = DateTime.UtcNow;
            int changed = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                changed = await _Sql.ExecuteAsync(conn, tx,
                    "UPDATE blackbox_migrations SET state = @state, from_target = @from_target, to_target = @to_target, delete_source = @delete_source, batch_size = @batch_size, "
                    + "total_entries = @total_entries, migrated_entries = @migrated_entries, failed_entries = @failed_entries, bytes_copied = @bytes_copied, last_entry_id = @last_entry_id, "
                    + "last_error = @last_error, started_utc = @started_utc, finished_utc = @finished_utc, last_update_utc = @last_update_utc WHERE id = @id;",
                    cmd => Bind(cmd, job), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return changed > 0;
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxStorageMigrationJob>> EnumerateActiveAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            string where = " WHERE state IN ('Pending', 'Running')";
            return BlackBoxSqlPaging.PageAsync(_Sql, "SELECT COUNT(*) FROM blackbox_migrations" + where,
                "SELECT " + _Columns + " FROM blackbox_migrations" + where + " ORDER BY created_utc ASC, id ASC",
                null, query.PageNumber, query.PageSize, FromReader, token);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxStorageMigrationJob>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return BlackBoxSqlPaging.PageAsync(_Sql, "SELECT COUNT(*) FROM blackbox_migrations",
                "SELECT " + _Columns + " FROM blackbox_migrations ORDER BY created_utc DESC, id DESC",
                null, query.PageNumber, query.PageSize, FromReader, token);
        }

        #endregion

        #region Private-Methods

        private void Bind(DbCommand cmd, BlackBoxStorageMigrationJob job)
        {
            _Sql.Add(cmd, "@id", job.Id);
            _Sql.Add(cmd, "@state", job.State.ToString());
            _Sql.Add(cmd, "@from_target", job.From?.ToString());
            _Sql.Add(cmd, "@to_target", job.To.ToString());
            _Sql.Add(cmd, "@delete_source", job.DeleteSource);
            _Sql.Add(cmd, "@batch_size", job.BatchSize);
            _Sql.Add(cmd, "@total_entries", job.TotalEntries);
            _Sql.Add(cmd, "@migrated_entries", job.MigratedEntries);
            _Sql.Add(cmd, "@failed_entries", job.FailedEntries);
            _Sql.Add(cmd, "@bytes_copied", job.BytesCopied);
            _Sql.Add(cmd, "@last_entry_id", job.LastEntryId);
            _Sql.Add(cmd, "@last_error", BlackBoxLimits.Clip(job.LastError, BlackBoxLimits.MaxShortTextChars));
            _Sql.AddDate(cmd, "@created_utc", job.CreatedUtc);
            _Sql.AddDate(cmd, "@started_utc", job.StartedUtc);
            _Sql.AddDate(cmd, "@finished_utc", job.FinishedUtc);
            _Sql.AddDate(cmd, "@last_update_utc", job.LastUpdateUtc);
        }

        private BlackBoxStorageMigrationJob FromReader(DbDataReader r)
        {
            BlackBoxStorageMigrationJob job = new BlackBoxStorageMigrationJob();
            job.Id = r["id"].ToString()!;
            job.State = BlackBoxSqlDialect.ReadEnum(r["state"], BlackBoxJobStateEnum.Unknown);
            string? from = BlackBoxSqlDialect.ReadString(r["from_target"]);
            job.From = from == null ? (BlackBoxStorageTargetEnum?)null : BlackBoxSqlDialect.ReadEnum(r["from_target"], BlackBoxStorageTargetEnum.Unknown);
            job.To = BlackBoxSqlDialect.ReadEnum(r["to_target"], BlackBoxStorageTargetEnum.Unknown);
            job.DeleteSource = BlackBoxSqlDialect.ReadBool(r["delete_source"], true);
            job.BatchSize = (int)BlackBoxSqlDialect.ReadLong(r["batch_size"], 50);
            job.TotalEntries = BlackBoxSqlDialect.ReadLong(r["total_entries"], 0);
            job.MigratedEntries = BlackBoxSqlDialect.ReadLong(r["migrated_entries"], 0);
            job.FailedEntries = BlackBoxSqlDialect.ReadLong(r["failed_entries"], 0);
            job.BytesCopied = BlackBoxSqlDialect.ReadLong(r["bytes_copied"], 0);
            job.LastEntryId = BlackBoxSqlDialect.ReadString(r["last_entry_id"]);
            job.LastError = BlackBoxSqlDialect.ReadString(r["last_error"]);
            job.CreatedUtc = _Sql.ReadDate(r["created_utc"]) ?? DateTime.UtcNow;
            job.StartedUtc = _Sql.ReadDate(r["started_utc"]);
            job.FinishedUtc = _Sql.ReadDate(r["finished_utc"]);
            job.LastUpdateUtc = _Sql.ReadDate(r["last_update_utc"]) ?? job.CreatedUtc;
            return job;
        }

        #endregion
    }
}
