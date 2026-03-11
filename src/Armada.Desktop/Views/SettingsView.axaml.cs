namespace Armada.Desktop.Views
{
    using Avalonia.Controls;
    using Avalonia.Interactivity;
    using Avalonia.VisualTree;
    using Armada.Desktop.ViewModels;

    /// <summary>
    /// Settings view.
    /// </summary>
    public partial class SettingsView : UserControl
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public SettingsView()
        {
            InitializeComponent();
        }

        private async void OnSaveClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel vm)
            {
                await vm.SaveAsync();
            }
        }

        private async void OnCheckServerClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel vm)
            {
                await vm.CheckServerStatusAsync();
            }
        }

        private async void OnStopServerClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel vm)
            {
                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null)
                {
                    ConfirmDialog dialog = new ConfirmDialog(
                        "Stop Server",
                        "This will stop the Admiral server. All captains will be disconnected.",
                        "Stop Server");
                    bool confirmed = await dialog.ShowConfirmAsync(owner);
                    if (!confirmed) return;
                }

                await vm.StopServerAsync();
            }
        }

        private async void OnResetClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel vm)
            {
                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null)
                {
                    ConfirmDialog dialog = new ConfirmDialog(
                        "Reset Armada",
                        "This will permanently delete the database, logs, docks, and bare repositories. Settings will be preserved. This cannot be undone.",
                        "Reset Everything");
                    bool confirmed = await dialog.ShowConfirmAsync(owner);
                    if (!confirmed) return;
                }

                await vm.ResetAsync();
            }
        }

        private async void OnInstallMcpClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel vm)
            {
                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null)
                {
                    ConfirmDialog dialog = new ConfirmDialog(
                        "Install MCP Config",
                        "This will add Armada as an MCP server in ~/.claude/settings.json.",
                        "Install",
                        isDanger: false);
                    bool confirmed = await dialog.ShowConfirmAsync(owner);
                    if (!confirmed) return;
                }

                await vm.InstallMcpAsync();
            }
        }
    }
}
