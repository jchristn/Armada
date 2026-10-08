namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;

    /// <summary>
    /// The Settings window ("Armada Harbor - Settings"): General (this computer's Harbor settings and the appearance),
    /// Repositories (when that view is present), and Admiral (the Admiral's own settings). The Settings menu
    /// items and button open it.
    /// </summary>
    public class SettingsWindow : Window
    {
        #region Private-Members

        private readonly TabControl _Tabs = new TabControl();
        private readonly Dictionary<SettingsTabEnum, TabItem> _TabItems = new Dictionary<SettingsTabEnum, TabItem>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Harbor session.</param>
        public SettingsWindow(HarborSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            Title = "Armada Harbor - Settings";
            Width = 960;
            Height = 760;
            MinWidth = 680;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (Application.Current is App app && app.WindowIcon != null) Icon = app.WindowIcon;

            Add(SettingsTabEnum.General, "General", new HarborSettingsView(session));
            Control? repositories = CreateRepositoriesView(session);
            if (repositories != null) Add(SettingsTabEnum.Repositories, "Repositories", repositories);
            Add(SettingsTabEnum.Admiral, "Admiral", new ArmadaSettingsView(session));

            _Tabs.Margin = new Thickness(12, 4, 0, 0);
            Content = _Tabs;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Select a tab. A tab that is not present (Repositories before its view exists) leaves the selection alone.
        /// </summary>
        /// <param name="tab">Tab.</param>
        public void ShowTab(SettingsTabEnum tab)
        {
            if (_TabItems.TryGetValue(tab, out TabItem? item)) _Tabs.SelectedItem = item;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// The Repositories tab's content: where this computer keeps vessel checkouts, mission docks, and the Harbor's
        /// own clones.
        /// </summary>
        /// <param name="session">Harbor session.</param>
        /// <returns>The view, or null for no Repositories tab.</returns>
        private static Control? CreateRepositoriesView(HarborSession session)
        {
            return new RepositoriesSettingsView(session);
        }

        private void Add(SettingsTabEnum tab, string header, Control content)
        {
            TabItem item = new TabItem { Header = HarborUi.TabHeader(header), Content = content };
            _TabItems[tab] = item;
            _Tabs.Items.Add(item);
        }

        #endregion
    }
}
