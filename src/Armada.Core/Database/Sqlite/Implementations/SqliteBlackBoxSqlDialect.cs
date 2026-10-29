namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// Sqlite plumbing for the shared black box SQL (<see cref="BlackBoxSqlDialect"/>).
    /// </summary>
    public class SqliteBlackBoxSqlDialect : BlackBoxSqlDialect
    {
        private readonly string _ConnectionString;
        private readonly SqliteWriteGate _WriteGate;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQLite driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public SqliteBlackBoxSqlDialect(SqliteDatabaseDriver driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            _ConnectionString = driver.ConnectionString;
            _WriteGate = driver.WriteGate;
        }

        /// <inheritdoc />
        public override string Provider => "Sqlite";

        /// <inheritdoc />
        public override string BlobSlice(string column, string offsetParameter, string lengthParameter)
        {
            return "substr(" + column + ", " + offsetParameter + ", " + lengthParameter + ")";
        }

        /// <inheritdoc />
        public override DbConnection CreateConnection()
        {
            return new SqliteProviderConnection(_ConnectionString);
        }

        /// <inheritdoc />
        public override async Task WriteAsync(Func<DbConnection, DbTransaction, Task> work, CancellationToken token)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            using (SqliteWriteLease writeLease = await _WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteTransaction tx = (SqliteTransaction)await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    await work(conn, tx).ConfigureAwait(false);
                    await tx.CommitAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public override void AddDate(DbCommand cmd, string name, DateTime? value)
        {
            Add(cmd, name, value.HasValue ? SqliteDatabaseDriver.ToIso8601(NormalizeUtc(value.Value)) : null);
        }

        /// <inheritdoc />
        public override DateTime? ReadDate(object? value)
        {
            if (value == null || value == DBNull.Value) return null;
            return SqliteDatabaseDriver.FromIso8601Nullable(value);
        }

        /// <inheritdoc />
        public override string Page(int offset, int limit)
        {
            return " LIMIT " + limit + " OFFSET " + offset;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            if (value.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return value;
        }
    }
}
