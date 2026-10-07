namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Filters for a page of vessel commit history.
    /// </summary>
    public class VesselCommitQuery
    {
        #region Public-Members

        /// <summary>
        /// Branch, or null for the vessel's default branch.
        /// </summary>
        public string? Branch { get; set; } = null;

        /// <summary>
        /// Only commits with a commit date strictly before this instant (ISO 8601; a bare yyyy-MM-dd means the start of that day in UTC), or null. Used to jump to a date.
        /// </summary>
        public string? Before { get; set; } = null;

        /// <summary>
        /// Cursor from the previous page's NextCursor, or null for the first page. When set, Branch and Before are ignored.
        /// </summary>
        public string? Cursor { get; set; } = null;

        /// <summary>
        /// Page size, 1 to 200 (default 50), or null.
        /// </summary>
        public int? Limit { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselCommitQuery()
        {
        }

        #endregion
    }
}
