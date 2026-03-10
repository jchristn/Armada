namespace Armada.Desktop.Views
{
    using System.Threading.Tasks;
    using Avalonia.Controls;
    using Avalonia.Input;
    using Avalonia.Interactivity;
    using Armada.Desktop.ViewModels;

    /// <summary>
    /// Mission diff viewer window.
    /// </summary>
    public partial class DiffViewerWindow : Window
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public DiffViewerWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Instantiate with view model.
        /// </summary>
        public DiffViewerWindow(DiffViewerViewModel viewModel) : this()
        {
            DataContext = viewModel;
            Title = "Diff: " + viewModel.MissionTitle;
        }

        private async void OnRefreshClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is DiffViewerViewModel vm)
            {
                await vm.LoadDiffAsync();
            }
        }

        private async void OnCopyResponseClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is DiffViewerViewModel vm && Clipboard != null)
            {
                await Clipboard.SetTextAsync(vm.DiffContent);
                if (sender is Button button)
                    await ShowCopiedFeedbackAsync(button);
            }
        }

        private async void OnCopyDiffClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is DiffViewerViewModel vm && Clipboard != null)
            {
                await Clipboard.SetTextAsync(vm.ParsedDiffContent);
                if (sender is Button button)
                    await ShowCopiedFeedbackAsync(button);
            }
        }

        private async void OnCopyAllClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is DiffViewerViewModel vm && Clipboard != null)
            {
                string content = vm.HasParsedDiff ? vm.ParsedDiffContent : vm.DiffContent;
                await Clipboard.SetTextAsync(content);
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
