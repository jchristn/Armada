namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net.WebSockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Interactivity;
    using Avalonia.Media;
    using Avalonia.Threading;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// The Harbor status window. Framework code-behind (partial class as Avalonia requires). Owns the
    /// reconnecting link loop, surfaces status, and streams link activity (work in, status out) to a
    /// copyable log so an operator can watch the Admiral issue work and results propagate back. Closing the
    /// window hides it to the tray rather than stopping the runner.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Public-Members

        /// <summary>
        /// Base name of Harbor's log file in <see cref="HarborAppSettings.LogDirectory"/> (daily files append a date).
        /// </summary>
        public const string HarborLogBaseName = "harbor.log";

        /// <summary>
        /// Raised on the UI thread when the link state, the link loop, or the MCP URL changes.
        /// </summary>
        public event EventHandler? StateChanged;

        /// <summary>
        /// Current link state.
        /// </summary>
        public HarborLinkStateEnum LinkState
        {
            get { return _LinkState; }
        }

        /// <summary>
        /// True while the link loop runs (connecting, connected, or retrying).
        /// </summary>
        public bool IsLinkRunning
        {
            get { return _RunCts != null; }
        }

        /// <summary>
        /// MCP base URL the Admiral advertised at the last handshake, or null.
        /// </summary>
        public string? McpUrl
        {
            get { return _McpUrl; }
        }

        #endregion

        #region Private-Members

        private const int _MaxLogLines = 500;

        private static readonly IBrush _Green = new SolidColorBrush(Color.Parse("#22c55e"));
        private static readonly IBrush _Amber = new SolidColorBrush(Color.Parse("#f59e0b"));
        private static readonly IBrush _Red = new SolidColorBrush(Color.Parse("#ef4444"));
        private static readonly IBrush _Gray = new SolidColorBrush(Color.Parse("#9ca3af"));

        private readonly HarborAppSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly List<string> _LogLines = new List<string>();
        private CancellationTokenSource? _RunCts;
        private HarborLinkStateEnum _LinkState = HarborLinkStateEnum.Idle;
        private string? _McpUrl = null;
        private HarborLinkClient? _Client = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Parameterless constructor for the Avalonia runtime XAML loader / previewer. Uses default settings.
        /// </summary>
        public MainWindow() : this(new HarborAppSettings())
        {
        }

        /// <summary>
        /// Instantiate with the shared application settings.
        /// </summary>
        /// <param name="settings">Harbor application settings.</param>
        public MainWindow(HarborAppSettings settings)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            InitializeComponent();

            _Logging = CreateLogging();
            RefreshSettingsDisplay();
            SetLinkState(HarborLinkStateEnum.Idle);

            Closing += OnWindowClosing;
            Opened += OnWindowOpened;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the link loop without showing the window (used when Harbor starts minimized at login). Calling it when
        /// the loop is already running does nothing.
        /// </summary>
        public void StartAutoConnect()
        {
            StartConnecting(true);
        }

        /// <summary>
        /// Start the link loop at the operator's request. Does nothing when it is already running.
        /// </summary>
        public void Connect()
        {
            StartConnecting(false);
        }

        /// <summary>
        /// Stop the link loop at the operator's request. Does nothing when it is not running.
        /// </summary>
        public void Disconnect()
        {
            if (_RunCts == null) return;
            StopConnecting();
            SetDetail("Disconnected by operator.");
            AppendInfo("Disconnected by operator");
        }

        /// <summary>
        /// Drop the current link (if any) and dial again.
        /// </summary>
        public void Reconnect()
        {
            if (_RunCts != null)
            {
                StopConnecting();
                AppendInfo("Reconnect requested");
            }

            StartConnecting(false);
        }

        /// <summary>
        /// Add an informational line to the activity log (menu command results and failures).
        /// </summary>
        /// <param name="message">Message.</param>
        public void ReportActivity(string message)
        {
            if (!string.IsNullOrWhiteSpace(message)) AppendInfo(message);
        }

        /// <summary>
        /// Show the current settings (name, link URL) after they were edited.
        /// </summary>
        public void RefreshSettingsDisplay()
        {
            HarborNameText.Text = _Settings.Name + "  (" + _Settings.HarborId + ")";
            ServerText.Text = _Settings.ServerLinkUrl;
            if (AppearanceBox.SelectedIndex != (int)_Settings.Appearance) AppearanceBox.SelectedIndex = (int)_Settings.Appearance;
        }

        /// <summary>
        /// Identifiers of the jobs running on this Harbor now; empty when not linked.
        /// </summary>
        /// <returns>Job identifiers.</returns>
        public List<string> LiveJobIds()
        {
            HarborLinkClient? client = _Client;
            return client != null ? client.LiveJobIds() : new List<string>();
        }

        /// <summary>
        /// The most recent activity log lines, oldest first.
        /// </summary>
        /// <param name="maxLines">Maximum number of lines.</param>
        /// <returns>Copy of the lines.</returns>
        public List<string> RecentActivity(int maxLines)
        {
            int count = Math.Min(Math.Max(maxLines, 0), _LogLines.Count);
            return _LogLines.GetRange(_LogLines.Count - count, count);
        }

        #endregion

        #region Private-Methods

        private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
        {
            // Quitting (the menu's Quit, Command-Q, logging out) must be allowed to close the window, or the shutdown is
            // cancelled.
            if (e.CloseReason == WindowCloseReason.ApplicationShutdown || e.CloseReason == WindowCloseReason.OSShutdown) return;

            // Keep the runner alive in the tray instead of exiting; the tray "Quit" item shuts the app down.
            e.Cancel = true;
            Hide();
            // On macOS, leave only the menu-bar icon while the window is closed (see MacActivationPolicy).
            MacActivationPolicy.HideFromDock();
        }

        private void OnWindowOpened(object? sender, EventArgs e)
        {
            // Once the window is on screen (and the native application has finished launching), make sure Harbor has
            // a Dock icon and app menu; a policy set earlier in startup can be reset by LSUIElement.
            MacActivationPolicy.ShowInDock();
            // Connect automatically on startup so an operator does not have to click Connect; the link loop
            // reconnects on its own after transient drops.
            StartConnecting(true);
        }

        private void OnAppearanceChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (AppearanceBox == null || AppearanceBox.SelectedIndex < 0) return;
            HarborAppearanceEnum appearance = (HarborAppearanceEnum)AppearanceBox.SelectedIndex;
            if (Application.Current is App app) app.ApplyAppearance(appearance);
            if (_Settings.Appearance == appearance) return;
            _Settings.Appearance = appearance;
            _Settings.Save();
        }

        private void OnConnectClick(object? sender, RoutedEventArgs e)
        {
            Connect();
        }

        private void StartConnecting(bool automatic)
        {
            if (_RunCts != null) return;
            _RunCts = new CancellationTokenSource();
            ConnectButton.IsEnabled = false;
            DisconnectButton.IsEnabled = true;
            AppendInfo(automatic ? "Auto-connecting on startup" : "Connect requested");
            _ = RunLoopAsync(_RunCts.Token);
            RaiseStateChanged();
        }

        private void StopConnecting()
        {
            _RunCts?.Cancel();
            _RunCts = null;
            ConnectButton.IsEnabled = true;
            DisconnectButton.IsEnabled = false;
            SetLinkState(HarborLinkStateEnum.Idle);
            RaiseStateChanged();
        }

        private void OnDisconnectClick(object? sender, RoutedEventArgs e)
        {
            Disconnect();
        }

        private void OnOpenDashboardClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_Settings.DashboardUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetDetail("Could not open dashboard: " + ex.Message);
            }
        }

        private void OnStatusClick(object? sender, RoutedEventArgs e)
        {
            RunMenuCommand(HarborMenuCommandEnum.Status);
        }

        private void OnSettingsClick(object? sender, RoutedEventArgs e)
        {
            RunMenuCommand(HarborMenuCommandEnum.Settings);
        }

        private void OnLogsClick(object? sender, RoutedEventArgs e)
        {
            // The Admiral's log when it is on this machine, else Harbor's own.
            if (Application.Current is App app && app.CanExecute(HarborMenuCommandEnum.OpenAdmiralLog)) app.Execute(HarborMenuCommandEnum.OpenAdmiralLog);
            else RunMenuCommand(HarborMenuCommandEnum.OpenHarborLog);
        }

        private static void RunMenuCommand(HarborMenuCommandEnum command)
        {
            if (Application.Current is App app) app.Execute(command);
        }

        private async void OnCopyLogClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                TopLevel? top = TopLevel.GetTopLevel(this);
                if (top?.Clipboard != null)
                    await top.Clipboard.SetTextAsync(LogBox.Text ?? string.Empty);
            }
            catch
            {
            }
        }

        private void OnClearLogClick(object? sender, RoutedEventArgs e)
        {
            _LogLines.Clear();
            LogBox.Text = string.Empty;
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            LocalHostCommandExecutor executor = new LocalHostCommandExecutor();
            Armada.Runtimes.LocalHarborJobRunner jobRunner = new Armada.Runtimes.LocalHarborJobRunner(_Logging);
            List<HarborCapability> capabilities = BuildCapabilities();

            while (!token.IsCancellationRequested)
            {
                using (WebSocketHarborTransport transport = new WebSocketHarborTransport(new Uri(_Settings.ServerLinkUrl), BuildHeaders()))
                {
                    HarborLinkClient client = new HarborLinkClient(
                        _Settings.HarborId,
                        _Settings.Name,
                        capabilities,
                        _Settings.MaxConcurrentJobs,
                        executor,
                        _Logging,
                        _Settings.HeartbeatIntervalMs,
                        AppendLog,
                        jobRunner);
                    _Client = client;

                    try
                    {
                        SetLinkState(HarborLinkStateEnum.Connecting);
                        SetDetail("Dialing " + _Settings.ServerLinkUrl + " ...");
                        await client.RunSessionAsync(transport, token, () =>
                        {
                            SetLinkState(HarborLinkStateEnum.Connected);
                            SetDetail("Linked to " + _Settings.ServerLinkUrl + ". Awaiting work.");
                            SetMcp(client.McpBaseUrl);
                        }).ConfigureAwait(false);

                        // A session ended by Disconnect or Reconnect must not report over the state they set.
                        if (token.IsCancellationRequested) break;
                        SetLinkState(HarborLinkStateEnum.Disconnected);
                        SetDetail("The link closed. Retrying in 3 seconds...");
                        AppendInfo("Link closed; retrying in 3s");
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (token.IsCancellationRequested) break;
                        SetLinkState(HarborLinkStateEnum.Disconnected);
                        SetDetail(DescribeConnectError(ex));
                        AppendInfo("Connect failed: " + ex.Message);
                    }

                    SetMcp(client.McpBaseUrl);
                }

                _Client = null;
                if (token.IsCancellationRequested) break;
                try
                {
                    await Task.Delay(3000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private string DescribeConnectError(Exception ex)
        {
            string detail = ex.Message;
            if (ex is WebSocketException || ex.InnerException is WebSocketException)
            {
                detail = ex.Message
                    + "\n\nThe Admiral did not complete the Harbor link handshake. The server-side Harbor link "
                    + "endpoint may not be enabled in this Admiral build yet, or the link URL / credentials may "
                    + "be wrong.\n\nLink URL: " + _Settings.ServerLinkUrl
                    + "\nRetrying in 3 seconds...";
            }

            return detail;
        }

        private List<HarborCapability> BuildCapabilities()
        {
            List<HarborCapability> capabilities = new List<HarborCapability>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string name in _Settings.Capabilities)
            {
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                    capabilities.Add(new HarborCapability { Name = name, Available = true });
            }

            // Advertise the agent runtimes this host can launch so the Admiral's router can match a captain's
            // requested runtime. The local job runner can drive any runtime; a launch fails with a clear error
            // if the corresponding CLI is not installed.
            foreach (string runtime in Enum.GetNames(typeof(Armada.Core.Enums.AgentRuntimeEnum)))
            {
                if (seen.Add(runtime))
                    capabilities.Add(new HarborCapability { Name = runtime, Available = true });
            }

            return capabilities;
        }

        private Dictionary<string, string>? BuildHeaders()
        {
            Dictionary<string, string> headers = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(_Settings.AccessKey)) headers["x-access-key"] = _Settings.AccessKey;
            if (!string.IsNullOrWhiteSpace(_Settings.Secret)) headers["x-secret-key"] = _Settings.Secret;
            // Informational only: the Admiral takes the owning user from the AccessKey credential and ignores
            // x-user-guid; x-tenant-guid applies to a credential-less loopback link or a global-admin credential.
            if (!string.IsNullOrWhiteSpace(_Settings.UserId)) headers["x-user-guid"] = _Settings.UserId;
            if (!string.IsNullOrWhiteSpace(_Settings.TenantId)) headers["x-tenant-guid"] = _Settings.TenantId;
            return headers.Count > 0 ? headers : null;
        }

        private void AppendInfo(string message)
        {
            AppendLog(new HarborLogEntry(HarborLogDirection.Info, message));
        }

        private void AppendLog(HarborLogEntry entry)
        {
            string line = entry.ToString();
            _Logging.Info("[Harbor] " + (entry.Direction == HarborLogDirection.Info ? "" : entry.Direction + " ") + entry.Message);
            Dispatcher.UIThread.Post(() =>
            {
                _LogLines.Add(line);
                if (_LogLines.Count > _MaxLogLines)
                    _LogLines.RemoveRange(0, _LogLines.Count - _MaxLogLines);

                LogBox.Text = string.Join("\n", _LogLines);
                LogBox.CaretIndex = LogBox.Text.Length;
            });
        }

        private static LoggingModule CreateLogging()
        {
            // Harbor's log: the link client, the job runner, and the activity log, in daily files under
            // ~/.armada-harbor/logs (harbor.log.yyyyMMdd), like the Admiral's admiral.log.
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            try
            {
                Directory.CreateDirectory(HarborAppSettings.LogDirectory());
                logging.Settings.MinimumSeverity = Severity.Info;
                logging.Settings.FileLogging = FileLoggingMode.FileWithDate;
                logging.Settings.LogFilename = Path.Combine(HarborAppSettings.LogDirectory(), HarborLogBaseName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // No log folder: run without a file log rather than not at all.
                logging.Settings.FileLogging = FileLoggingMode.Disabled;
            }

            return logging;
        }

        private void SetLinkState(HarborLinkStateEnum state)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _LinkState = state;
                switch (state)
                {
                    case HarborLinkStateEnum.Connecting:
                        StatusText.Text = "Connecting...";
                        StatusDot.Fill = _Amber;
                        break;
                    case HarborLinkStateEnum.Connected:
                        StatusText.Text = "Connected";
                        StatusDot.Fill = _Green;
                        break;
                    case HarborLinkStateEnum.Disconnected:
                        StatusText.Text = "Disconnected";
                        StatusDot.Fill = _Red;
                        break;
                    default:
                        StatusText.Text = "Idle";
                        StatusDot.Fill = _Gray;
                        break;
                }

                RaiseStateChanged();
            });
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetDetail(string detail)
        {
            Dispatcher.UIThread.Post(() => DetailText.Text = detail);
        }

        private void SetMcp(string? mcp)
        {
            Dispatcher.UIThread.Post(() =>
            {
                McpText.Text = string.IsNullOrEmpty(mcp) ? "-" : mcp;
                if (string.Equals(_McpUrl, mcp, StringComparison.Ordinal)) return;
                _McpUrl = mcp;
                RaiseStateChanged();
            });
        }

        #endregion
    }
}
