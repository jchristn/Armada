namespace Armada.Core.Database
{
    using System;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Storage;

    /// <summary>Common typed result reader. Provider SQL and identifier delimiters are supplied by provider implementations.</summary>
    public abstract class StorageMethodsBase
    {
        /// <summary>Reads catalog statistics, then optionally counts catalog-discovered tables with a five-second per-query limit.</summary>
        protected async Task<StorageDatabase> ReadAsync(DbConnection connection, string provider, string tablesSql, string sizeSql, bool exactCounts, string openQuote, string closeQuote, CancellationToken token)
        {
            StorageDatabase result = new StorageDatabase { Provider = provider, Status = "Complete" };
            using (DbCommand command = connection.CreateCommand())
            {
                command.CommandTimeout = 5;
                command.CommandText = tablesSql;
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        if (result.Tables.Count >= StorageDatabase.MaxTables) { result.Warnings.Add("TableLimit"); result.Status = "Partial"; break; }
                        result.Tables.Add(new StorageTable
                        {
                            Schema = reader.GetString(0), Name = reader.GetString(1),
                            Rows = Number(reader, 2), DataBytes = Number(reader, 3), IndexBytes = Number(reader, 4),
                            CountKind = reader.IsDBNull(2) ? "Unavailable" : "Estimated"
                        });
                    }
                }
            }
            try { result.AllocatedBytes = await ScalarAsync(connection, sizeSql, token).ConfigureAwait(false); }
            catch (DbException) { result.Status = "Partial"; result.Warnings.Add("DatabaseAllocationUnavailable"); }
            if (exactCounts)
            {
                foreach (StorageTable table in result.Tables)
                {
                    token.ThrowIfCancellationRequested();
                    string quoted = openQuote + table.Name.Replace(closeQuote, closeQuote + closeQuote) + closeQuote;
                    if (table.Schema.Length > 0) quoted = openQuote + table.Schema.Replace(closeQuote, closeQuote + closeQuote) + closeQuote + "." + quoted;
                    try
                    {
                        table.Rows = await ScalarAsync(connection, "SELECT " + (provider == "SqlServer" ? "COUNT_BIG" : "COUNT") + "(*) FROM " + quoted, token).ConfigureAwait(false);
                        table.CountKind = "Exact";
                    }
                    catch (DbException) { table.Warning = "ExactCountUnavailable"; result.Status = "Partial"; }
                }
            }
            return result;
        }

        /// <summary>Executes a read-only scalar with a bounded command timeout.</summary>
        protected async Task<long?> ScalarAsync(DbConnection connection, string sql, CancellationToken token)
        {
            using (DbCommand command = connection.CreateCommand())
            {
                command.CommandTimeout = 5;
                command.CommandText = sql;
                object? value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
                return value == null || value == DBNull.Value ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
        }

        private static long? Number(DbDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? null : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }
    }
}
