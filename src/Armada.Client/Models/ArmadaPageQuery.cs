namespace Armada.Client.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Paging and filter parameters for the list endpoints that take <c>pageNumber</c>, <c>pageSize</c>, and free-form
    /// filters on the query string (the dashboard's <c>buildQuery</c>).
    /// </summary>
    public class ArmadaPageQuery
    {
        #region Public-Members

        /// <summary>
        /// Page number (1-based), or null for the server default. Values below 1 are clamped to 1.
        /// </summary>
        public int? PageNumber
        {
            get { return _PageNumber; }
            set { _PageNumber = value.HasValue ? Math.Max(1, value.Value) : null; }
        }

        /// <summary>
        /// Page size, or null for the server default. Clamped to 1..1000 when set.
        /// </summary>
        public int? PageSize
        {
            get { return _PageSize; }
            set { _PageSize = value.HasValue ? Math.Clamp(value.Value, 1, 1000) : null; }
        }

        /// <summary>
        /// Additional query-string filters (for example <c>status</c>, <c>vesselId</c>, <c>order</c>). Empty values are
        /// skipped. Never null.
        /// </summary>
        public Dictionary<string, string> Filters
        {
            get { return _Filters; }
            set { _Filters = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private int? _PageNumber = null;
        private int? _PageSize = null;
        private Dictionary<string, string> _Filters = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with no paging or filters.
        /// </summary>
        public ArmadaPageQuery()
        {
        }

        /// <summary>
        /// Instantiate for one page.
        /// </summary>
        /// <param name="pageNumber">Page number (1-based).</param>
        /// <param name="pageSize">Page size.</param>
        public ArmadaPageQuery(int pageNumber, int pageSize)
        {
            PageNumber = pageNumber;
            PageSize = pageSize;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add or replace a filter and return this instance for chaining.
        /// </summary>
        /// <param name="key">Filter name.</param>
        /// <param name="value">Filter value; null or empty removes the filter.</param>
        /// <returns>This instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null or empty.</exception>
        public ArmadaPageQuery With(string key, string? value)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (String.IsNullOrEmpty(value)) _Filters.Remove(key);
            else _Filters[key] = value;
            return this;
        }

        /// <summary>
        /// A copy of this query for a different page.
        /// </summary>
        /// <param name="pageNumber">Page number.</param>
        /// <returns>The copy.</returns>
        public ArmadaPageQuery ForPage(int pageNumber)
        {
            ArmadaPageQuery copy = new ArmadaPageQuery();
            copy.PageNumber = pageNumber;
            copy.PageSize = PageSize;
            copy.Filters = new Dictionary<string, string>(_Filters, StringComparer.Ordinal);
            return copy;
        }

        #endregion
    }
}
