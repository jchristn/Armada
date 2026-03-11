namespace Armada.Desktop.Views
{
    using Avalonia.Controls;
    using Avalonia.Interactivity;
    using Avalonia.VisualTree;
    using Armada.Desktop.ViewModels;

    /// <summary>
    /// Dashboard view - live status display.
    /// </summary>
    public partial class DashboardView : UserControl
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public DashboardView()
        {
            InitializeComponent();
        }

        private async void OnDeleteVoyageClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string voyageId && DataContext is DashboardViewModel vm)
            {
                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null)
                {
                    ConfirmDialog dialog = new ConfirmDialog(
                        "Delete Voyage",
                        "Permanently delete this voyage and all its missions? This cannot be undone.",
                        "Delete");
                    bool confirmed = await dialog.ShowConfirmAsync(owner);
                    if (!confirmed) return;
                }

                await vm.DeleteVoyageAsync(voyageId);
            }
        }

        private async void OnDeleteMissionFromActionClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string missionId && DataContext is DashboardViewModel vm)
            {
                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null)
                {
                    ConfirmDialog dialog = new ConfirmDialog(
                        "Delete Mission",
                        "Permanently delete this mission? This cannot be undone.",
                        "Delete");
                    bool confirmed = await dialog.ShowConfirmAsync(owner);
                    if (!confirmed) return;
                }

                await vm.DeleteMissionAsync(missionId);
            }
        }
    }
}
