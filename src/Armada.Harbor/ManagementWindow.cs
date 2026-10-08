namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;

    /// <summary>
    /// The management window: Status, Harbor settings, Armada settings, TUI preferences, Logs, and Backups as tabs.
    /// The menus open it on the tab their command belongs to.
    /// </summary>
    public class ManagementWindow : Window
    {
        #region Private-Members

        private readonly TabControl _Tabs = new TabControl();
        private readonly Dictionary<ManagementTabEnum, TabItem> _TabItems = new Dictionary<ManagementTabEnum, TabItem>();
        private readonly LogBrowserView _Logs;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Harbor session.</param>
        public ManagementWindow(HarborSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            Title = "Armada Harbor - Manage";
            Width = 1040;
            Height = 760;
            MinWidth = 720;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (Application.Current is App app && app.WindowIcon != null) Icon = app.WindowIcon;

            _Logs = new LogBrowserView(session);
            Add(ManagementTabEnum.Status, "Status", new StatusView(session));
            Add(ManagementTabEnum.Harbor, "Harbor", new HarborSettingsView(session));
            Add(ManagementTabEnum.Repositories, "Repositories", new RepositoriesSettingsView(session));
            Add(ManagementTabEnum.Armada, "Armada", new ArmadaSettingsView(session));
            Add(ManagementTabEnum.Tui, "TUI", new TuiSettingsView(session));
            Add(ManagementTabEnum.Logs, "Logs", _Logs);
            Add(ManagementTabEnum.Backups, "Backups", new BackupsView(session));

            _Tabs.Margin = new Thickness(12, 6, 6, 6);
            Content = _Tabs;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Select a tab.
        /// </summary>
        /// <param name="tab">Tab.</param>
        public void ShowTab(ManagementTabEnum tab)
        {
            if (_TabItems.TryGetValue(tab, out TabItem? item)) _Tabs.SelectedItem = item;
        }

        /// <summary>
        /// Open the Logs tab with the cursor in the mission box.
        /// </summary>
        public void ShowMissionLookup()
        {
            ShowTab(ManagementTabEnum.Logs);
            _Logs.FocusMissionLookup();
        }

        /// <summary>
        /// Open the Logs tab on the newest Harbor (or Admiral) log.
        /// </summary>
        /// <param name="harbor">True for Harbor's log, false for the Admiral's.</param>
        public void ShowNewestLog(bool harbor)
        {
            ShowTab(ManagementTabEnum.Logs);
            _Logs.ShowLog(harbor, true);
        }

        #endregion

        #region Private-Methods

        private void Add(ManagementTabEnum tab, string header, Control content)
        {
            // A TextBlock header keeps Fluent's large tab font from applying (TabItem.FontSize would also reach the content).
            TabItem item = new TabItem { Header = new TextBlock { Text = header, FontSize = 16 }, Content = content };
            _TabItems[tab] = item;
            _Tabs.Items.Add(item);
        }

        #endregion
    }
}
