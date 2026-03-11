namespace Armada.Desktop.Views
{
    using Avalonia.Controls;
    using Avalonia.Interactivity;
    using Avalonia.VisualTree;
    using Armada.Desktop.Services;
    using Armada.Desktop.ViewModels;

    /// <summary>
    /// Mission list view.
    /// </summary>
    public partial class MissionListView : UserControl
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public MissionListView()
        {
            InitializeComponent();
        }

        private async void OnRetryClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MissionListViewModel vm && vm.SelectedMission != null)
            {
                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null)
                {
                    RestartMissionDialog dialog = new RestartMissionDialog(
                        vm.SelectedMission.Title,
                        vm.SelectedMission.Description ?? "");
                    bool confirmed = await dialog.ShowRestartAsync(owner);
                    if (!confirmed) return;

                    await vm.RestartMissionAsync(
                        vm.SelectedMission.Id,
                        dialog.ResultTitle,
                        dialog.ResultDescription);
                }
            }
        }

        private async void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MissionListViewModel vm && vm.SelectedMission != null)
            {
                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null)
                {
                    ConfirmDialog dialog = new ConfirmDialog(
                        "Cancel Mission",
                        $"Are you sure you want to cancel \"{vm.SelectedMission.Title}\"?",
                        "Cancel Mission");
                    bool confirmed = await dialog.ShowConfirmAsync(owner);
                    if (!confirmed) return;
                }

                await vm.CancelMissionAsync(vm.SelectedMission.Id);
            }
        }

        private async void OnDeleteMissionClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MissionListViewModel vm && vm.SelectedMission != null)
            {
                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null)
                {
                    ConfirmDialog dialog = new ConfirmDialog(
                        "Delete Mission",
                        $"Permanently delete \"{vm.SelectedMission.Title}\"? This cannot be undone.",
                        "Delete");
                    bool confirmed = await dialog.ShowConfirmAsync(owner);
                    if (!confirmed) return;
                }

                await vm.DeleteMissionAsync(vm.SelectedMission.Id);
            }
        }

        private void OnToggleCreateMissionClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MissionListViewModel vm) vm.ShowCreateMission = !vm.ShowCreateMission;
        }

        private async void OnCreateMissionClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MissionListViewModel vm) await vm.CreateMissionAsync();
        }

        private void OnCancelCreateMissionClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MissionListViewModel vm) vm.ShowCreateMission = false;
        }

        private void OnPreviousPageClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MissionListViewModel vm) vm.PreviousPage();
        }

        private void OnNextPageClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MissionListViewModel vm) vm.NextPage();
        }

        private async void OnRefreshPageClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MissionListViewModel vm) await vm.RefreshPageAsync();
        }

        private void OnMissionRowClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string missionId && DataContext is MissionListViewModel vm)
            {
                MissionDisplayItem? item = null;
                foreach (MissionDisplayItem m in vm.Missions)
                {
                    if (m.Mission.Id == missionId) { item = m; break; }
                }
                if (item != null) vm.SelectedDisplayItem = item;
            }
        }

        private void OnViewDiffClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MissionListViewModel vm && vm.SelectedMission != null)
            {
                DiffViewerViewModel diffVm = new DiffViewerViewModel(
                    vm.GetConnection(),
                    vm.SelectedMission.Id,
                    vm.SelectedMission.Title);
                DiffViewerWindow diffWindow = new DiffViewerWindow(diffVm);

                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null) diffWindow.Show(owner);
                else diffWindow.Show();
            }
        }

        private void OnViewLogClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MissionListViewModel vm && vm.SelectedMission != null)
            {
                MissionLogViewerViewModel logVm = new MissionLogViewerViewModel(
                    vm.SelectedMission.Id,
                    vm.SelectedMission.Title,
                    vm.GetConnection().GetSettings());
                MissionLogViewerWindow logWindow = new MissionLogViewerWindow(logVm);

                Window? owner = this.FindAncestorOfType<Window>();
                if (owner != null) logWindow.Show(owner);
                else logWindow.Show();
            }
        }
    }
}
