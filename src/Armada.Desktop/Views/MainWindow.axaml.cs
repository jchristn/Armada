namespace Armada.Desktop.Views
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Interactivity;
    using Avalonia.Media.Imaging;
    using Armada.Desktop.Services;
    using Armada.Desktop.ViewModels;

    /// <summary>
    /// Main application window with sidebar navigation.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Bind the toast overlay to a notification service.
        /// </summary>
        /// <param name="service">Notification service.</param>
        public void BindNotifications(DesktopNotificationService service)
        {
            ToastOverlay? overlay = this.FindControl<ToastOverlay>("ToastOverlay");
            overlay?.Bind(service);
        }

        /// <summary>
        /// Update the sidebar logo based on the current theme.
        /// </summary>
        /// <param name="isDark">True for dark theme, false for light.</param>
        public void UpdateLogo(bool isDark)
        {
            Image? logo = this.FindControl<Image>("SidebarLogo");
            if (logo != null)
            {
                string assetPath = isDark
                    ? "avares://Armada.Desktop/Assets/logo-dark.png"
                    : "avares://Armada.Desktop/Assets/logo-light.png";
                Uri uri = new Uri(assetPath);
                using (System.IO.Stream stream = Avalonia.Platform.AssetLoader.Open(uri))
                {
                    logo.Source = new Bitmap(stream);
                }
            }
        }

        /// <summary>
        /// Handle navigation button clicks.
        /// </summary>
        private void OnNavClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string pageName)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.NavigateTo(pageName);
                }
            }
        }
    }
}
