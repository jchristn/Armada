namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using MySqlConnector;

    /// <summary>
    /// Reads one bounded page in the MySQL database: a COUNT over the filter, then the rows of the page in a
    /// deterministic order with the provider's paging clause (docs/ENUMERATION.md).
    /// </summary>
    internal static class MysqlBoundedPage
    {
        /// <summary>
        /// Count the rows of <paramref name="fromWhere"/> and read the page the query names.
        /// </summary>
        /// <typeparam name="T">Row type.</typeparam>
        /// <param name="conn">Open connection.</param>
        /// <param name="fromWhere">FROM and WHERE clauses, for example "FROM t WHERE a = @a".</param>
        /// <param name="orderBy">ORDER BY expression, ending with a unique column so the order is total.</param>
        /// <param name="bind">Binds the parameters of the WHERE clause; called once per command.</param>
        /// <param name="map">Maps one row.</param>
        /// <param name="query">Page number and size.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page with exact totals.</returns>
        internal static async Task<EnumerationResult<T>> ReadAsync<T>(MySqlConnection conn, string fromWhere, string orderBy, Action<MySqlCommand>? bind, Func<MySqlDataReader, T> map, EnumerationQuery query, CancellationToken token)
        {
            if (conn == null) throw new ArgumentNullException(nameof(conn));
            if (query == null) throw new ArgumentNullException(nameof(query));
            long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) " + fromWhere + ";", bind, token).ConfigureAwait(false);
            List<T> rows = new List<T>();
            if (total > query.Offset)
            {
                rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * " + fromWhere + " ORDER BY " + orderBy + MysqlCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    bind, map, token).ConfigureAwait(false);
            }

            return EnumerationResult<T>.Create(query, rows, total);
        }
    }
}
