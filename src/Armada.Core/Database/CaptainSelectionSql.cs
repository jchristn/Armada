namespace Armada.Core.Database
{
    using System;
    using System.Data.Common;
    using System.Globalization;

    /// <summary>
    /// Command, parameter, timestamp, and paging helpers shared by the captain selection stores. They work over any
    /// provider connection.
    /// </summary>
    internal static class CaptainSelectionSql
    {
        internal static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql)
        {
            DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return command;
        }

        internal static void Add(DbCommand command, string name, object? value)
        {
            DbParameter parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        internal static string Utc(DateTime value) => UtcClock.Format(value, "o");

        internal static DateTime ParseUtc(object value) => value is DateTime date ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : UtcClock.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);

        internal static string PageSql(DbConnection connection) => connection.GetType().Name == "SqlConnection"
            ? " OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY" : " LIMIT @take OFFSET @skip";

        internal static void Required(string value, string name)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException(name + " is required.", name);
        }

        internal static void Paging(int pageNumber, int pageSize)
        {
            if (pageNumber < 1 || pageSize < 1 || pageSize > 1000 || (long)(pageNumber - 1) * pageSize > Int32.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page number must be positive and page size between 1 and 1000.");
        }
    }
}
