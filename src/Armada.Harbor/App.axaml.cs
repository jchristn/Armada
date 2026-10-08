namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Armada.Core.Hosting;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.ApplicationLifetimes;
    using Avalonia.Input.Platform;
    using Avalonia.Markup.Xaml;
    using Avalonia.Platform;
    using Avalonia.Styling;

    /// <summary>
    /// The Armada Harbor Avalonia application. Framework code-behind, so it is a partial class as Avalonia
    /// requires. Owns the shared session, the main, Status, and Settings windows, the menus (macOS application menu, window
    /// menu, tray menu), and the system tray icon, carries out the menu commands, and keeps the runner alive in the tray
    /// when the windows are closed.
    /// </summary>
    public partial class App : Application, IHarborMenuHost
    {
        #region Public-Members

        /// <summary>
        /// One-line link status for the top of the tray menu.
        /// </summary>
        public string StatusLine
        {
            get
            {
                if (_Window == null || _Settings == null) return "Starting...";
                string server = DescribeServer(_Settings.ServerLinkUrl);
                switch (_Window.LinkState)
                {
                    case HarborLinkStateEnum.Connected: return "Connected to " + server;
                    case HarborLinkStateEnum.Connecting: return "Connecting to " + server + "...";
                    case HarborLinkStateEnum.Disconnected: return "Disconnected from " + server + " (retrying)";
                    case HarborLinkStateEnum.Error: return "Cannot reach " + server + " (retrying)";
                    default: return "Not connected";
                }
            }
        }

        /// <summary>
        /// The Harbor icon for windows, or null when the asset is missing.
        /// </summary>
        public WindowIcon? WindowIcon { get; private set; } = null;

        #endregion

        #region Private-Members

        private const string _DocumentationUrl = "https://github.com/jchristn/armada#readme";
        private const int _DiagnosticsActivityLines = 60;

        private HarborAppSettings? _Settings;
        private MainWindow? _Window;
        private HarborSession? _Session;
        private StatusWindow? _StatusWindow;
        private SettingsWindow? _SettingsWindow;
        private AboutWindow? _About;
        private TrayIcon? _TrayIcon;
        private HarborMenuBuilder? _Menus;
        private IClassicDesktopStyleApplicationLifetime? _Desktop;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initialize the application from XAML and install the macOS application menu, which must be in place before
        /// Avalonia would create its default ("About Avalonia") one.
        /// </summary>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            _Menus = new HarborMenuBuilder(this);
            NativeMenu.SetMenu(this, _Menus.BuildApplicationMenu());
        }

        /// <summary>
        /// Complete framework initialization: load settings, show the window, and install the tray icon.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _Desktop = desktop;
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                WindowIcon = LoadIcon();

                _Settings = HarborAppSettings.Load();
                _Settings.Save();
                ApplyAppearance(_Settings.Appearance);

                _Window = new MainWindow(_Settings);
                _Session = new HarborSession(_Settings, _Window);
                _Session.Changed += (sender, args) => RefreshMenus();
                if (_Menus != null) NativeMenu.SetMenu(_Window, _Menus.BuildWindowMenu());

                if (Program.StartMinimized)
                {
                    // Started by the login item: stay in the menu bar / tray, but connect as if the window had opened.
                    MacActivationPolicy.HideFromDock();
                    _Window.StartAutoConnect();
                }
                else
                {
                    MacActivationPolicy.ShowInDock();
                    _Window.Show();
                }

                ApplyDockIcon();
                InstallTray();
                _ = _Session.ResolveAdmiralAsync();
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Apply a color scheme to the whole application. System follows the operating system's setting.
        /// </summary>
        /// <param name="appearance">Scheme to apply.</param>
        public void ApplyAppearance(HarborAppearanceEnum appearance)
        {
            if (appearance == HarborAppearanceEnum.Light) RequestedThemeVariant = ThemeVariant.Light;
            else if (appearance == HarborAppearanceEnum.Dark) RequestedThemeVariant = ThemeVariant.Dark;
            else RequestedThemeVariant = ThemeVariant.Default;
        }

        /// <summary>
        /// True when the menu command can run now.
        /// </summary>
        /// <param name="command">Command.</param>
        /// <returns>True when enabled.</returns>
        public bool CanExecute(HarborMenuCommandEnum command)
        {
            switch (command)
            {
                case HarborMenuCommandEnum.ShowWindow:
                case HarborMenuCommandEnum.About:
                case HarborMenuCommandEnum.OpenHarborFolder:
                case HarborMenuCommandEnum.Documentation:
                case HarborMenuCommandEnum.Quit:
                    return true;
                case HarborMenuCommandEnum.Settings:
                case HarborMenuCommandEnum.Status:
                case HarborMenuCommandEnum.Logs:
                case HarborMenuCommandEnum.CopyHarborId:
                case HarborMenuCommandEnum.OpenDashboard:
                case HarborMenuCommandEnum.CopyDiagnostics:
                    return _Session != null;
                case HarborMenuCommandEnum.Connect:
                    return _Window != null && !_Window.IsLinkRunning;
                case HarborMenuCommandEnum.Disconnect:
                case HarborMenuCommandEnum.Reconnect:
                    return _Window != null && _Window.IsLinkRunning;
                case HarborMenuCommandEnum.CopyMcpUrl:
                    return !String.IsNullOrEmpty(_Window?.McpUrl);
                case HarborMenuCommandEnum.MinimizeWindow:
                case HarborMenuCommandEnum.CloseWindow:
                    return _Window != null;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Why the menu command is unavailable, or null.
        /// </summary>
        /// <param name="command">Command.</param>
        /// <returns>Reason, or null.</returns>
        public string? DisabledReason(HarborMenuCommandEnum command)
        {
            switch (command)
            {
                case HarborMenuCommandEnum.CopyMcpUrl:
                    return "The Admiral sends its MCP URL when the link connects.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Run a menu command. Does nothing when <see cref="CanExecute"/> is false.
        /// </summary>
        /// <param name="command">Command.</param>
        public void Execute(HarborMenuCommandEnum command)
        {
            if (!CanExecute(command)) return;

            switch (command)
            {
                case HarborMenuCommandEnum.ShowWindow:
                    ShowWindow();
                    break;
                case HarborMenuCommandEnum.About:
                    ShowAbout();
                    break;
                case HarborMenuCommandEnum.Settings:
                    ShowSettings(SettingsTabEnum.General);
                    break;
                case HarborMenuCommandEnum.Status:
                    ShowStatus(StatusTabEnum.Overview);
                    break;
                case HarborMenuCommandEnum.Logs:
                    ShowStatus(StatusTabEnum.Logs);
                    break;
                case HarborMenuCommandEnum.Connect:
                    _Window?.Connect();
                    break;
                case HarborMenuCommandEnum.Disconnect:
                    _Window?.Disconnect();
                    break;
                case HarborMenuCommandEnum.Reconnect:
                    _Window?.Reconnect();
                    break;
                case HarborMenuCommandEnum.CopyHarborId:
                    _ = CopyAsync(_Settings!.HarborId, "Harbor ID");
                    break;
                case HarborMenuCommandEnum.CopyMcpUrl:
                    _ = CopyAsync(_Window!.McpUrl!, "MCP URL");
                    break;
                case HarborMenuCommandEnum.OpenHarborFolder:
                    Directory.CreateDirectory(HarborAppSettings.SettingsDirectory());
                    Report(PlatformShell.Open(HarborAppSettings.SettingsDirectory(), out string? harborError), "open the Harbor folder", harborError);
                    break;
                case HarborMenuCommandEnum.OpenDashboard:
                    Report(PlatformShell.Open(_Settings!.DashboardUrl, out string? dashboardError), "open the dashboard", dashboardError);
                    break;
                case HarborMenuCommandEnum.Documentation:
                    Report(PlatformShell.Open(_DocumentationUrl, out string? docsError), "open the documentation", docsError);
                    break;
                case HarborMenuCommandEnum.CopyDiagnostics:
                    _ = CopyAsync(BuildDiagnostics(), "Diagnostics");
                    break;
                case HarborMenuCommandEnum.MinimizeWindow:
                    ActiveWindow()?.SetValue(Window.WindowStateProperty, WindowState.Minimized);
                    break;
                case HarborMenuCommandEnum.CloseWindow:
                    ActiveWindow()?.Close();
                    break;
                case HarborMenuCommandEnum.Quit:
                    _Desktop?.Shutdown();
                    break;
            }

            RefreshMenus();
        }

        #endregion

        #region Private-Methods

        private void InstallTray()
        {
            _TrayIcon = new TrayIcon
            {
                Icon = WindowIcon,
                ToolTipText = "Armada Harbor",
                Menu = _Menus?.BuildTrayMenu(),
                IsVisible = true
            };
            _TrayIcon.Clicked += (sender, args) => ShowWindow();

            TrayIcons icons = new TrayIcons { _TrayIcon };
            TrayIcon.SetIcons(this, icons);
        }

        private void RefreshMenus()
        {
            _Menus?.Refresh();
            if (_TrayIcon != null) _TrayIcon.ToolTipText = "Armada Harbor - " + StatusLine;
        }

        private static void ApplyDockIcon()
        {
            // Inside the .app bundle the Dock uses Contents/Resources/AppIcon.icns; the runtime icon is only the
            // fallback for dotnet run and the bare executable, which macOS would otherwise show as "exec".
            if (!OperatingSystem.IsMacOS() || MacActivationPolicy.IsRunningFromBundle()) return;

            try
            {
                using (Stream stream = AssetLoader.Open(new Uri("avares://Armada.Harbor/Assets/logo-macos.png")))
                {
                    MacDockIcon.TryApply(stream);
                }
            }
            catch (FileNotFoundException)
            {
                // Missing asset: keep the default Dock icon.
            }
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
            MacActivationPolicy.ShowInDock();
            _Window.Show();
            _Window.WindowState = WindowState.Normal;
            _Window.Activate();
        }

        private StatusWindow? ShowStatus(StatusTabEnum tab)
        {
            if (_Session == null) return null;
            if (_StatusWindow == null)
            {
                StatusWindow window = new StatusWindow(_Session);
                NativeMenu? menu = _Menus?.BuildWindowMenu();
                if (menu != null) NativeMenu.SetMenu(window, menu);
                window.Closed += (sender, args) =>
                {
                    _Menus?.Release(menu);
                    _StatusWindow = null;
                    HarborDialog.HideFromDockWhenNoWindows();
                };
                _StatusWindow = window;
            }

            _StatusWindow.ShowTab(tab);
            Present(_StatusWindow);
            return _StatusWindow;
        }

        private SettingsWindow? ShowSettings(SettingsTabEnum tab)
        {
            if (_Session == null) return null;
            if (_SettingsWindow == null)
            {
                SettingsWindow window = new SettingsWindow(_Session);
                NativeMenu? menu = _Menus?.BuildWindowMenu();
                if (menu != null) NativeMenu.SetMenu(window, menu);
                window.Closed += (sender, args) =>
                {
                    _Menus?.Release(menu);
                    _SettingsWindow = null;
                    HarborDialog.HideFromDockWhenNoWindows();
                };
                _SettingsWindow = window;
            }

            _SettingsWindow.ShowTab(tab);
            Present(_SettingsWindow);
            return _SettingsWindow;
        }

        private static void Present(Window window)
        {
            // On macOS a window only comes forward while the app is a regular (Dock) app.
            MacActivationPolicy.ShowInDock();
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }

        private void ShowAbout()
        {
            if (_Settings == null) return;
            if (_About != null)
            {
                _About.Activate();
                return;
            }

            MacActivationPolicy.ShowInDock();
            _About = new AboutWindow(_Settings, _Session?.Admiral, this);
            _About.Closed += (sender, args) =>
            {
                _About = null;
                HarborDialog.HideFromDockWhenNoWindows();
            };
            _About.Show();
            _About.Activate();
        }

        private Window? ActiveWindow()
        {
            if (_StatusWindow != null && _StatusWindow.IsActive) return _StatusWindow;
            if (_SettingsWindow != null && _SettingsWindow.IsActive) return _SettingsWindow;
            if (_About != null && _About.IsActive) return _About;
            return _Window;
        }

        private string BuildDiagnostics()
        {
            List<string> activity = _Window?.RecentActivity(_DiagnosticsActivityLines) ?? new List<string>();
            HarborLinkStateEnum state = _Window?.LinkState ?? HarborLinkStateEnum.Idle;
            return HarborDiagnostics.Build(_Settings!, state, _Window?.McpUrl, _Session?.Admiral, activity);
        }

        private async Task CopyAsync(string text, string what)
        {
            IClipboard? clipboard = (ActiveWindow() ?? _Window)?.Clipboard;
            if (clipboard == null)
            {
                Report(false, "copy the " + what, "no clipboard is available");
                return;
            }

            try
            {
                await clipboard.SetTextAsync(text);
                _Window?.ReportActivity(what + " copied to the clipboard");
            }
            catch (Exception ex)
            {
                Report(false, "copy the " + what, ex.Message);
            }
        }

        private void Report(bool succeeded, string action, string? error)
        {
            if (succeeded) return;
            string message = "Could not " + action + ": " + (error ?? "unknown error");
            _Window?.ReportActivity(message);
            // Seen even when the command came from the tray with every window closed.
            _ = HarborDialog.ShowMessageAsync(ActiveWindow(), "Armada Harbor", message);
        }

        private static string DescribeServer(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return uri.Authority;
            return url;
        }

        #endregion
    }
}
