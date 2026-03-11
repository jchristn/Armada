namespace Armada.Desktop
{
    using System;
    using System.Threading.Tasks;
    using System.Windows.Input;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.ApplicationLifetimes;
    using Avalonia.Markup.Xaml;
    using Avalonia.Styling;
    using Avalonia.Threading;
    using Armada.Desktop.Services;
    using Armada.Desktop.ViewModels;
    using Armada.Desktop.Views;

    /// <summary>
    /// Application root.
    /// </summary>
    public partial class App : Application
    {
        #region Private-Members

        private ArmadaConnectionService? _ConnectionService;
        private DesktopNotificationService? _NotificationService;
        private MainWindow? _MainWindow;
        private bool _HasShownTrayNotification;

        #endregion

        #region Public-Members

        /// <summary>
        /// Command to show the main window from the tray icon.
        /// </summary>
        public ICommand ShowWindowCommand { get; }

        /// <summary>
        /// Command to exit the application.
        /// </summary>
        public ICommand ExitCommand { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public App()
        {
            ShowWindowCommand = new RelayCommand(() =>
            {
                if (_MainWindow != null)
                {
                    _MainWindow.Show();
                    _MainWindow.WindowState = WindowState.Normal;
                    _MainWindow.Activate();
                }
            });

            ExitCommand = new RelayCommand(() =>
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            });
        }

        #endregion

        /// <inheritdoc />
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = this;
        }

        /// <inheritdoc />
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Global error handling
                AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                {
                    Exception? ex = args.ExceptionObject as Exception;
                    System.Diagnostics.Debug.WriteLine("Unhandled exception: " + ex?.Message);
                };

                TaskScheduler.UnobservedTaskException += (sender, args) =>
                {
                    args.SetObserved();
                    System.Diagnostics.Debug.WriteLine("Unobserved task exception: " + args.Exception?.Message);
                };

                // Prevent app from shutting down when main window is hidden to tray
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                _ConnectionService = new ArmadaConnectionService();
                _NotificationService = new DesktopNotificationService();

                // Wire notification service to data refreshes
                _ConnectionService.DataRefreshed += (sender, e) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _NotificationService.ProcessUpdate(
                            _ConnectionService.Missions,
                            _ConnectionService.Captains,
                            _ConnectionService.Voyages);
                    });
                };

                SplashWindow splash = new SplashWindow();
                desktop.MainWindow = splash;
                splash.Show();

                _ = InitializeAsync(desktop, splash);
            }

            base.OnFrameworkInitializationCompleted();
        }

        #region Public-Members

        /// <summary>
        /// Current theme mode (System, Light, Dark).
        /// </summary>
        public string CurrentThemeMode { get; private set; } = "Dark";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set theme by mode string: System, Light, or Dark.
        /// </summary>
        /// <param name="mode">Theme mode.</param>
        public void SetThemeMode(string mode)
        {
            CurrentThemeMode = mode ?? "Dark";

            if (mode == "System")
            {
                RequestedThemeVariant = ThemeVariant.Default;
            }
            else if (mode == "Light")
            {
                RequestedThemeVariant = ThemeVariant.Light;
            }
            else
            {
                RequestedThemeVariant = ThemeVariant.Dark;
            }

            // Update sidebar logo based on actual resolved theme
            if (_MainWindow != null)
            {
                _MainWindow.UpdateLogo(ActualThemeVariant == ThemeVariant.Dark);
            }
        }

        /// <summary>
        /// Switch between light and dark themes (legacy).
        /// </summary>
        /// <param name="isDark">True for dark, false for light.</param>
        public void SetTheme(bool isDark)
        {
            SetThemeMode(isDark ? "Dark" : "Light");
        }

        #endregion

        #region Private-Methods

        private async Task InitializeAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash)
        {
            try
            {
                await _ConnectionService!.InitializeAsync(message =>
                {
                    Dispatcher.UIThread.Post(() => splash.UpdateStatus(message));
                }).ConfigureAwait(false);

                // Initial data load
                await _ConnectionService.RefreshAsync().ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    MainWindowViewModel mainVm = new MainWindowViewModel(_ConnectionService, _NotificationService!);
                    _MainWindow = new MainWindow
                    {
                        DataContext = mainVm
                    };

                    // Wire navigation callbacks for toast click-to-navigate
                    _NotificationService!.NavigateToMission = (missionId) =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            _MainWindow.Show();
                            _MainWindow.Activate();
                            mainVm.NavigateToMission(missionId);
                        });
                    };
                    _NotificationService.NavigateToVoyage = (voyageId) =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            _MainWindow.Show();
                            _MainWindow.Activate();
                            mainVm.NavigateToVoyage(voyageId);
                        });
                    };

                    // Bind toast notifications
                    _MainWindow.BindNotifications(_NotificationService);

                    // Sync notification settings
                    _NotificationService!.Enabled = _ConnectionService.GetSettings().Notifications;

                    // Handle window closing -- minimize to tray instead of exiting
                    _MainWindow.Closing += (sender, args) =>
                    {
                        args.Cancel = true;

                        if (!_HasShownTrayNotification)
                        {
                            _HasShownTrayNotification = true;
                            _NotificationService!.ShowInfo("Armada Desktop", "Armada Desktop is running in the background");

                            // Delay hide so the toast is briefly visible
                            _ = DelayedHideAsync(_MainWindow);
                        }
                        else
                        {
                            _MainWindow.Hide();
                        }
                    };

                    desktop.MainWindow = _MainWindow;
                    _MainWindow.Show();
                    splash.Close();
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    splash.UpdateStatus("Failed: " + ex.Message);
                });
            }
        }

        private async Task DelayedHideAsync(MainWindow window)
        {
            await Task.Delay(2000).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => window.Hide());
        }

        #endregion
    }

    /// <summary>
    /// Simple relay command for tray icon binding.
    /// </summary>
    internal class RelayCommand : ICommand
    {
        private Action _Execute;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RelayCommand(Action execute)
        {
            _Execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        /// <inheritdoc />
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

        /// <inheritdoc />
        public bool CanExecute(object? parameter) => true;

        /// <inheritdoc />
        public void Execute(object? parameter) => _Execute();
    }
}
