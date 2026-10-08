namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;

    /// <summary>
    /// The Status window ("Armada Harbor - Status"): an Overview tab (this Harbor and its link, the Admiral's health and
    /// workload, and data directory disk usage) and a Logs tab. The Status and Logs menu items and buttons open it on
    /// the matching tab.
    /// </summary>
    public class StatusWindow : Window
    {
        #region Private-Members

        private readonly TabControl _Tabs = new TabControl();
        private readonly Dictionary<StatusTabEnum, TabItem> _TabItems = new Dictionary<StatusTabEnum, TabItem>();
        private readonly LogBrowserView _Logs;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Harbor session.</param>
        public StatusWindow(HarborSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            Title = "Armada Harbor - Status";
            Width = 1040;
            Height = 760;
            MinWidth = 720;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (Application.Current is App app && app.WindowIcon != null) Icon = app.WindowIcon;

            _Logs = new LogBrowserView(session);
            Add(StatusTabEnum.Overview, "Overview", new StatusView(session));
            Add(StatusTabEnum.Logs, "Logs", _Logs);

            _Tabs.Margin = new Thickness(12, 4, 0, 0);
            Content = _Tabs;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Select a tab.
        /// </summary>
        /// <param name="tab">Tab.</param>
        public void ShowTab(StatusTabEnum tab)
        {
            if (_TabItems.TryGetValue(tab, out TabItem? item)) _Tabs.SelectedItem = item;
        }

        #endregion

        #region Private-Methods

        private void Add(StatusTabEnum tab, string header, Control content)
        {
            TabItem item = new TabItem { Header = HarborUi.TabHeader(header), Content = content };
            _TabItems[tab] = item;
            _Tabs.Items.Add(item);
        }

        #endregion
    }
}
