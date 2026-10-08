namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Hosting;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;

    /// <summary>
    /// The Armada tab: the Admiral's settings. Live applies the common values through the REST API, so they take
    /// effect at once and the Admiral rewrites its settings.json. File edits settings.json directly (every value,
    /// including nested sections) on this machine; the Admiral reads it at startup, so those edits need a restart,
    /// which this tab can request.
    /// </summary>
    public class ArmadaSettingsView : UserControl
    {
        #region Private-Members

        private readonly HarborSession _Session;
        private readonly TabControl _Tabs = new TabControl();
        private readonly TabItem _FileTab;
        private readonly TextBlock _LiveState = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _LiveMessage = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _FileBanner = new TextBlock();
        private readonly NumericUpDown _MaxCaptains = Number(0, 1000);
        private readonly NumericUpDown _HeartbeatSeconds = Number(5, 3600);
        private readonly NumericUpDown _StallMinutes = Number(1, 1440);
        private readonly NumericUpDown _IdleCaptainSeconds = Number(0, 86400);
        private readonly NumericUpDown _PlanningInactivityMinutes = Number(0, 10080);
        private readonly NumericUpDown _PlanningAbandonMinutes = Number(0, 43200);
        private readonly NumericUpDown _PlanningRetentionDays = Number(0, 3650);
        private readonly ComboBox _LandingMode = new ComboBox { ItemsSource = Enum.GetNames(typeof(LandingModeEnum)), MinWidth = 160 };
        private readonly NumericUpDown _AdmiralPort = Number(1, 65535);
        private readonly NumericUpDown _McpPort = Number(1, 65535);
        private readonly List<Control> _LiveInputs = new List<Control>();
        private SettingsData? _Loaded = null;
        private JsonFileEditorView? _FileEditor = null;
        private string? _FileEditorPath = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Harbor session.</param>
        public ArmadaSettingsView(HarborSession session)
        {
            _Session = session ?? throw new ArgumentNullException(nameof(session));

            _Tabs.Items.Add(new TabItem { Header = new TextBlock { Text = "Live", FontSize = 14 }, Content = BuildLiveTab() });
            _FileTab = new TabItem { Header = new TextBlock { Text = "File (settings.json)", FontSize = 14 } };
            _Tabs.Items.Add(_FileTab);
            Content = _Tabs;

            AttachedToVisualTree += (sender, args) =>
            {
                _Session.Changed += OnSessionChanged;
                RefreshFileTab();
                if (_Loaded == null) _ = LoadLiveAsync();
            };
            DetachedFromVisualTree += (sender, args) => _Session.Changed -= OnSessionChanged;
        }

        #endregion

        #region Private-Methods

        private void OnSessionChanged(object? sender, EventArgs e)
        {
            RefreshFileTab();
        }

        private Control BuildLiveTab()
        {
            StackPanel root = new StackPanel { Spacing = 10, Margin = new Thickness(4, 8, 12, 12) };
            root.Children.Add(HarborUi.Note("These values are read from and applied to the running Admiral through its API: they take effect immediately and the Admiral saves them to its settings.json. Port changes take effect after a restart. Nested sections (retention, permissions, push, remote control) are on the File tab."));
            root.Children.Add(HarborUi.Secondary(_LiveState));

            Grid form = new Grid { ColumnDefinitions = new ColumnDefinitions("310,*"), RowSpacing = 8 };
            AddField(form, "Max captains", _MaxCaptains, "0 means no limit.");
            AddField(form, "Heartbeat interval (seconds)", _HeartbeatSeconds, "At least 5.");
            AddField(form, "Stall threshold (minutes)", _StallMinutes, "A captain with no progress this long is stalled.");
            AddField(form, "Idle captain timeout (seconds)", _IdleCaptainSeconds, "0 keeps idle captains indefinitely.");
            AddField(form, "Planning inactivity timeout (minutes)", _PlanningInactivityMinutes, "0 never stops an inactive planning session.");
            AddField(form, "Planning abandonment timeout (minutes)", _PlanningAbandonMinutes, "0 turns abandonment cleanup off.");
            AddField(form, "Planning session retention (days)", _PlanningRetentionDays, "0 keeps stopped and failed sessions.");
            AddField(form, "Landing mode", _LandingMode, "Default for completed missions; vessels and voyages can override it.");
            AddField(form, "Admiral port", _AdmiralPort, "Takes effect after a restart; Harbor's link URL must follow it.");
            AddField(form, "MCP port", _McpPort, "Takes effect after a restart.");
            root.Children.Add(form);

            StackPanel buttons = HarborUi.ButtonRow();
            Button apply = HarborUi.Button("Apply", () => _ = ApplyLiveAsync());
            apply.Classes.Add("accent");
            buttons.Children.Add(apply);
            buttons.Children.Add(HarborUi.Button("Reload", () => _ = LoadLiveAsync(), "Read the values from the Admiral again"));
            buttons.Children.Add(HarborUi.Button("Restart Admiral", () => _ = RestartAdmiralAsync()));
            root.Children.Add(buttons);
            root.Children.Add(_LiveMessage);

            return new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        }

        private void AddField(Grid form, string label, Control input, string? help)
        {
            _LiveInputs.Add(input);
            int row = form.RowDefinitions.Count;
            form.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            TextBlock labelBlock = new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(labelBlock, row);
            form.Children.Add(labelBlock);

            // Help sits to the right of the input (the inputs are narrow), one row per setting.
            Grid cell = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), ColumnSpacing = 14 };
            input.HorizontalAlignment = HorizontalAlignment.Stretch;
            cell.Children.Add(input);
            if (!String.IsNullOrEmpty(help))
            {
                TextBlock helpBlock = HarborUi.Help(help!);
                helpBlock.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(helpBlock, 1);
                cell.Children.Add(helpBlock);
            }

            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, 1);
            form.Children.Add(cell);
        }

        private static NumericUpDown Number(int min, int max)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Increment = 1, FormatString = "0", MinWidth = 160 };
        }

        private async Task LoadLiveAsync()
        {
            SetLiveEnabled(false);
            _LiveState.Text = "Reading settings from " + (_Session.RestBaseUrl ?? "the Admiral") + "...";
            SetMessage(_LiveMessage, null, false);

            using (ArmadaClient? client = _Session.CreateClient())
            {
                if (client == null)
                {
                    _LiveState.Text = "Harbor's link URL has no REST equivalent; fix it in Harbor Settings.";
                    return;
                }

                try
                {
                    SettingsData? data = await client.GetSettingsAsync().ConfigureAwait(true);
                    if (data == null)
                    {
                        _LiveState.Text = "The Admiral returned no settings.";
                        return;
                    }

                    _Loaded = data;
                    Show(data);
                    SetLiveEnabled(true);
                    _LiveState.Text = "Connected to " + _Session.RestBaseUrl + ". Values shown are the Admiral's current settings.";
                }
                catch (ArmadaApiException ex)
                {
                    _LiveState.Text = DescribeApiError(ex);
                }
            }
        }

        private void Show(SettingsData data)
        {
            _MaxCaptains.Value = data.MaxCaptains;
            _HeartbeatSeconds.Value = data.HeartbeatIntervalSeconds;
            _StallMinutes.Value = data.StallThresholdMinutes;
            _IdleCaptainSeconds.Value = data.IdleCaptainTimeoutSeconds;
            _PlanningInactivityMinutes.Value = data.PlanningSessionInactivityTimeoutMinutes;
            _PlanningAbandonMinutes.Value = data.PlanningSessionAbandonmentTimeoutMinutes;
            _PlanningRetentionDays.Value = data.PlanningSessionRetentionDays;
            _LandingMode.SelectedItem = data.LandingMode?.ToString();
            _AdmiralPort.Value = data.AdmiralPort;
            _McpPort.Value = data.McpPort;
        }

        private async Task ApplyLiveAsync()
        {
            if (_Loaded == null) return;

            // Send only what changed: the API applies each present value and leaves the rest alone.
            SettingsData changes = new SettingsData();
            int count = 0;
            int? value;
            if ((value = Changed(_MaxCaptains, _Loaded.MaxCaptains)).HasValue) { changes.MaxCaptains = value; count++; }
            if ((value = Changed(_HeartbeatSeconds, _Loaded.HeartbeatIntervalSeconds)).HasValue) { changes.HeartbeatIntervalSeconds = value; count++; }
            if ((value = Changed(_StallMinutes, _Loaded.StallThresholdMinutes)).HasValue) { changes.StallThresholdMinutes = value; count++; }
            if ((value = Changed(_IdleCaptainSeconds, _Loaded.IdleCaptainTimeoutSeconds)).HasValue) { changes.IdleCaptainTimeoutSeconds = value; count++; }
            if ((value = Changed(_PlanningInactivityMinutes, _Loaded.PlanningSessionInactivityTimeoutMinutes)).HasValue) { changes.PlanningSessionInactivityTimeoutMinutes = value; count++; }
            if ((value = Changed(_PlanningAbandonMinutes, _Loaded.PlanningSessionAbandonmentTimeoutMinutes)).HasValue) { changes.PlanningSessionAbandonmentTimeoutMinutes = value; count++; }
            if ((value = Changed(_PlanningRetentionDays, _Loaded.PlanningSessionRetentionDays)).HasValue) { changes.PlanningSessionRetentionDays = value; count++; }
            bool portChanged = false;
            if ((value = Changed(_AdmiralPort, _Loaded.AdmiralPort)).HasValue) { changes.AdmiralPort = value; count++; portChanged = true; }
            if ((value = Changed(_McpPort, _Loaded.McpPort)).HasValue) { changes.McpPort = value; count++; portChanged = true; }
            if (_LandingMode.SelectedItem is string landing
                && Enum.TryParse(landing, out LandingModeEnum mode)
                && (!_Loaded.LandingMode.HasValue || _Loaded.LandingMode.Value != mode))
            {
                changes.LandingMode = mode;
                count++;
            }

            if (count == 0)
            {
                SetMessage(_LiveMessage, "Nothing changed.", false);
                return;
            }

            using (ArmadaClient? client = _Session.CreateClient())
            {
                if (client == null) return;
                try
                {
                    SettingsData? updated = await client.UpdateSettingsAsync(changes).ConfigureAwait(true);
                    if (updated != null)
                    {
                        _Loaded = updated;
                        Show(updated);
                    }

                    SetMessage(_LiveMessage, "Applied " + count + (count == 1 ? " change" : " changes") + "."
                        + (portChanged ? " Port changes take effect after the Admiral restarts." : ""), false);
                    _FileEditor?.Reload();
                }
                catch (ArmadaApiException ex)
                {
                    SetMessage(_LiveMessage, DescribeApiError(ex), true);
                }
            }
        }

        private static int? Changed(NumericUpDown input, int? original)
        {
            if (!input.Value.HasValue) return null;
            int current = (int)input.Value.Value;
            return original.HasValue && original.Value == current ? (int?)null : current;
        }

        private void RefreshFileTab()
        {
            LocalAdmiralInfo? admiral = _Session.Admiral;
            if (admiral == null)
            {
                _FileTab.Content = HarborUi.Note("Looking for the Armada data directory...");
                return;
            }

            if (!admiral.IsLocal)
            {
                _FileTab.Content = new StackPanel
                {
                    Margin = new Thickness(4, 8),
                    Children = { HarborUi.Note(admiral.Reason + " Use the Live tab or the dashboard to change its settings.") }
                };
                _FileEditor = null;
                _FileEditorPath = null;
                return;
            }

            if (_FileEditor != null && String.Equals(_FileEditorPath, admiral.SettingsFile, StringComparison.Ordinal)) return;

            StackPanel header = new StackPanel { Spacing = 6 };
            header.Children.Add(HarborUi.Note(admiral.SettingsFile));
            _FileBanner.Text = "The Admiral reads this file when it starts: saved changes take effect after a restart. Until then, a settings change from the dashboard or the Live tab rewrites this file from the running settings and discards edits made here.";
            Grid bannerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
            bannerRow.Children.Add(HarborUi.Notice(_FileBanner));
            Button restart = HarborUi.Button("Restart Admiral", () => _ = RestartAdmiralAsync());
            restart.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(restart, 1);
            bannerRow.Children.Add(restart);
            header.Children.Add(bannerRow);

            _FileEditor = new JsonFileEditorView(admiral.SettingsFile, SettingsTextValidator.ValidateArmadaSettings, header);
            _FileEditor.Saved += (sender, args) =>
            {
                _ = _Session.ResolveAdmiralAsync();
                _ = OfferRestartAsync();
            };
            _FileEditorPath = admiral.SettingsFile;
            _FileTab.Content = _FileEditor;
        }

        private async Task OfferRestartAsync()
        {
            bool restart = await HarborDialog.ConfirmAsync(TopLevel.GetTopLevel(this) as Window, "Restart the Admiral?",
                "The Admiral reads settings.json when it starts. Restart it now to apply the saved changes? It is unavailable for a few seconds while it restarts.",
                "Restart").ConfigureAwait(true);
            if (restart) await RequestRestartAsync().ConfigureAwait(true);
        }

        private async Task RestartAdmiralAsync()
        {
            bool restart = await HarborDialog.ConfirmAsync(TopLevel.GetTopLevel(this) as Window, "Restart the Admiral?",
                "Restart the Admiral at " + (_Session.RestBaseUrl ?? "?") + "? It is unavailable for a few seconds while it restarts.",
                "Restart").ConfigureAwait(true);
            if (restart) await RequestRestartAsync().ConfigureAwait(true);
        }

        private async Task RequestRestartAsync()
        {
            using (ArmadaClient? client = _Session.CreateClient())
            {
                if (client == null) return;
                try
                {
                    await client.RestartServerAsync().ConfigureAwait(true);
                    _Session.Window.ReportActivity("Admiral restart requested");
                    SetMessage(_LiveMessage, "Restart requested. The link reconnects when the Admiral is back.", false);
                    _Loaded = null;
                }
                catch (ArmadaApiException ex)
                {
                    await HarborDialog.ShowMessageAsync(TopLevel.GetTopLevel(this) as Window, "Could not restart the Admiral", DescribeApiError(ex)).ConfigureAwait(true);
                }
            }
        }

        private string DescribeApiError(ArmadaApiException ex)
        {
            if (ex.StatusCode == 401 || ex.StatusCode == 403)
            {
                return _Session.IsAdmiralLocal
                    ? "Not authorized. Harbor uses the API key from this Admiral's settings.json, or its own access key; neither was accepted. Set an admin access key in Harbor Settings."
                    : "Not authorized. Set an admin access key (an Armada credential) in Harbor Settings.";
            }

            if (ex.StatusCode == 0) return "Could not reach the Admiral at " + _Session.RestBaseUrl + ": " + ex.Message;
            return "The Admiral answered " + ex.StatusCode + ": " + ex.Message;
        }

        private void SetLiveEnabled(bool enabled)
        {
            foreach (Control input in _LiveInputs) input.IsEnabled = enabled;
        }

        private static void SetMessage(TextBlock block, string? text, bool isError)
        {
            block.Text = text ?? String.Empty;
            block.IsVisible = !String.IsNullOrEmpty(text);
            if (isError) block.Foreground = new SolidColorBrush(Color.Parse("#ef4444"));
            else HarborUi.Secondary(block);
        }

        #endregion
    }
}
