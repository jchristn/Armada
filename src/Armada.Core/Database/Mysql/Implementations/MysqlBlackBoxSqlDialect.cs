namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// Mysql plumbing for the shared black box SQL (<see cref="BlackBoxSqlDialect"/>).
    /// </summary>
    public class MysqlBlackBoxSqlDialect : BlackBoxSqlDialect
    {
        private readonly string _ConnectionString;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public MysqlBlackBoxSqlDialect(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        /// <inheritdoc />
        public override string Provider => "Mysql";

        /// <inheritdoc />
        public override DbConnection CreateConnection()
        {
            return new MySqlConnection(_ConnectionString);
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
            Add(cmd, name, value.HasValue ? DateTime.SpecifyKind(NormalizeUtc(value.Value), DateTimeKind.Unspecified) : null);
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
