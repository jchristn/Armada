namespace Armada.Tui.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Per-table preferences persisted for a grid (column visibility, page size, sort).
    /// </summary>
    public class TablePreferences
    {
        #region Public-Members

        /// <summary>
        /// Visible column keys in order, or null for the defaults.
        /// </summary>
        public List<string>? VisibleColumns { get; set; } = null;

        /// <summary>
        /// Page size, or null for the default.
        /// </summary>
        public int? PageSize { get; set; } = null;

        /// <summary>
        /// Sort column key, or null.
        /// </summary>
        public string? SortColumn { get; set; } = null;

        /// <summary>
        /// Sort descending.
        /// </summary>
        public bool SortDescending { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TablePreferences()
        {
        }

        #endregion
    }
}
