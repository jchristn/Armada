namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Avalonia.Controls;
    using Avalonia.Interactivity;
    using Avalonia.Threading;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// The Harbor status window. Framework code-behind (partial class as Avalonia requires). Owns the
    /// reconnecting link loop: it dials the Admiral, runs a link session, and retries with a short backoff
    /// when the link drops, surfacing status to the operator.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Private-Members

        private readonly HarborAppSettings _Settings;
        private readonly LoggingModule _Logging;
        private CancellationTokenSource? _RunCts;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            _Settings = HarborAppSettings.Load();
            _Settings.Save();
            _Logging = new LoggingModule();
            _Logging.Settings.EnableConsole = false;

            HarborNameText.Text = _Settings.Name + "  (" + _Settings.HarborId + ")";
            ServerText.Text = _Settings.ServerLinkUrl;
            Closing += (sender, args) => _RunCts?.Cancel();
        }

        #endregion

        #region Private-Methods

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
            SetStatus("Idle");
        }

        private void OnOpenDashboardClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_Settings.DashboardUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStatus("Could not open dashboard: " + ex.Message);
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
                        SetStatus("Connecting...");
                        await client.RunSessionAsync(transport, token).ConfigureAwait(false);
                        SetStatus("Disconnected");
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        SetStatus("Disconnected: " + ex.Message);
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

        private void SetStatus(string status)
        {
            Dispatcher.UIThread.Post(() => StatusText.Text = status);
        }

        private void SetMcp(string? mcp)
        {
            Dispatcher.UIThread.Post(() => McpText.Text = string.IsNullOrEmpty(mcp) ? "-" : mcp);
        }

        #endregion
    }
}
