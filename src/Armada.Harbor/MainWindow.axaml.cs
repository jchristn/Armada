namespace Armada.Harbor
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net.WebSockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Hosting;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Core.Storage;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Input.Platform;
    using Avalonia.Interactivity;
    using Avalonia.Media;
    using Avalonia.Threading;
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
        public const string HarborLogBaseName = HarborLogPaths.HarborLogBaseName;

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
        // Busy links log dozens of entries a second; the log box is redrawn at most this often, never once per entry.
        private static readonly TimeSpan _LogRenderInterval = TimeSpan.FromMilliseconds(250);

        private readonly HarborAppSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly HarborActivityLog _ActivityLog = new HarborActivityLog(_MaxLogLines);
        // Shared by every session's link client, so a dock made in one session is still known when the next removes it.
        private readonly HarborLogClassifier _LogClassifier = new HarborLogClassifier();
        private bool _LoadingShowDetails = false;
        private CancellationTokenSource? _RunCts;
        private HarborLinkStateEnum _LinkState = HarborLinkStateEnum.Idle;
        private string? _McpUrl = null;
        // The job runner and what Harbor knows about its jobs live as long as the window, not one link session or one
        // Connect: a job keeps running through a link drop or a Disconnect, keeps its place in Running now, is listed in
        // the next session's heartbeats, and reports its remaining output and its exit when the link is back.
        private readonly HarborJobState _JobState = new HarborJobState();
        private readonly HarborLinkStatistics _LinkStatistics = new HarborLinkStatistics();
        // The link loop itself; shared with a headless start, which runs the same host without this window.
        private readonly HarborLinkHost _Host;
        private string? _LastRuntimeSummary = null;
        private readonly DispatcherTimer _JobsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        // Entries logged from any thread wait here; the UI thread takes them in batches (DrainActivity).
        private readonly ConcurrentQueue<HarborLogEntry> _PendingLog = new ConcurrentQueue<HarborLogEntry>();
        private int _LogDrainScheduled = 0;
        private bool _ActivityDirty = false;
        private IDisposable? _StatusDotBinding = null;

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
            _Host = new HarborLinkHost(_Settings, _Logging, _JobState, _LinkStatistics, _LogClassifier);
            _Host.OnLinkState = SetLinkState;
            _Host.OnDetail = SetDetail;
            _Host.OnMcp = SetMcp;
            _Host.OnLog = AppendLog;
            _Host.OnRuntimesDetected = ShowRuntimes;
            _ActivityLog.ShowDetails = _Settings.ShowActivityDetails;
            _LoadingShowDetails = true;
            ShowDetailsBox.IsChecked = _Settings.ShowActivityDetails;
            _LoadingShowDetails = false;
            RefreshSettingsDisplay();
            UpdateConnectButton();
            SetLinkState(HarborLinkStateEnum.Idle);

            Closing += OnWindowClosing;
            Opened += OnWindowOpened;
            JobsList.OpenLink += OpenJobLink;
            JobsList.ViewOutput += view =>
            {
                if (view.LogPath != null && Application.Current is App app) app.ShowJobLog(view.LogPath, view.MissionId);
            };

            // Elapsed times tick while the window shows; nothing to do while it is hidden in the tray.
            _JobsTimer.Tick += (sender, args) => RefreshJobs();
            Opened += (sender, args) => _JobsTimer.Start();
            Activated += (sender, args) =>
            {
                RefreshJobs();
                _JobsTimer.Start();
            };
            PropertyChanged += (sender, args) =>
            {
                if (args.Property == IsVisibleProperty && !IsVisible) _JobsTimer.Stop();
                // The log is not redrawn while hidden or minimized; catch up when it shows again.
                if ((args.Property == IsVisibleProperty || args.Property == WindowStateProperty) && _ActivityDirty) RenderActivityIfShown();
            };
            RefreshJobs();
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
            SetDetail("You disconnected. Connect to take work from the Admiral again.");
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
            MachineText.Text = "This computer: " + _Settings.Name;
            ToolTip.SetTip(MachineText, "Harbor " + _Settings.Name + " (" + _Settings.HarborId + ")");
            ServerText.Text = "Admiral at " + DescribeServer(_Settings.ServerLinkUrl);
            ToolTip.SetTip(ServerText, _Settings.ServerLinkUrl);
        }

        /// <summary>
        /// Identifiers of the jobs running on this Harbor now, linked or not.
        /// </summary>
        /// <returns>Job identifiers.</returns>
        public List<string> LiveJobIds()
        {
            return _JobState.LiveJobIds();
        }

        /// <summary>
        /// The jobs running on this Harbor now (what each is, its runtime, and when it started), linked or not: a job keeps
        /// running while the link is down.
        /// </summary>
        /// <returns>Jobs, oldest first.</returns>
        public List<HarborJobInfo> LiveJobs()
        {
            return _JobState.LiveJobs();
        }

        /// <summary>
        /// The most recent activity log lines in full detail (request IDs and full paths), oldest first, whichever view
        /// the window shows.
        /// </summary>
        /// <param name="maxLines">Maximum number of lines.</param>
        /// <returns>Copy of the lines.</returns>
        public List<string> RecentActivity(int maxLines)
        {
            if (Dispatcher.UIThread.CheckAccess()) DrainActivity();
            return _ActivityLog.Recent(maxLines, true);
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
            // Bring Harbor forward; otherwise the app that launched it (a terminal) stays active and keeps the menu bar.
            MacActivationPolicy.Activate();
            Activate();
            ReportPathImport();
            // Connect automatically on startup so an operator does not have to click Connect; the link loop
            // reconnects on its own after transient drops.
            StartConnecting(true);
        }

        private void OnConnectToggleClick(object? sender, RoutedEventArgs e)
        {
            if (IsLinkRunning) Disconnect();
            else Connect();
        }

        private void UpdateConnectButton()
        {
            ConnectButton.Content = IsLinkRunning ? "Disconnect" : "Connect";
            ToolTip.SetTip(ConnectButton, IsLinkRunning
                ? "Stop taking work from the Admiral (jobs already running keep running)"
                : "Link this computer to the Admiral and take work from it");
        }

        private void RefreshJobs()
        {
            List<HarborJobInfo> jobs = LiveJobs();
            JobsList.Update(jobs, DateTime.UtcNow, _Settings.DashboardUrl);
            JobsCountText.Text = jobs.Count + " of " + _Settings.MaxConcurrentJobs + " slots in use";
        }

        private void StartConnecting(bool automatic)
        {
            if (_RunCts != null) return;
            _RunCts = new CancellationTokenSource();
            UpdateConnectButton();
            AppendInfo(automatic ? "Auto-connecting on startup" : "Connect requested", HarborLogCategoryEnum.Link, HarborLogOutcomeEnum.None);
            // The link loop prepares its job runner before it reports Connecting; show Connecting from the click so the
            // header never reads Disconnected beside a Disconnect button.
            SetLinkState(HarborLinkStateEnum.Connecting);
            SetDetail("Reaching the Admiral at " + _Settings.ServerLinkUrl + "...");
            _ = RunLoopAsync(_RunCts.Token);
            RaiseStateChanged();
        }

        private void StopConnecting()
        {
            _RunCts?.Cancel();
            _RunCts = null;
            UpdateConnectButton();
            SetLinkState(HarborLinkStateEnum.Idle);
            // The link loop stops without reporting, so clear what it last said ("Ready", a retry countdown).
            SetDetail("Not connected. Connect to start taking work from the Admiral.");
            RaiseStateChanged();
        }

        private void OpenJobLink(string link)
        {
            if (!PlatformShell.Open(link, out string? error)) SetDetail("Could not open the dashboard: " + (error ?? "unknown error"));
        }

        private void OnOpenDashboardClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_Settings.DashboardUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetDetail("Could not open the dashboard: " + ex.Message);
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
            RunMenuCommand(HarborMenuCommandEnum.Logs);
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
                DrainActivity();
                if (top?.Clipboard != null)
                    await top.Clipboard.SetTextAsync(_ActivityLog.ToText());
            }
            catch
            {
            }
        }

        private void OnClearLogClick(object? sender, RoutedEventArgs e)
        {
            _PendingLog.Clear();
            _ActivityLog.Clear();
            _ActivityDirty = false;
            LogBox.Text = string.Empty;
        }

        private void OnShowDetailsChanged(object? sender, RoutedEventArgs e)
        {
            if (_LoadingShowDetails) return;
            bool show = ShowDetailsBox.IsChecked == true;
            _ActivityLog.ShowDetails = show;
            DrainActivity();
            RenderActivity();
            if (_Settings.ShowActivityDetails == show) return;
            _Settings.ShowActivityDetails = show;
            // Remembered across restarts; a failed save only loses the preference.
            if (!_Settings.TrySave(out string? error))
                _Logging.Warn("[Harbor] could not save the Show details preference: " + error);
        }

        private void RenderActivityIfShown()
        {
            // Laying out the whole log box is the expensive part; skip it while nobody can see it.
            if (!IsVisible || WindowState == WindowState.Minimized)
            {
                _ActivityDirty = true;
                return;
            }

            RenderActivity();
        }

        private void RenderActivity()
        {
            _ActivityDirty = false;
            string text = _ActivityLog.ToText();
            LogBox.Text = text;
            // Follow the newest line from its start: a caret at the very end also scrolls a long last line
            // sideways, which cut the start off every line in view.
            LogBox.CaretIndex = text.LastIndexOf('\n') + 1;
        }

        private Task RunLoopAsync(CancellationToken token)
        {
            return _Host.RunAsync(token);
        }

        private void ShowRuntimes(List<HarborCapability> runtimes)
        {
            List<string> available = new List<string>();
            List<string> missing = new List<string>();
            List<string> details = new List<string>();
            foreach (HarborCapability runtime in runtimes)
            {
                string? command = HarborRuntimeDetector.CommandFor(Enum.Parse<Armada.Core.Enums.AgentRuntimeEnum>(runtime.Name));
                if (command == null) continue;
                if (runtime.Available) available.Add(runtime.Name);
                else missing.Add(runtime.Name + " (" + command + ")");
                details.Add(runtime.Name + ": " + (runtime.Detail ?? (runtime.Available ? "found" : "not found")));
            }

            string summary = "Agents this computer can run: " + (available.Count > 0 ? String.Join(", ", available) : "none") + "."
                + (missing.Count > 0 ? " Not found on PATH: " + String.Join(", ", missing) + "." : "");
            string tip = String.Join(Environment.NewLine, details) + Environment.NewLine + "PATH: " + (Environment.GetEnvironmentVariable("PATH") ?? String.Empty);
            if (!String.Equals(summary, _LastRuntimeSummary, StringComparison.Ordinal))
            {
                _LastRuntimeSummary = summary;
                AppendInfo(summary, HarborLogCategoryEnum.General, missing.Count > 0 ? HarborLogOutcomeEnum.Failed : HarborLogOutcomeEnum.Ok);
            }

            Dispatcher.UIThread.Post(() =>
            {
                RuntimesText.Text = summary;
                ToolTip.SetTip(RuntimesText, tip);
            });
        }

        private void ReportPathImport()
        {
            HarborPathImportResult? import = Program.PathImport;
            if (import == null || import.Shell == null) return;
            if (import.Added.Count > 0)
                AppendInfo("Added to PATH from " + (import.LoginShellRead ? "the login shell (" + import.Shell + ")" : "the usual tool folders") + ": " + String.Join(", ", import.Added));
            if (!import.LoginShellRead && import.Error != null)
                AppendInfo("Could not read the login shell's PATH: " + import.Error, HarborLogCategoryEnum.General, HarborLogOutcomeEnum.Failed);
        }

        private void AppendInfo(string message)
        {
            AppendInfo(message, HarborLogCategoryEnum.General, HarborLogOutcomeEnum.None);
        }

        private void AppendInfo(string message, HarborLogCategoryEnum category, HarborLogOutcomeEnum outcome)
        {
            AppendLog(new HarborLogEntry(HarborLogDirection.Info, message) { Category = category, Outcome = outcome });
        }

        private void AppendLog(HarborLogEntry entry)
        {
            _Logging.Info("[Harbor] " + (entry.Direction == HarborLogDirection.Info ? "" : entry.Direction + " ") + entry.Message);
            _PendingLog.Enqueue(entry);
            // One drain is scheduled per batch, after a short wait so a burst of entries costs one redraw.
            if (Interlocked.Exchange(ref _LogDrainScheduled, 1) == 1) return;
            Dispatcher.UIThread.Post(() => DispatcherTimer.RunOnce(() =>
            {
                Interlocked.Exchange(ref _LogDrainScheduled, 0);
                DrainActivity();
                if (_ActivityDirty) RenderActivityIfShown();
            }, _LogRenderInterval));
        }

        private void DrainActivity()
        {
            // Entries taken here (also by Copy and diagnostics) are shown by the next scheduled redraw.
            while (_PendingLog.TryDequeue(out HarborLogEntry? entry))
            {
                _ActivityLog.Add(entry);
                _ActivityDirty = true;
            }
        }

        /// <summary>
        /// Create Harbor's log: daily files under the Harbor log directory, or no file log when that folder is unusable.
        /// </summary>
        /// <returns>The logging module.</returns>
        internal static LoggingModule CreateLogging()
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
                string brush;
                switch (state)
                {
                    case HarborLinkStateEnum.Connecting:
                        StatusText.Text = "Connecting";
                        brush = "HarborWarningBrush";
                        break;
                    case HarborLinkStateEnum.Connected:
                        StatusText.Text = "Connected";
                        brush = "HarborSuccessBrush";
                        break;
                    case HarborLinkStateEnum.Error:
                        StatusText.Text = "Error";
                        brush = "HarborDangerBrush";
                        break;
                    default:
                        // Idle (the operator disconnected) and Disconnected (the link closed and is retrying).
                        StatusText.Text = "Disconnected";
                        brush = state == HarborLinkStateEnum.Disconnected ? "HarborWarningBrush" : "HarborIdleBrush";
                        break;
                }

                _StatusDotBinding?.Dispose();
                _StatusDotBinding = StatusDot.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, StatusDot.GetResourceObservable(brush));
                if (state != HarborLinkStateEnum.Connected) RefreshJobs();

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
                if (string.Equals(_McpUrl, mcp, StringComparison.Ordinal)) return;
                _McpUrl = mcp;
                RaiseStateChanged();
            });
        }

        private static string DescribeServer(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return uri.Authority;
            return url;
        }

        #endregion
    }
}
