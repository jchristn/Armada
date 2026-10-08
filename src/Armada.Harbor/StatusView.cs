namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Harbor;
    using Armada.Core.Hosting;
    using Armada.Core.Models;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Threading;

    /// <summary>
    /// The Overview tab of the Status window: this computer's Harbor and its link and running jobs, the Admiral's health
    /// and workload (polled while the tab is shown), and how much disk the Armada data directory uses.
    /// </summary>
    public class StatusView : UserControl
    {
        #region Private-Members

        private const int _PollIntervalMs = 5000;

        private readonly HarborSession _Session;
        private readonly SelectableTextBlock _HarborValue;
        private readonly SelectableTextBlock _LinkValue;
        private readonly SelectableTextBlock _McpValue;
        private readonly TextBlock _JobsValue;
        private readonly JobListView _Jobs = new JobListView();
        private readonly SelectableTextBlock _HarborLogValue;
        private readonly SelectableTextBlock _RestValue;
        private readonly SelectableTextBlock _HealthValue;
        private readonly SelectableTextBlock _VersionValue;
        private readonly SelectableTextBlock _UptimeValue;
        private readonly SelectableTextBlock _PortsValue;
        private readonly SelectableTextBlock _CaptainsValue;
        private readonly SelectableTextBlock _MissionsValue;
        private readonly SelectableTextBlock _VoyagesValue;
        private readonly StackPanel _UsagePanel;
        private readonly TextBlock _UsageNote;
        private readonly DispatcherTimer _Timer;
        private bool _Polling = false;
        private CancellationTokenSource? _UsageCts = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Harbor session.</param>
        public StatusView(HarborSession session)
        {
            _Session = session ?? throw new ArgumentNullException(nameof(session));

            Grid harbor = HarborUi.DetailGrid();
            _HarborValue = HarborUi.AddRow(harbor, "Harbor", null);
            _LinkValue = HarborUi.AddRow(harbor, "Connection", null);
            _McpValue = HarborUi.AddRow(harbor, "MCP URL", null);
            _HarborLogValue = HarborUi.AddRow(harbor, "Harbor log", null);
            StackPanel harborBody = new StackPanel { Spacing = 12 };
            harborBody.Children.Add(harbor);
            Grid jobsHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            jobsHeader.Children.Add(new TextBlock { Text = "Running now", FontWeight = Avalonia.Media.FontWeight.SemiBold });
            _JobsValue = HarborUi.Secondary(new TextBlock { FontSize = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            Grid.SetColumn(_JobsValue, 1);
            jobsHeader.Children.Add(_JobsValue);
            harborBody.Children.Add(jobsHeader);
            harborBody.Children.Add(_Jobs);

            Grid admiral = HarborUi.DetailGrid();
            _RestValue = HarborUi.AddRow(admiral, "Address", null);
            _HealthValue = HarborUi.AddRow(admiral, "Health", "Checking...");
            _VersionValue = HarborUi.AddRow(admiral, "Version", null);
            _UptimeValue = HarborUi.AddRow(admiral, "Uptime", null);
            _PortsValue = HarborUi.AddRow(admiral, "Ports", null);
            _CaptainsValue = HarborUi.AddRow(admiral, "Captains", null);
            _MissionsValue = HarborUi.AddRow(admiral, "Missions", null);
            _VoyagesValue = HarborUi.AddRow(admiral, "Active voyages", null);

            StackPanel usageButtons = HarborUi.ButtonRow();
            Button refresh = HarborUi.Button("Measure Again", () => _ = MeasureUsageAsync(), "Measure the size of each item in the data directory");
            usageButtons.Children.Add(refresh);
            usageButtons.Children.Add(HarborUi.Button("Open Folder", OpenDataFolder));
            StackPanel usage = new StackPanel { Spacing = 8 };
            _UsageNote = HarborUi.Note("");
            usage.Children.Add(_UsageNote);
            _UsagePanel = new StackPanel { Spacing = 4 };
            usage.Children.Add(_UsagePanel);
            Content = HarborUi.Page(
                HarborUi.Card("This computer", harborBody, null),
                HarborUi.Card("Admiral server", admiral, null),
                HarborUi.Card("Armada data on this computer", usage, usageButtons));

            _Timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_PollIntervalMs) };
            _Timer.Tick += (sender, args) => _ = PollAdmiralAsync();

            AttachedToVisualTree += (sender, args) =>
            {
                _Session.Changed += OnSessionChanged;
                RefreshLocal();
                _ = PollAdmiralAsync();
                _Timer.Start();
                if (_UsagePanel.Children.Count == 0) _ = MeasureUsageAsync();
            };
            DetachedFromVisualTree += (sender, args) =>
            {
                // Unsubscribe so a closed window is not kept alive (and refreshed) by the session.
                _Session.Changed -= OnSessionChanged;
                _Timer.Stop();
                _UsageCts?.Cancel();
            };
        }

        #endregion

        #region Private-Methods

        private void OnSessionChanged(object? sender, EventArgs e)
        {
            RefreshLocal();
        }

        private void RefreshLocal()
        {
            HarborAppSettings settings = _Session.Settings;
            _HarborValue.Text = settings.Name + "  (" + settings.HarborId + ")";
            _LinkValue.Text = DescribeLink(_Session.Window.LinkState) + " - " + settings.ServerLinkUrl;
            _McpValue.Text = String.IsNullOrEmpty(_Session.Window.McpUrl) ? "-  (sent by the Admiral when connected)" : _Session.Window.McpUrl;
            List<HarborJobInfo> jobs = _Session.Window.LiveJobs();
            _Jobs.Update(jobs, DateTime.UtcNow);
            _JobsValue.Text = jobs.Count + " of " + settings.MaxConcurrentJobs + " slots in use";
            string? harborLog = new HarborLogPaths(HarborAppSettings.LogDirectory()).FindLatestHarborLog();
            _HarborLogValue.Text = harborLog ?? HarborAppSettings.LogDirectory() + " (no log yet)";
            _RestValue.Text = _Session.RestBaseUrl ?? "-  (the Admiral address is not a ws:// or wss:// address)";

            if (_Session.Admiral == null) _UsageNote.Text = "Looking for the Armada data directory...";
            else if (!_Session.Admiral.IsLocal) _UsageNote.Text = _Session.Admiral.Reason;
            else _UsageNote.Text = _Session.Admiral.DataDirectory;
        }

        private static string DescribeLink(HarborLinkStateEnum state)
        {
            switch (state)
            {
                case HarborLinkStateEnum.Connected: return "Connected";
                case HarborLinkStateEnum.Connecting: return "Connecting";
                case HarborLinkStateEnum.Error: return "Error (retrying)";
                case HarborLinkStateEnum.Disconnected: return "Disconnected (retrying)";
                default: return "Disconnected";
            }
        }

        private async Task PollAdmiralAsync()
        {
            if (_Polling) return;
            _Polling = true;
            try
            {
                RefreshLocal();
                using (ArmadaClient? client = _Session.CreateClient(4000))
                {
                    if (client == null)
                    {
                        _HealthValue.Text = "Unknown: the Admiral address is not a ws:// or wss:// address";
                        ClearAdmiral();
                        return;
                    }

                    HealthResult? health = null;
                    try
                    {
                        health = await client.GetHealthAsync().ConfigureAwait(true);
                    }
                    catch (ArmadaApiException ex)
                    {
                        _HealthValue.Text = "Cannot reach the Admiral: " + ex.Message;
                        ClearAdmiral();
                        return;
                    }

                    _HealthValue.Text = health?.Status ?? "unknown";
                    _VersionValue.Text = health?.Version ?? "-";
                    _UptimeValue.Text = health?.Uptime == null ? "-" : health.Uptime + (health.StartUtc.HasValue ? "  (since " + HarborUi.LocalTime(health.StartUtc.Value) + ")" : "");
                    _PortsValue.Text = DescribePorts(health);

                    try
                    {
                        ArmadaStatus? status = await client.GetStatusAsync().ConfigureAwait(true);
                        if (status == null) return;
                        _CaptainsValue.Text = status.TotalCaptains + " total: " + status.WorkingCaptains + " working, " + status.IdleCaptains + " idle, " + status.StalledCaptains + " stalled";
                        _MissionsValue.Text = status.MissionsByStatus == null || status.MissionsByStatus.Count == 0
                            ? "None"
                            : String.Join(", ", status.MissionsByStatus.Where(p => p.Value > 0).Select(p => p.Key + " " + p.Value));
                        _VoyagesValue.Text = status.ActiveVoyages.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch (ArmadaApiException ex)
                    {
                        string reason = ex.StatusCode == 401 || ex.StatusCode == 403
                            ? (_Session.IsAdmiralLocal
                                ? "Not authorized: this Admiral's settings.json has no API key Harbor can use; set an access key in Settings > General."
                                : "Not authorized: set an access key (an Armada credential) in Settings > General.")
                            : ex.Message;
                        _CaptainsValue.Text = reason;
                        _MissionsValue.Text = "-";
                        _VoyagesValue.Text = "-";
                    }
                }
            }
            finally
            {
                _Polling = false;
            }
        }

        private void ClearAdmiral()
        {
            _VersionValue.Text = "-";
            _UptimeValue.Text = "-";
            _PortsValue.Text = "-";
            _CaptainsValue.Text = "-";
            _MissionsValue.Text = "-";
            _VoyagesValue.Text = "-";
        }

        private static string DescribePorts(HealthResult? health)
        {
            if (health?.Ports == null) return "-";
            return "Admiral " + health.Ports.Admiral + ", MCP " + health.Ports.Mcp;
        }

        private async Task MeasureUsageAsync()
        {
            LocalAdmiralInfo? admiral = _Session.Admiral;
            if (admiral == null || !admiral.IsLocal)
            {
                _UsagePanel.Children.Clear();
                return;
            }

            _UsageCts?.Cancel();
            CancellationTokenSource cts = new CancellationTokenSource();
            _UsageCts = cts;
            _UsageNote.Text = "Measuring " + admiral.DataDirectory + "...";

            List<DirectoryUsageEntry> entries;
            try
            {
                string root = admiral.DataDirectory;
                entries = await Task.Run(() => DirectoryUsage.Measure(root, cts.Token)).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _UsageNote.Text = "Could not measure " + admiral.DataDirectory + ": " + ex.Message;
                return;
            }

            long total = entries.Sum(e => e.SizeBytes);
            _UsageNote.Text = admiral.DataDirectory + "  -  " + DirectoryUsage.FormatBytes(total) + " in total";
            _UsagePanel.Children.Clear();

            Grid grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,Auto,Auto,Auto"), RowSpacing = 4, ColumnSpacing = 18 };
            AddUsageRow(grid, "Name", "Size", "Files", "Modified", true);
            foreach (DirectoryUsageEntry entry in entries)
            {
                AddUsageRow(grid,
                    entry.Name + (entry.IsDirectory ? "/" : ""),
                    DirectoryUsage.FormatBytes(entry.SizeBytes),
                    entry.FileCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
                    HarborUi.LocalTime(entry.LastWriteUtc),
                    false);
            }

            _UsagePanel.Children.Add(grid);
        }

        private static void AddUsageRow(Grid grid, string name, string size, string files, string modified, bool header)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            string[] cells = new string[] { name, size, files, modified };
            for (int i = 0; i < cells.Length; i++)
            {
                TextBlock cell = new TextBlock { Text = cells[i] };
                if (header) cell.FontWeight = Avalonia.Media.FontWeight.SemiBold;
                else if (i > 0) HarborUi.Secondary(cell);
                if (i == 1 || i == 2) cell.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }
        }

        private void OpenDataFolder()
        {
            LocalAdmiralInfo? admiral = _Session.Admiral;
            if (admiral == null || !admiral.IsLocal) return;
            if (!PlatformShell.Open(admiral.DataDirectory, out string? error))
                _ = HarborDialog.ShowMessageAsync(TopLevel.GetTopLevel(this) as Window, "Could not open the folder", error ?? "Unknown error");
        }

        #endregion
    }
}
