namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net.WebSockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Avalonia.Controls;
    using Avalonia.Controls.Shapes;
    using Avalonia.Interactivity;
    using Avalonia.Media;
    using Avalonia.Threading;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// The Harbor status window. Framework code-behind (partial class as Avalonia requires). Owns the
    /// reconnecting link loop and surfaces status and full connection details to the operator. Closing the
    /// window hides it to the tray rather than stopping the runner.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Private-Members

        private static readonly IBrush _Green = new SolidColorBrush(Color.Parse("#22c55e"));
        private static readonly IBrush _Amber = new SolidColorBrush(Color.Parse("#f59e0b"));
        private static readonly IBrush _Red = new SolidColorBrush(Color.Parse("#ef4444"));
        private static readonly IBrush _Gray = new SolidColorBrush(Color.Parse("#9ca3af"));

        private readonly HarborAppSettings _Settings;
        private readonly LoggingModule _Logging;
        private CancellationTokenSource? _RunCts;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with the shared application settings.
        /// </summary>
        /// <param name="settings">Harbor application settings.</param>
        public MainWindow(HarborAppSettings settings)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            InitializeComponent();

            _Logging = new LoggingModule();
            _Logging.Settings.EnableConsole = false;

            HarborNameText.Text = _Settings.Name + "  (" + _Settings.HarborId + ")";
            ServerText.Text = _Settings.ServerLinkUrl;
            SetStatus("Idle", _Gray);

            Closing += OnWindowClosing;
        }

        #endregion

        #region Private-Methods

        private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
        {
            // Keep the runner alive in the tray instead of exiting; the tray "Quit" item shuts the app down.
            e.Cancel = true;
            Hide();
        }

        private void OnConnectClick(object? sender, RoutedEventArgs e)
        {
            if (_RunCts != null) return;
            _RunCts = new CancellationTokenSource();
            ConnectButton.IsEnabled = false;
            DisconnectButton.IsEnabled = true;
            _ = RunLoopAsync(_RunCts.Token);
        }

        private void OnDisconnectClick(object? sender, RoutedEventArgs e)
        {
            _RunCts?.Cancel();
            _RunCts = null;
            ConnectButton.IsEnabled = true;
            DisconnectButton.IsEnabled = false;
            SetStatus("Idle", _Gray);
            SetDetail("Disconnected by operator.");
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

        private async Task RunLoopAsync(CancellationToken token)
        {
            LocalHostCommandExecutor executor = new LocalHostCommandExecutor();
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
                        _Settings.HeartbeatIntervalMs);

                    try
                    {
                        SetStatus("Connecting...", _Amber);
                        SetDetail("Dialing " + _Settings.ServerLinkUrl + " ...");
                        await client.RunSessionAsync(transport, token, () =>
                        {
                            SetStatus("Connected", _Green);
                            SetDetail("Linked to " + _Settings.ServerLinkUrl + ". Handshake sent; awaiting work.");
                        }).ConfigureAwait(false);

                        SetStatus("Disconnected", _Red);
                        SetDetail("The link closed. Retrying in 3 seconds...");
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        SetStatus("Disconnected", _Red);
                        SetDetail(DescribeConnectError(ex));
                    }

                    SetMcp(client.McpBaseUrl);
                }

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
                    + "\n\nThe Admiral accepted the connection request but did not complete the Harbor link handshake. "
                    + "The server-side Harbor link endpoint may not be enabled in this Admiral build yet, or the link "
                    + "URL / credentials may be wrong.\n\nLink URL: " + _Settings.ServerLinkUrl
                    + "\nRetrying in 3 seconds...";
            }

            return detail;
        }

        private List<HarborCapability> BuildCapabilities()
        {
            List<HarborCapability> capabilities = new List<HarborCapability>();
            foreach (string name in _Settings.Capabilities)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    capabilities.Add(new HarborCapability { Name = name, Available = true });
            }

            return capabilities;
        }

        private Dictionary<string, string>? BuildHeaders()
        {
            if (string.IsNullOrWhiteSpace(_Settings.AccessKey)) return null;
            return new Dictionary<string, string>
            {
                { "x-access-key", _Settings.AccessKey },
                { "x-secret-key", _Settings.Secret }
            };
        }

        private void SetStatus(string status, IBrush color)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText.Text = status;
                StatusDot.Fill = color;
            });
        }

        private void SetDetail(string detail)
        {
            Dispatcher.UIThread.Post(() => DetailText.Text = detail);
        }

        private void SetMcp(string? mcp)
        {
            Dispatcher.UIThread.Post(() => McpText.Text = string.IsNullOrEmpty(mcp) ? "-" : mcp);
        }

        #endregion
    }
}
