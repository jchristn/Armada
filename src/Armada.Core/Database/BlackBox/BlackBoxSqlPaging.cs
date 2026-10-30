namespace Armada.Core.Database.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Paging helper for the shared black box SQL: a COUNT and a paged SELECT over the same filter on one connection.
    /// </summary>
    public static class BlackBoxSqlPaging
    {
        /// <summary>
        /// Run a count and a paged select and build the page.
        /// </summary>
        /// <typeparam name="T">Row type.</typeparam>
        /// <param name="sql">Dialect.</param>
        /// <param name="countSql">COUNT SQL without a trailing semicolon.</param>
        /// <param name="selectSql">SELECT SQL with ORDER BY and without paging or a trailing semicolon.</param>
        /// <param name="bind">Binder, or null.</param>
        /// <param name="pageNumber">Requested page number.</param>
        /// <param name="pageSize">Requested page size.</param>
        /// <param name="map">Row mapper.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page with exact totals.</returns>
        /// <exception cref="ArgumentNullException">Thrown when sql or map is null.</exception>
        public static async Task<EnumerationResult<T>> PageAsync<T>(BlackBoxSqlDialect sql, string countSql, string selectSql, Action<DbCommand>? bind, int pageNumber, int pageSize, Func<DbDataReader, T> map, CancellationToken token)
        {
            if (sql == null) throw new ArgumentNullException(nameof(sql));
            if (map == null) throw new ArgumentNullException(nameof(map));
            BlackBoxSqlDialect.NormalizePage(pageNumber, pageSize, out int number, out int size, out int offset);
            using (DbConnection conn = sql.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await sql.ScalarLongAsync(conn, null, countSql + ";", bind, token).ConfigureAwait(false);
                List<T> rows = await sql.QueryAsync(conn, null, selectSql + sql.Page(offset, size) + ";", bind, map, token).ConfigureAwait(false);
                EnumerationResult<T> result = new EnumerationResult<T>();
                result.PageNumber = number;
                result.PageSize = size;
                result.TotalRecords = total;
                result.TotalPages = (int)Math.Ceiling((double)total / size);
                result.Objects = rows;
                return result;
            }
        }
    }
}
