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
    /// The Admiral tab of the Settings window: the Admiral's own settings (not this computer's). Live applies the common
    /// values through the REST API, so they take effect at once and the Admiral rewrites its settings.json. File edits
    /// settings.json directly (every value, including nested sections) on this machine; the Admiral reads it at startup,
    /// so those edits need a restart, which this tab can request. Restarting requires administrator privileges: the tab
    /// asks the Admiral (GET /api/v1/whoami) whether Harbor's credential is a global administrator and disables Restart
    /// Admiral, with the reason, when it is not; the Admiral enforces the same requirement on the restart request.
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
        private readonly List<Button> _RestartButtons = new List<Button>();
        private readonly TextBlock _PrivilegeNote = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        private AdminPrivilegeStatus _Privilege = new AdminPrivilegeStatus { Explanation = "Checking whether Harbor's credential is an administrator..." };
        private bool _CheckingPrivilege = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Harbor session.</param>
        public ArmadaSettingsView(HarborSession session)
        {
            _Session = session ?? throw new ArgumentNullException(nameof(session));

            _Tabs.Items.Add(new TabItem { Header = new TextBlock { Text = "Change live", FontSize = 14 }, Content = BuildLiveTab() });
            _FileTab = new TabItem { Header = new TextBlock { Text = "Edit file (settings.json)", FontSize = 14 } };
            _Tabs.Items.Add(_FileTab);

            TextBlock explanation = new TextBlock
            {
                Text = "These are the Admiral server's settings, not this computer's. Change live sends them through the Admiral's "
                    + "API: they take effect at once and the Admiral saves them to its settings.json. When the Admiral runs on this "
                    + "computer you can also edit that settings.json file directly; the Admiral reads it when it starts."
            };
            Border notice = HarborUi.Notice(explanation);
            notice.Margin = new Thickness(20, 16, 20, 4);
            DockPanel root = new DockPanel();
            DockPanel.SetDock(notice, Dock.Top);
            root.Children.Add(notice);
            _Tabs.Margin = new Thickness(8, 0, 0, 0);
            root.Children.Add(_Tabs);
            Content = root;

            AttachedToVisualTree += (sender, args) =>
            {
                _Session.Changed += OnSessionChanged;
                RefreshFileTab();
                if (_Loaded == null) _ = LoadLiveAsync();
                _ = RefreshPrivilegeAsync();
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
            StackPanel intro = new StackPanel { Spacing = 6 };
            intro.Children.Add(HarborUi.Note("Port changes take effect after the Admiral restarts. Nested sections (retention, permissions, push, remote control) are on Edit file."));
            intro.Children.Add(HarborUi.Secondary(_LiveState));

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
            StackPanel buttons = HarborUi.ButtonRow();
            Button apply = HarborUi.Button("Apply", () => _ = ApplyLiveAsync());
            apply.Classes.Add("accent");
            buttons.Children.Add(apply);
            buttons.Children.Add(HarborUi.Button("Reload", () => _ = LoadLiveAsync(), "Read the values from the Admiral again"));
            buttons.Children.Add(RestartButton());
            StackPanel footer = new StackPanel { Spacing = 8 };
            footer.Children.Add(buttons);
            footer.Children.Add(HarborUi.Secondary(_PrivilegeNote));
            footer.Children.Add(_LiveMessage);

            return HarborUi.Page(intro, HarborUi.Card("Admiral settings", form, null), footer);
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
                    _LiveState.Text = "The Admiral address in General is not a ws:// or wss:// address; fix it there first.";
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
                _FileTab.Content = HarborUi.Page(HarborUi.Note("Looking for the Armada data directory..."));
                return;
            }

            if (!admiral.IsLocal)
            {
                _FileTab.Content = HarborUi.Page(HarborUi.Note(admiral.Reason + " Change live works over the network."));
                _FileEditor = null;
                _FileEditorPath = null;
                return;
            }

            if (_FileEditor != null && String.Equals(_FileEditorPath, admiral.SettingsFile, StringComparison.Ordinal)) return;

            StackPanel header = new StackPanel { Spacing = 6 };
            header.Children.Add(HarborUi.Note(admiral.SettingsFile));
            _FileBanner.Text = "The Admiral reads this file when it starts: saved changes take effect after a restart. Until then, a settings change from the dashboard or Change live rewrites this file from the running settings and discards edits made here.";
            Grid bannerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
            bannerRow.Children.Add(HarborUi.Notice(_FileBanner));
            Button restart = RestartButton();
            restart.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(restart, 1);
            bannerRow.Children.Add(restart);
            header.Children.Add(bannerRow);

            _FileEditor = new JsonFileEditorView(admiral.SettingsFile, SettingsTextValidator.ValidateArmadaSettings, header);
            _FileEditor.Margin = new Thickness(20, 16, 20, 20);
            _FileEditor.Saved += (sender, args) =>
            {
                _ = _Session.ResolveAdmiralAsync();
                _ = OfferRestartAsync();
            };
            _FileEditorPath = admiral.SettingsFile;
            _FileTab.Content = _FileEditor;
        }

        private Button RestartButton()
        {
            Button button = HarborUi.Button("Restart Admiral", () => _ = RestartAdmiralAsync());
            _RestartButtons.Add(button);
            ApplyPrivilege();
            return button;
        }

        /// <summary>
        /// Ask the Admiral who Harbor's credential is, and enable Restart Admiral only for a global administrator.
        /// </summary>
        private async Task<AdminPrivilegeStatus> RefreshPrivilegeAsync()
        {
            if (_CheckingPrivilege) return _Privilege;
            _CheckingPrivilege = true;
            try
            {
                using (ArmadaClient? client = _Session.CreateClient(5000))
                {
                    if (client == null)
                    {
                        _Privilege = new AdminPrivilegeStatus { State = AdminPrivilegeStateEnum.Unknown, Explanation = "The Admiral address in General is not a ws:// or wss:// address." };
                    }
                    else
                    {
                        try
                        {
                            _Privilege = AdminPrivilegeCheck.FromWhoAmI(await client.WhoamiAsync().ConfigureAwait(true));
                        }
                        catch (ArmadaApiException ex)
                        {
                            _Privilege = AdminPrivilegeCheck.FromError(ex.StatusCode, ex.Message);
                        }
                    }
                }
            }
            finally
            {
                _CheckingPrivilege = false;
            }

            ApplyPrivilege();
            return _Privilege;
        }

        private void ApplyPrivilege()
        {
            string tip = _Privilege.IsAdmin
                ? "Restart the Admiral (requires administrator privileges). " + _Privilege.Explanation
                : "Requires administrator privileges. " + _Privilege.Explanation;
            foreach (Button button in _RestartButtons)
            {
                button.IsEnabled = _Privilege.IsAdmin;
                ToolTip.SetTip(button, tip);
                // A disabled button shows no tooltip, so the reason is also written under the buttons.
                ToolTip.SetShowOnDisabled(button, true);
            }

            _PrivilegeNote.Text = _Privilege.IsAdmin ? String.Empty : "Restart Admiral requires administrator privileges. " + _Privilege.Explanation;
            _PrivilegeNote.IsVisible = !_Privilege.IsAdmin;
        }

        private async Task OfferRestartAsync()
        {
            // Offered after saving settings.json; only an administrator can act on it.
            AdminPrivilegeStatus privilege = await RefreshPrivilegeAsync().ConfigureAwait(true);
            if (!privilege.IsAdmin)
            {
                SetMessage(_LiveMessage, "Saved. The Admiral reads settings.json when it starts; restarting it requires administrator privileges. " + privilege.Explanation, false);
                return;
            }

            await RestartAdmiralAsync("The Admiral reads settings.json when it starts. Restart it now to apply the saved changes?").ConfigureAwait(true);
        }

        private Task RestartAdmiralAsync()
        {
            return RestartAdmiralAsync("Restart the Admiral at " + (_Session.RestBaseUrl ?? "?") + "?");
        }

        private async Task RestartAdmiralAsync(string question)
        {
            Window? owner = TopLevel.GetTopLevel(this) as Window;
            bool restart = await HarborDialog.ConfirmAsync(owner, "Restart the Admiral?",
                RestartConfirmMessage(question, _Privilege),
                "Restart").ConfigureAwait(true);
            if (!restart) return;

            // Confirm with the Admiral again just before restarting: the credential or the user may have changed.
            AdminPrivilegeStatus privilege = await RefreshPrivilegeAsync().ConfigureAwait(true);
            if (!privilege.IsAdmin)
            {
                await HarborDialog.ShowMessageAsync(owner, "Administrator privileges required", "This operation requires administrator privileges. " + privilege.Explanation).ConfigureAwait(true);
                return;
            }

            await RequestRestartAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// The restart confirmation text.
        /// </summary>
        /// <param name="question">What is asked.</param>
        /// <param name="privilege">Who Harbor is signed in as.</param>
        /// <returns>Text.</returns>
        public static string RestartConfirmMessage(string question, AdminPrivilegeStatus privilege)
        {
            return "This operation requires administrator privileges. " + (privilege?.Explanation ?? String.Empty) + "\n\n"
                + question + " It is unavailable for a few seconds while it restarts, and the link reconnects when it is back.";
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
                    ? "Not authorized. Harbor uses the API key from this Admiral's settings.json, or its own access key; neither was accepted. Set an admin access key in Settings > General."
                    : "Not authorized. Set an admin access key (an Armada credential) in Settings > General.";
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
            HarborUi.SetMessage(block, text, isError);
        }

        #endregion
    }
}
