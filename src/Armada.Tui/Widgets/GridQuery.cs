namespace Armada.Tui.Widgets
{
    /// <summary>
    /// What an <see cref="ArmadaGrid{T}"/> asks its loader for: one page with an optional server sort.
    /// </summary>
    public class GridQuery
    {
        #region Public-Members

        /// <summary>
        /// 1-based page number.
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size.
        /// </summary>
        public int PageSize { get; set; } = 25;

        /// <summary>
        /// Sort field, or null for the server default.
        /// </summary>
        public string? SortKey { get; set; } = null;

        /// <summary>
        /// Sort descending.
        /// </summary>
        public bool SortDescending { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public GridQuery()
        {
        }

        #endregion
    }
}
