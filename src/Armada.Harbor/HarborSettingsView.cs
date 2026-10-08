namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Core.Hosting;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;

    /// <summary>
    /// The General tab of the Settings window: this computer's Harbor settings (~/.armada-harbor/settings.json) and the
    /// appearance. Saving validates, writes the file atomically with a backup, applies the values, and reconnects the
    /// link when a link setting changed. Restore Previous Version puts back a version an earlier save replaced.
    /// </summary>
    public class HarborSettingsView : UserControl
    {
        #region Private-Members

        private readonly HarborSession _Session;
        private readonly TextBox _Name = new TextBox();
        private readonly SelectableTextBlock _HarborId;
        private readonly TextBox _LinkUrl = new TextBox();
        private readonly TextBox _DashboardUrl = new TextBox();
        private readonly TextBox _TenantId = new TextBox { Watermark = "None" };
        private readonly TextBox _UserId = new TextBox { Watermark = "None" };
        private readonly TextBox _AccessKey = new TextBox { PasswordChar = '\u2022', Watermark = "None: no sign-in (same-computer Admiral only)" };
        private readonly TextBox _Secret = new TextBox { PasswordChar = '\u2022', Watermark = "None" };
        private readonly CheckBox _ShowSecrets = new CheckBox { Content = "Show" };
        private readonly TextBox _Capabilities = new TextBox { Watermark = "git" };
        private readonly NumericUpDown _Heartbeat = new NumericUpDown { Minimum = 0, Maximum = 600000, Increment = 1000, FormatString = "0", MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly NumericUpDown _MaxJobs = new NumericUpDown { Minimum = 1, Maximum = 64, Increment = 1, FormatString = "0", MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly ComboBox _Appearance = new ComboBox { ItemsSource = new string[] { "Follow the system", "Light", "Dark" }, MinWidth = 180 };
        private readonly TextBlock _Message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private bool _Loading = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Harbor session.</param>
        public HarborSettingsView(HarborSession session)
        {
            _Session = session ?? throw new ArgumentNullException(nameof(session));

            Grid identity = Form();
            AddField(identity, "Name", _Name, "How this computer appears on the Admiral's Harbors page.");
            _HarborId = HarborUi.Secondary(new SelectableTextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
            AddField(identity, "Harbor ID", _HarborId, null);

            Grid connection = Form();
            AddField(connection, "Admiral address", _LinkUrl, "Where this computer connects to the Admiral: ws://host:7890/v1.0/harbor/connect (wss:// over TLS).");
            AddField(connection, "Dashboard address", _DashboardUrl, "Opened by the Dashboard button.");

            Grid keyRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            keyRow.Children.Add(_AccessKey);
            Grid.SetColumn(_ShowSecrets, 1);
            _ShowSecrets.Margin = new Thickness(8, 0, 0, 0);
            keyRow.Children.Add(_ShowSecrets);
            _ShowSecrets.IsCheckedChanged += (sender, args) =>
            {
                char mask = _ShowSecrets.IsChecked == true ? '\0' : '\u2022';
                _AccessKey.PasswordChar = mask;
                _Secret.PasswordChar = mask;
            };
            AddField(connection, "Access key", keyRow, "An Armada credential. Needed when the Admiral is on another computer or requires sign-in.");
            AddField(connection, "Secret", _Secret, null);
            AddField(connection, "Tenant ID", _TenantId, "Tenant to register under. The access key decides the tenant unless it belongs to a global admin.");
            AddField(connection, "User ID", _UserId, "For information only; the Admiral takes the owner from the access key.");

            Grid work = Form();
            AddField(work, "Jobs at once", _MaxJobs, "How many agents may run on this computer at the same time.");
            AddField(work, "Tools offered", _Capabilities, "Comma-separated tools this computer offers, such as git. Agent runtimes are offered automatically.");
            AddField(work, "Heartbeat (ms)", _Heartbeat, "How often this computer tells the Admiral it is alive. 0 turns heartbeats off.");

            Grid look = Form();
            AddField(look, "Color scheme", _Appearance, "Follow the system, or always light or dark. Applies right away.");
            _Appearance.SelectionChanged += (sender, args) =>
            {
                // Preview at once; Save keeps it (Revert puts the saved one back).
                if (!_Loading && _Appearance.SelectedIndex >= 0 && Application.Current is App app) app.ApplyAppearance((HarborAppearanceEnum)_Appearance.SelectedIndex);
            };

            StackPanel buttons = HarborUi.ButtonRow();
            Button save = HarborUi.Button("Save", Save);
            save.Classes.Add("accent");
            buttons.Children.Add(save);
            buttons.Children.Add(HarborUi.Button("Revert", () => Load(_Session.Settings), "Discard unsaved changes"));
            buttons.Children.Add(HarborUi.Button("Restore Previous Version...", () => _ = RestorePreviousAsync(), "Choose a version of this file from before an earlier save and put it back"));
            buttons.Children.Add(HarborUi.Button("Reload from File", ReloadFromFile, "Read the settings file again (after editing it by hand)"));
            buttons.Children.Add(HarborUi.Button("Open File", OpenFile, "Open settings.json in a text editor"));
            buttons.Children.Add(HarborUi.Button("Show in Folder", RevealFile));

            StackPanel footer = new StackPanel { Spacing = 8 };
            footer.Children.Add(new ScrollViewer
            {
                Content = buttons,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            });
            footer.Children.Add(_Message);

            Content = HarborUi.Page(
                HarborUi.Note("These settings are for Harbor on this computer, stored in " + HarborAppSettings.DefaultPath()
                    + ". Saving keeps the previous version and reconnects when a connection setting changed."),
                HarborUi.Card("This computer", identity, null),
                HarborUi.Card("Connection to the Admiral", connection, null),
                HarborUi.Card("Work", work, null),
                HarborUi.Card("Appearance", look, null),
                footer);
            Load(_Session.Settings);

            // An appearance previewed but not saved does not outlive the window.
            DetachedFromVisualTree += (sender, args) =>
            {
                if (Application.Current is App app) app.ApplyAppearance(_Session.Settings.Appearance);
            };
        }

        #endregion

        #region Private-Methods

        private static Grid Form()
        {
            return new Grid { ColumnDefinitions = new ColumnDefinitions("160,*"), RowSpacing = 12 };
        }

        private static void AddField(Grid form, string label, Control input, string? help)
        {
            int row = form.RowDefinitions.Count;
            form.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            TextBlock labelBlock = new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetRow(labelBlock, row);
            form.Children.Add(labelBlock);

            StackPanel cell = new StackPanel { Spacing = 2 };
            cell.Children.Add(input);
            if (!String.IsNullOrEmpty(help)) cell.Children.Add(HarborUi.Help(help!));
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, 1);
            form.Children.Add(cell);
        }

        private void Load(HarborAppSettings settings)
        {
            _Loading = true;
            _Name.Text = settings.Name;
            _HarborId.Text = settings.HarborId;
            _LinkUrl.Text = settings.ServerLinkUrl;
            _DashboardUrl.Text = settings.DashboardUrl;
            _TenantId.Text = settings.TenantId;
            _UserId.Text = settings.UserId;
            _AccessKey.Text = settings.AccessKey;
            _Secret.Text = settings.Secret;
            _Capabilities.Text = String.Join(", ", settings.Capabilities ?? new List<string>());
            _Heartbeat.Value = settings.HeartbeatIntervalMs;
            _MaxJobs.Value = settings.MaxConcurrentJobs;
            _Appearance.SelectedIndex = (int)settings.Appearance;
            _Loading = false;
            if (Application.Current is App app) app.ApplyAppearance(settings.Appearance);
            SetMessage(null, false);
        }

        private HarborAppSettings Read()
        {
            HarborAppSettings edited = _Session.Settings.Clone();
            edited.Name = (_Name.Text ?? String.Empty).Trim();
            edited.ServerLinkUrl = (_LinkUrl.Text ?? String.Empty).Trim();
            edited.DashboardUrl = (_DashboardUrl.Text ?? String.Empty).Trim();
            edited.TenantId = (_TenantId.Text ?? String.Empty).Trim();
            edited.UserId = (_UserId.Text ?? String.Empty).Trim();
            edited.AccessKey = (_AccessKey.Text ?? String.Empty).Trim();
            edited.Secret = (_Secret.Text ?? String.Empty).Trim();
            edited.Capabilities = (_Capabilities.Text ?? String.Empty)
                .Split(new char[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            edited.HeartbeatIntervalMs = (int)(_Heartbeat.Value ?? 0);
            edited.MaxConcurrentJobs = (int)(_MaxJobs.Value ?? 1);
            edited.Appearance = _Appearance.SelectedIndex < 0 ? HarborAppearanceEnum.System : (HarborAppearanceEnum)_Appearance.SelectedIndex;
            return edited;
        }

        private void Save()
        {
            HarborAppSettings edited = Read();
            bool wasLinked = _Session.Window.IsLinkRunning;
            if (!_Session.ApplySettings(edited, out List<string> errors))
            {
                SetMessage(String.Join("\n", errors), true);
                return;
            }

            Load(_Session.Settings);
            SetMessage("Saved." + (wasLinked ? " The link reconnects if a link setting changed." : ""), false);
        }

        private async Task RestorePreviousAsync()
        {
            Window? owner = TopLevel.GetTopLevel(this) as Window;
            string path = HarborAppSettings.DefaultPath();
            SettingsBackupEntry? chosen = await RestoreVersionDialog.ChooseAsync(owner, path).ConfigureAwait(true);
            if (chosen == null) return;

            HarborAppSettings? restored = HarborAppSettings.TryLoadFile(chosen.Path, out string? readError);
            if (restored == null)
            {
                SetMessage("Could not read that version: " + readError, true);
                return;
            }

            // A version from before the Harbor ID was set keeps this Harbor's identity.
            if (String.IsNullOrWhiteSpace(restored.HarborId)) restored.HarborId = _Session.Settings.HarborId;
            List<string> problems = restored.Validate();
            if (problems.Count > 0)
            {
                SetMessage("That version has problems, so it was not restored:\n" + String.Join("\n", problems), true);
                return;
            }

            string when = HarborUi.LocalTime(chosen.TakenUtc);
            bool confirmed = await HarborDialog.ConfirmAsync(owner, "Restore this version?",
                "Replace Harbor's settings with the version saved before " + when + "? Unsaved changes on this page are discarded, and the current version is kept so you can switch back."
                + (_Session.Window.IsLinkRunning ? " Harbor reconnects if a connection setting changes." : ""),
                "Restore").ConfigureAwait(true);
            if (!confirmed) return;

            // Saved like any edit: validated, written atomically, with the current file kept as a backup.
            if (!_Session.ApplySettings(restored, out List<string> errors))
            {
                SetMessage(String.Join("\n", errors), true);
                return;
            }

            Load(_Session.Settings);
            SetMessage("Restored the version saved before " + when + ".", false);
        }

        private void ReloadFromFile()
        {
            HarborAppSettings fromFile = HarborAppSettings.Load();
            fromFile.HarborId = String.IsNullOrWhiteSpace(fromFile.HarborId) ? _Session.Settings.HarborId : fromFile.HarborId;
            if (!_Session.ApplySettings(fromFile, out List<string> errors))
            {
                SetMessage("The settings file has problems; nothing was applied:\n" + String.Join("\n", errors), true);
                return;
            }

            Load(_Session.Settings);
            SetMessage("Reloaded from " + HarborAppSettings.DefaultPath() + ".", false);
        }

        private void OpenFile()
        {
            _Session.Settings.Save();
            if (!PlatformShell.OpenInTextEditor(HarborAppSettings.DefaultPath(), out string? error))
                SetMessage("Could not open the file: " + error, true);
            else
                SetMessage("Opened in your editor. Click Reload from File after saving it there.", false);
        }

        private void RevealFile()
        {
            _Session.Settings.Save();
            if (!PlatformShell.Reveal(HarborAppSettings.DefaultPath(), out string? error))
                SetMessage("Could not show the file: " + error, true);
        }

        private void SetMessage(string? text, bool isError)
        {
            HarborUi.SetMessage(_Message, text, isError);
        }

        #endregion
    }
}
