namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

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

        #endregion
    }
}
