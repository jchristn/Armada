namespace Armada.Desktop.Views
{
    using Avalonia.Controls;
    using Avalonia.Interactivity;
    using Avalonia.VisualTree;
    using Armada.Desktop.ViewModels;

    /// <summary>
    /// Voyage list view.
    /// </summary>
    public partial class VoyageListView : UserControl
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public VoyageListView()
        {
            InitializeComponent();
        }

        private void OnPreviousPageClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VoyageListViewModel vm) vm.PreviousPage();
        }

        private void OnNextPageClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VoyageListViewModel vm) vm.NextPage();
        }

        private async void OnRefreshPageClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VoyageListViewModel vm) await vm.RefreshPageAsync();
        }

        private void OnVoyageClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string voyageId)
            {
                if (DataContext is VoyageListViewModel vm)
                {
                    vm.SelectVoyage(voyageId);
                }
            }
        }

        private async void OnRetryFailedClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VoyageListViewModel vm && vm.SelectedVoyage != null)
            {
                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null)
                {
                    ConfirmDialog dialog = new ConfirmDialog(
                        "Retry Failed Missions",
                        $"This will create new missions to retry all failed tasks in \"{vm.SelectedVoyage.Title}\".",
                        "Retry",
                        isDanger: false);
                    bool confirmed = await dialog.ShowConfirmAsync(owner);
                    if (!confirmed) return;
                }

                await vm.RetryFailedAsync(vm.SelectedVoyage.Id);
            }
        }

        private async void OnCancelVoyageClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VoyageListViewModel vm && vm.SelectedVoyage != null)
            {
                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null)
                {
                    ConfirmDialog dialog = new ConfirmDialog(
                        "Cancel Voyage",
                        $"Are you sure you want to cancel \"{vm.SelectedVoyage.Title}\"? Active missions will be stopped.",
                        "Cancel Voyage");
                    bool confirmed = await dialog.ShowConfirmAsync(owner);
                    if (!confirmed) return;
                }

                await vm.CancelVoyageAsync(vm.SelectedVoyage.Id);
            }
        }

        private void OnToggleCreateVoyageClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VoyageListViewModel vm) vm.ShowCreateVoyage = !vm.ShowCreateVoyage;
        }

        private async void OnCreateVoyageClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VoyageListViewModel vm) await vm.CreateVoyageAsync();
        }

        private void OnCancelCreateVoyageClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VoyageListViewModel vm) vm.ShowCreateVoyage = false;
        }

        private async void OnDeleteVoyageClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is VoyageListViewModel vm && vm.SelectedVoyage != null)
            {
                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null)
                {
                    ConfirmDialog dialog = new ConfirmDialog(
                        "Delete Voyage",
                        $"This will permanently delete \"{vm.SelectedVoyage.Title}\" and all its associated missions. This cannot be undone.",
                        "Delete");
                    bool confirmed = await dialog.ShowConfirmAsync(owner);
                    if (!confirmed) return;
                }

                await vm.DeleteVoyageAsync(vm.SelectedVoyage.Id);
            }
        }
    }
}
