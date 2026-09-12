namespace Armada.Harbor
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.ApplicationLifetimes;
    using Avalonia.Markup.Xaml;
    using Avalonia.Platform;

    /// <summary>
    /// The Armada Harbor Avalonia application. Framework code-behind, so it is a partial class as Avalonia
    /// requires. Owns the shared settings, the main window, and the system tray icon, and keeps the runner
    /// alive in the tray when the window is closed.
    /// </summary>
    public partial class App : Application
    {
        #region Private-Members

        private HarborAppSettings? _Settings;
        private MainWindow? _Window;
        private TrayIcon? _TrayIcon;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initialize the application from XAML.
        /// </summary>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Complete framework initialization: load settings, show the window, and install the tray icon.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                _Settings = HarborAppSettings.Load();
                _Settings.Save();

                _Window = new MainWindow(_Settings);
                _Window.Show();

                InstallTray(desktop);
            }

            base.OnFrameworkInitializationCompleted();
        }

        #endregion

        #region Private-Methods

        private void InstallTray(IClassicDesktopStyleApplicationLifetime desktop)
        {
            NativeMenu menu = new NativeMenu();

            NativeMenuItem open = new NativeMenuItem { Header = "Open Armada Harbor" };
            open.Click += (sender, args) => ShowWindow();
            menu.Items.Add(open);

            NativeMenuItem dashboard = new NativeMenuItem { Header = "Open Dashboard" };
            dashboard.Click += (sender, args) => OpenDashboard();
            menu.Items.Add(dashboard);

            menu.Items.Add(new NativeMenuItemSeparator());

            NativeMenuItem quit = new NativeMenuItem { Header = "Quit" };
            quit.Click += (sender, args) => desktop.Shutdown();
            menu.Items.Add(quit);

            _TrayIcon = new TrayIcon
            {
                Icon = LoadIcon(),
                ToolTipText = "Armada Harbor",
                Menu = menu,
                IsVisible = true
            };
            _TrayIcon.Clicked += (sender, args) => ShowWindow();

            TrayIcons icons = new TrayIcons { _TrayIcon };
            TrayIcon.SetIcons(this, icons);
        }

        private static WindowIcon? LoadIcon()
        {
            try
            {
                using (Stream stream = AssetLoader.Open(new Uri("avares://Armada.Harbor/Assets/logo.png")))
                {
                    return new WindowIcon(stream);
                }
            }
            catch
            {
                return null;
            }
        }

        private void ShowWindow()
        {
            if (_Window == null) return;
            _Window.Show();
            _Window.WindowState = WindowState.Normal;
            _Window.Activate();
        }

        private void OpenDashboard()
        {
            if (_Settings == null) return;
            try
            {
                Process.Start(new ProcessStartInfo(_Settings.DashboardUrl) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        #endregion
    }
}
