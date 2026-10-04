namespace Armada.Client
{
    using System;

    /// <summary>
    /// The visible window of one page: first and last 1-based record numbers, page count, and navigation flags.
    /// Immutable.
    /// </summary>
    public class PageWindow
    {
        #region Public-Members

        /// <summary>
        /// 1-based page number, clamped to 1..<see cref="TotalPages"/> (or 1 when empty).
        /// </summary>
        public int PageNumber { get; }

        /// <summary>
        /// Page size (at least 1).
        /// </summary>
        public int PageSize { get; }

        /// <summary>
        /// Total records (never negative).
        /// </summary>
        public long TotalRecords { get; }

        /// <summary>
        /// Total pages (at least 1).
        /// </summary>
        public int TotalPages { get; }

        /// <summary>
        /// First record number on the page (1-based), or 0 when there are no records.
        /// </summary>
        public long First { get; }

        /// <summary>
        /// Last record number on the page (1-based), or 0 when there are no records.
        /// </summary>
        public long Last { get; }

        /// <summary>
        /// True when a previous page exists.
        /// </summary>
        public bool HasPrevious
        {
            get { return PageNumber > 1; }
        }

        /// <summary>
        /// True when a next page exists.
        /// </summary>
        public bool HasNext
        {
            get { return PageNumber < TotalPages; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="pageNumber">1-based page number.</param>
        /// <param name="pageSize">Page size; values below 1 become 1.</param>
        /// <param name="totalRecords">Total records; negative becomes 0.</param>
        public PageWindow(int pageNumber, int pageSize, long totalRecords)
        {
            PageSize = Math.Max(1, pageSize);
            TotalRecords = Math.Max(0, totalRecords);
            TotalPages = (int)Math.Max(1, (TotalRecords + PageSize - 1) / PageSize);
            PageNumber = Math.Clamp(pageNumber, 1, TotalPages);
            if (TotalRecords == 0)
            {
                First = 0;
                Last = 0;
            }
            else
            {
                First = ((long)(PageNumber - 1) * PageSize) + 1;
                Last = Math.Min(TotalRecords, (long)PageNumber * PageSize);
            }
        }

        #endregion
    }
}
