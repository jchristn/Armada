namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;

    /// <summary>
    /// The Harbor tab: a form for this Harbor's settings (~/.armada-harbor/settings.json). Saving validates, writes the
    /// file atomically with a backup, applies the values, and reconnects the link when a link setting changed.
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
        private readonly TextBox _AccessKey = new TextBox { PasswordChar = '•', Watermark = "None: unauthenticated local link" };
        private readonly TextBox _Secret = new TextBox { PasswordChar = '•', Watermark = "None" };
        private readonly CheckBox _ShowSecrets = new CheckBox { Content = "Show" };
        private readonly TextBox _Capabilities = new TextBox { Watermark = "git" };
        private readonly NumericUpDown _Heartbeat = new NumericUpDown { Minimum = 0, Maximum = 600000, Increment = 1000, FormatString = "0", MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly NumericUpDown _MaxJobs = new NumericUpDown { Minimum = 1, Maximum = 64, Increment = 1, FormatString = "0", MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly ComboBox _Appearance = new ComboBox { ItemsSource = new string[] { "System", "Light", "Dark" }, MinWidth = 180 };
        private readonly TextBlock _Message = new TextBlock { TextWrapping = TextWrapping.Wrap };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Harbor session.</param>
        public HarborSettingsView(HarborSession session)
        {
            _Session = session ?? throw new ArgumentNullException(nameof(session));

            StackPanel root = new StackPanel { Spacing = 10, Margin = new Thickness(4, 4, 12, 12) };
            root.Children.Add(HarborUi.Heading("Harbor Settings"));
            root.Children.Add(HarborUi.Note("Stored in " + HarborAppSettings.DefaultPath() + ". Saving keeps the previous file as a backup and reconnects the link when a link setting changed."));

            Grid form = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), RowSpacing = 8 };
            AddField(form, "Name", _Name, "Shown on the Admiral's Harbors page.");
            _HarborId = HarborUi.Secondary(new SelectableTextBlock { VerticalAlignment = VerticalAlignment.Center });
            AddField(form, "Harbor ID", _HarborId, null);
            AddField(form, "Link URL", _LinkUrl, "The Admiral's Harbor link: ws://host:7890/v1.0/harbor/connect (wss:// through TLS).");
            AddField(form, "Dashboard URL", _DashboardUrl, "Opened by Open Dashboard.");
            AddField(form, "Tenant ID", _TenantId, "Tenant to register under. A credential decides the tenant unless it belongs to a global admin.");
            AddField(form, "User ID", _UserId, "Informational only; the Admiral takes the owner from the access key.");

            Grid keyRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            keyRow.Children.Add(_AccessKey);
            Grid.SetColumn(_ShowSecrets, 1);
            _ShowSecrets.Margin = new Thickness(8, 0, 0, 0);
            keyRow.Children.Add(_ShowSecrets);
            _ShowSecrets.IsCheckedChanged += (sender, args) =>
            {
                char mask = _ShowSecrets.IsChecked == true ? '\0' : '•';
                _AccessKey.PasswordChar = mask;
                _Secret.PasswordChar = mask;
            };
            AddField(form, "Access key", keyRow, "An Armada credential (bearer token). Required when the Admiral is on another machine or requires authentication.");
            AddField(form, "Secret", _Secret, null);
            AddField(form, "Capabilities", _Capabilities, "Comma-separated tools this host offers. Agent runtimes are advertised automatically.");
            AddField(form, "Heartbeat (ms)", _Heartbeat, "0 turns heartbeats off.");
            AddField(form, "Max concurrent jobs", _MaxJobs, null);
            AddField(form, "Appearance", _Appearance, null);
            root.Children.Add(form);

            StackPanel buttons = HarborUi.ButtonRow();
            Button save = HarborUi.Button("Save", Save);
            save.Classes.Add("accent");
            buttons.Children.Add(save);
            buttons.Children.Add(HarborUi.Button("Revert", () => Load(_Session.Settings), "Discard unsaved changes"));
            buttons.Children.Add(HarborUi.Button("Reload from File", ReloadFromFile, "Read the settings file again (after editing it by hand)"));
            buttons.Children.Add(HarborUi.Button("Open File", OpenFile, "Open settings.json in a text editor"));
            buttons.Children.Add(HarborUi.Button("Show in Folder", RevealFile));
            root.Children.Add(buttons);
            root.Children.Add(_Message);

            Content = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            Load(_Session.Settings);
        }

        #endregion

        #region Private-Methods

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
            _Message.Text = text ?? String.Empty;
            _Message.IsVisible = !String.IsNullOrEmpty(text);
            if (isError) _Message.Foreground = new SolidColorBrush(Color.Parse("#ef4444"));
            else HarborUi.Secondary(_Message);
        }

        #endregion
    }
}
