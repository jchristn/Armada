namespace Armada.Tui.Shell
{
    using Armada.Tui.Routing;

    /// <summary>
    /// One visible sidebar row: a section heading or a destination.
    /// </summary>
    public class SidebarEntry
    {
        #region Public-Members

        /// <summary>
        /// Section (for headings and section items), or null for top-level items.
        /// </summary>
        public NavSection? Section { get; }

        /// <summary>
        /// Destination, or null for a heading.
        /// </summary>
        public NavItem? Item { get; }

        /// <summary>
        /// True for a section heading.
        /// </summary>
        public bool IsHeading
        {
            get { return Item == null; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="section">Section.</param>
        /// <param name="item">Item.</param>
        public SidebarEntry(NavSection? section, NavItem? item)
        {
            Section = section;
            Item = item;
        }

        #endregion
    }
}
