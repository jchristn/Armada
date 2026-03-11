namespace Armada.Desktop.Views
{
    using Avalonia.Controls;
    using Avalonia.Input;
    using Avalonia.Interactivity;
    using Armada.Desktop.Services;

    /// <summary>
    /// Overlay control that displays toast notifications in the top-right corner.
    /// </summary>
    public partial class ToastOverlay : UserControl
    {
        #region Private-Members

        private DesktopNotificationService? _Service;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ToastOverlay()
        {
            InitializeComponent();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Bind to a notification service.
        /// </summary>
        /// <param name="service">Notification service.</param>
        public void Bind(DesktopNotificationService service)
        {
            _Service = service;
            ItemsControl? list = this.FindControl<ItemsControl>("ToastList");
            if (list != null)
            {
                list.ItemsSource = service.ActiveToasts;
            }
        }

        #endregion

        #region Private-Methods

        private void OnDismissClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ToastNotification toast && _Service != null)
            {
                _Service.Dismiss(toast);
            }
        }

        private void OnToastBodyClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.DataContext is ToastNotification toast)
            {
                toast.OnClick?.Invoke();
                _Service?.Dismiss(toast);
            }
        }

        #endregion
    }
}
