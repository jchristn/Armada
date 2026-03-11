namespace Armada.Desktop.Views
{
    using Avalonia.Controls;
    using Avalonia.Interactivity;
    using Armada.Desktop.ViewModels;

    /// <summary>
    /// Notification history view.
    /// </summary>
    public partial class NotificationHistoryView : UserControl
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public NotificationHistoryView()
        {
            InitializeComponent();
        }

        private void OnMarkAllReadClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is NotificationHistoryViewModel vm)
            {
                vm.MarkAllRead();
            }
        }

        private void OnClearClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is NotificationHistoryViewModel vm)
            {
                vm.ClearHistory();
            }
        }
    }
}
