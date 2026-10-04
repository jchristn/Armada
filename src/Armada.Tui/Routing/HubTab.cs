namespace Armada.Tui.Routing
{
    using System;

    /// <summary>
    /// One tab of a hub screen (for example Missions: Merge Queue).
    /// </summary>
    public class HubTab
    {
        #region Public-Members

        /// <summary>
        /// Query value selecting the tab, for example <c>merge-queue</c>.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// English label (catalog key), for example Merge Queue.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// TUI screen implementing the tab (the plan's screen name).
        /// </summary>
        public string ScreenName { get; }

        /// <summary>
        /// Workstream task that builds the screen, for example W3.10.
        /// </summary>
        public string Workstream { get; }

        /// <summary>
        /// Visible only to global admins.
        /// </summary>
        public bool GlobalAdminOnly { get; }

        /// <summary>
        /// Visible only to tenant admins (and global admins).
        /// </summary>
        public bool TenantAdminOnly { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="key">Query value.</param>
        /// <param name="label">English label.</param>
        /// <param name="screenName">Screen name.</param>
        /// <param name="workstream">Workstream task.</param>
        /// <param name="globalAdminOnly">Global admins only.</param>
        /// <param name="tenantAdminOnly">Tenant admins only.</param>
        /// <exception cref="ArgumentNullException">Thrown when a string argument is null.</exception>
        public HubTab(string key, string label, string screenName, string workstream, bool globalAdminOnly = false, bool tenantAdminOnly = false)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Label = label ?? throw new ArgumentNullException(nameof(label));
            ScreenName = screenName ?? throw new ArgumentNullException(nameof(screenName));
            Workstream = workstream ?? throw new ArgumentNullException(nameof(workstream));
            GlobalAdminOnly = globalAdminOnly;
            TenantAdminOnly = tenantAdminOnly;
        }

        #endregion
    }
}
