namespace Armada.Desktop.Views
{
    using System.Threading.Tasks;
    using Avalonia.Controls;
    using Avalonia.Input;
    using Avalonia.Interactivity;
    using Armada.Desktop.ViewModels;

    /// <summary>
    /// Mission log viewer window.
    /// </summary>
    public partial class MissionLogViewerWindow : Window
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public MissionLogViewerWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Instantiate with view model.
        /// </summary>
        public MissionLogViewerWindow(MissionLogViewerViewModel viewModel) : this()
        {
            DataContext = viewModel;
            Title = "Armada - Mission Log: " + viewModel.MissionTitle;
            Closed += (s, e) => viewModel.Dispose();
        }

        /// <summary>
        /// Handle Ctrl+Shift+C to copy full content.
        /// </summary>
        protected override async void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.C && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
            {
                if (DataContext is MissionLogViewerViewModel vm && Clipboard != null)
                {
                    await Clipboard.SetTextAsync(vm.LogContent);
                }
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        private async void OnRefreshClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MissionLogViewerViewModel vm)
            {
                await vm.LoadLogAsync();
            }
        }

        private async void OnCopyClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MissionLogViewerViewModel vm && Clipboard != null)
            {
                await Clipboard.SetTextAsync(vm.LogContent);
                if (sender is Button button)
                    await ShowCopiedFeedbackAsync(button);
            }
        }

        /// <summary>
        /// Briefly change button text to "Copied!" then revert.
        /// </summary>
        private static async Task ShowCopiedFeedbackAsync(Button button)
        {
            object? original = button.Content;
            button.Content = "Copied!";
            await Task.Delay(1500);
            button.Content = original;
        }
    }
}
