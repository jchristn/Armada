namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// SqlServer plumbing for the shared black box SQL (<see cref="BlackBoxSqlDialect"/>).
    /// </summary>
    public class SqlServerBlackBoxSqlDialect : BlackBoxSqlDialect
    {
        private readonly string _ConnectionString;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQL Server driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public SqlServerBlackBoxSqlDialect(SqlServerDatabaseDriver driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            _ConnectionString = driver.ConnectionString;
        }

        /// <inheritdoc />
        public override string Provider => "SqlServer";

        /// <inheritdoc />
        public override void AddBlob(DbCommand cmd, string name, byte[]? value)
        {
            SqlParameter parameter = new SqlParameter(name, System.Data.SqlDbType.VarBinary, -1);
            parameter.Value = (object?)value ?? DBNull.Value;
            cmd.Parameters.Add(parameter);
        }

        /// <inheritdoc />
        public override DbConnection CreateConnection()
        {
            return new SqlConnection(_ConnectionString);
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
            SqlParameter parameter = new SqlParameter(name, System.Data.SqlDbType.DateTime2);
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
            return " OFFSET " + offset + " ROWS FETCH NEXT " + limit + " ROWS ONLY";
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            if (value.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return value;
        }
    }
}
