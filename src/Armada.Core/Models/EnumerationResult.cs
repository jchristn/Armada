namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using Armada.Core.Services;

    /// <summary>
    /// Paginated result wrapper for enumeration queries.
    /// </summary>
    /// <typeparam name="T">Entity type.</typeparam>
    public class EnumerationResult<T>
    {
        #region Public-Members

        /// <summary>
        /// Whether the operation succeeded.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Current page number (1-based).
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size (number of items per page).
        /// </summary>
        public int PageSize { get; set; } = 100;

        /// <summary>
        /// Total number of pages.
        /// </summary>
        public int TotalPages { get; set; }

        /// <summary>
        /// Total number of records matching the query.
        /// </summary>
        public long TotalRecords { get; set; }

        /// <summary>
        /// The result objects for this page.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxPageSize, Reason = "One page")]
        public List<T> Objects { get; set; } = new List<T>();

        /// <summary>
        /// Query execution time in milliseconds.
        /// </summary>
        public double TotalMs { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public EnumerationResult()
        {
        }

        /// <summary>
        /// Create an EnumerationResult from query parameters, data, and total count.
        /// </summary>
        /// <param name="query">The enumeration query.</param>
        /// <param name="data">Result objects for this page.</param>
        /// <param name="totalCount">Total number of matching records.</param>
        /// <returns>Populated EnumerationResult.</returns>
        public static EnumerationResult<T> Create(EnumerationQuery query, List<T> data, long totalCount)
        {
            EnumerationResult<T> result = new EnumerationResult<T>();
            result.PageNumber = query.PageNumber;
            result.PageSize = query.PageSize;
            result.TotalRecords = totalCount;
            result.TotalPages = query.PageSize > 0
                ? (int)Math.Ceiling((double)totalCount / query.PageSize)
                : 0;
            result.Objects = data ?? new List<T>();
            return result;
        }

        /// <summary>
        /// Page an in-memory list with the query's page number and size.
        /// </summary>
        /// <param name="query">The enumeration query.</param>
        /// <param name="all">Every matching record, already filtered and ordered.</param>
        /// <returns>The requested page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        public static EnumerationResult<T> Page(EnumerationQuery query, List<T> all)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<T> source = all ?? new List<T>();
            List<T> page = source.Skip(query.Offset).Take(query.PageSize).ToList();
            return Create(query, page, source.Count);
        }

        #endregion
    }
}
