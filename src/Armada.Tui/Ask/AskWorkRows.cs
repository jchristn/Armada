namespace Armada.Tui.Ask
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// The mission or target rows of one tracked work card, read from the paged row enumeration (the snapshot itself
    /// carries counts only). Holds the first page; <see cref="TotalRecords"/> says how many rows exist in all.
    /// </summary>
    public class AskWorkRows
    {
        #region Public-Members

        /// <summary>
        /// Rows read per page.
        /// </summary>
        public const int PageSize = 100;

        /// <summary>
        /// Mission rows (voyage and mission work), in creation order.
        /// </summary>
        public List<AskWorkMissionSnapshot> Missions { get; set; } = new List<AskWorkMissionSnapshot>();

        /// <summary>
        /// Target rows (fleet action runs), by vessel name.
        /// </summary>
        public List<AskWorkTargetSnapshot> Targets { get; set; } = new List<AskWorkTargetSnapshot>();

        /// <summary>
        /// Number of rows on the server (every page).
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskWorkRows()
        {
        }

        #endregion
    }
}
