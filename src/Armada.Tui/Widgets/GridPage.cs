namespace Armada.Tui.Widgets
{
    using System.Collections.Generic;

    /// <summary>
    /// One page returned to an <see cref="ArmadaGrid{T}"/> by its loader.
    /// </summary>
    /// <typeparam name="T">Row type.</typeparam>
    public class GridPage<T>
    {
        #region Public-Members

        /// <summary>
        /// Rows. Never null.
        /// </summary>
        public List<T> Rows { get; set; } = new List<T>();

        /// <summary>
        /// Total records across pages.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public GridPage()
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="rows">Rows.</param>
        /// <param name="totalRecords">Total records.</param>
        public GridPage(List<T> rows, long totalRecords)
        {
            Rows = rows ?? new List<T>();
            TotalRecords = totalRecords;
        }

        #endregion
    }
}
