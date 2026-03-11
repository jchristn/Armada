namespace Armada.Desktop.Views
{
    using Avalonia.Controls;
    using Avalonia.Interactivity;
    using Armada.Desktop.ViewModels;

    /// <summary>
    /// Dispatch view for creating new voyages.
    /// </summary>
    public partial class DispatchView : UserControl
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public DispatchView()
        {
            InitializeComponent();
        }

        private void OnQuickModeClick(object? sender, RoutedEventArgs e)
        {
            Border? quick = this.FindControl<Border>("QuickPanel");
            Border? advanced = this.FindControl<Border>("AdvancedPanel");
            if (quick != null) quick.IsVisible = true;
            if (advanced != null) advanced.IsVisible = false;
        }

        private void OnAdvancedModeClick(object? sender, RoutedEventArgs e)
        {
            Border? quick = this.FindControl<Border>("QuickPanel");
            Border? advanced = this.FindControl<Border>("AdvancedPanel");
            if (quick != null) quick.IsVisible = false;
            if (advanced != null) advanced.IsVisible = true;
        }

        private async void OnDispatchClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is DispatchViewModel vm)
            {
                await vm.DispatchAsync();
            }
        }

        private async void OnLaunchVoyageClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is DispatchViewModel vm)
            {
                await vm.DispatchAdvancedAsync();
            }
        }

        private void OnAddMissionClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is DispatchViewModel vm)
            {
                vm.AddMissionEntry();
            }
        }

        private void OnRemoveMissionClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is MissionEntry entry)
            {
                if (this.DataContext is DispatchViewModel vm)
                {
                    vm.RemoveMissionEntry(entry);
                }
            }
        }
    }
}
