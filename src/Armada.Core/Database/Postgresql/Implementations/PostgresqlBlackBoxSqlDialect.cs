namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// Postgresql plumbing for the shared black box SQL (<see cref="BlackBoxSqlDialect"/>).
    /// </summary>
    public class PostgresqlBlackBoxSqlDialect : BlackBoxSqlDialect
    {
        private readonly string _ConnectionString;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">PostgreSQL driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public PostgresqlBlackBoxSqlDialect(PostgresqlDatabaseDriver driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            _ConnectionString = driver.ConnectionString;
        }

        /// <inheritdoc />
        public override string Provider => "Postgresql";

        /// <inheritdoc />
        public override string True => "TRUE";

        /// <inheritdoc />
        public override string False => "FALSE";

        /// <inheritdoc />
        public override string BlobSlice(string column, string offsetParameter, string lengthParameter)
        {
            return "substring(" + column + " from CAST(" + offsetParameter + " AS INTEGER) for CAST(" + lengthParameter + " AS INTEGER))";
        }

        /// <inheritdoc />
        public override DbConnection CreateConnection()
        {
            return new NpgsqlConnection(_ConnectionString);
        }

        /// <inheritdoc />
        public override async Task WriteAsync(Func<DbConnection, DbTransaction, Task> work, CancellationToken token)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            using (DbConnection conn = CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (DbTransaction tx = await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    await work(conn, tx).ConfigureAwait(false);
                    await tx.CommitAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public override void AddDate(DbCommand cmd, string name, DateTime? value)
        {
            if (!value.HasValue) { Add(cmd, name, null); return; }
            NpgsqlParameter parameter = new NpgsqlParameter(name, NpgsqlTypes.NpgsqlDbType.Timestamp);
            parameter.Value = DateTime.SpecifyKind(NormalizeUtc(value.Value), DateTimeKind.Unspecified);
            cmd.Parameters.Add(parameter);
        }

        /// <inheritdoc />
        public override DateTime? ReadDate(object? value)
        {
            if (value == null || value == DBNull.Value) return null;
            if (value is DateTime dt) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return DateTime.SpecifyKind(Convert.ToDateTime(value, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);
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
