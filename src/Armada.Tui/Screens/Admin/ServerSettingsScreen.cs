namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Tui.Approvals;
    using Armada.Tui.Input;
    using Armada.Tui.Modals;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using TUIKit.Input;
    using TUIKit.Widgets;
    using Button = Armada.Tui.Widgets.Button;
    using ButtonRow = Armada.Tui.Widgets.ButtonRow;

    /// <summary>
    /// Settings, Server tab (dashboard <c>Server.tsx</c> with <c>RepositoryHealthSettingsSection</c>,
    /// <c>ImportFleetActionSettings</c>, <c>CliPermissionSettings</c>, and <c>RetentionSettings</c>): health, uptime,
    /// connection, and tunnel
    /// cards; server detail fields; every settings group with its own Save (and Discard where the dashboard has it),
    /// the dashboard's validation messages, and <c>Ctrl+S</c> saving the group that holds focus; MCP snippets and
    /// system paths to copy; database backup to a chosen path and restore from a chosen file; and the server actions
    /// (setup wizard, health check, restart, rebuild with a live build log, roll back, stop, factory reset), with
    /// proxy-mode blocking and admin gating as on the dashboard. Not thread-safe.
    /// </summary>
    public class ServerSettingsScreen : StackScreen
    {
        #region Public-Members

        /// <summary>
        /// Page header.
        /// </summary>
        public ScreenHeader Header { get; }

        /// <summary>
        /// Health, uptime, connection, and remote tunnel cards.
        /// </summary>
        public KpiBar StatusCards { get; } = new KpiBar();

        /// <summary>
        /// The scrolling settings form.
        /// </summary>
        public FormView Form { get; } = new FormView();

        /// <summary>
        /// Last health result, or null.
        /// </summary>
        public HealthResult? Health { get; private set; } = null;

        /// <summary>
        /// Last settings, or null.
        /// </summary>
        public SettingsData? Settings { get; private set; } = null;

        /// <summary>
        /// Proxy session context from the last load, or null.
        /// </summary>
        public ProxySessionContext? ProxyContext { get; private set; } = null;

        /// <summary>
        /// Latest rebuild status, or null before a rebuild starts.
        /// </summary>
        public RebuildStatus? Rebuild { get; private set; } = null;

        /// <summary>
        /// True while rebuild status polling runs.
        /// </summary>
        public bool RebuildPolling { get; private set; } = false;

        /// <summary>
        /// Rebuild status poll interval in milliseconds (the dashboard's 1.5 s).
        /// </summary>
        public int RebuildPollIntervalMs
        {
            get { return _RebuildPollIntervalMs; }
            set { _RebuildPollIntervalMs = Math.Clamp(value, 10, 60000); }
        }

        /// <summary>
        /// Settings groups by key (server, rebuild, agent, planning, repositoryHealth, import, fleetActions,
        /// permissions, retention, remoteControl).
        /// </summary>
        public IReadOnlyDictionary<string, ServerSettingsGroup> Groups
        {
            get { return _Groups; }
        }

        /// <summary>
        /// True when connected through Armada.Proxy with a deployment selected (local-only actions are blocked).
        /// </summary>
        public bool ProxyMode
        {
            get { return !String.IsNullOrEmpty(ProxyContext?.SelectedInstanceId ?? Context.Session.Proxy?.SelectedInstanceId); }
        }

        /// <summary>
        /// True for global administrators (backup, server actions, repository health editing).
        /// </summary>
        public bool IsAdmin
        {
            get { return Context.Session.IsGlobalAdmin; }
        }

        /// <summary>Admiral port.</summary>
        public InputField AdmiralPort { get; } = new InputField();

        /// <summary>MCP port.</summary>
        public InputField McpPort { get; } = new InputField();

        /// <summary>Max captains.</summary>
        public InputField MaxCaptains { get; } = new InputField();

        /// <summary>Self vessel (the vessel with Armada's source).</summary>
        public SelectField<string> SelfVessel { get; } = new SelectField<string>();

        /// <summary>Build slot retention.</summary>
        public InputField SlotRetention { get; } = new InputField();

        /// <summary>Heartbeat interval.</summary>
        public InputField HeartbeatInterval { get; } = new InputField();

        /// <summary>Stall threshold.</summary>
        public InputField StallThreshold { get; } = new InputField();

        /// <summary>Idle captain timeout.</summary>
        public InputField IdleCaptainTimeout { get; } = new InputField();

        /// <summary>Auto-create pull requests.</summary>
        public ToggleField AutoCreatePr { get; } = new ToggleField();

        /// <summary>Planning idle timeout.</summary>
        public InputField PlanningInactivity { get; } = new InputField();

        /// <summary>Planning abandonment timeout.</summary>
        public InputField PlanningAbandonment { get; } = new InputField();

        /// <summary>Planning transcript retention.</summary>
        public InputField PlanningRetention { get; } = new InputField();

        /// <summary>Repository health numeric fields by key (intervalMinutes, ..., thresholds.behindWarn, ...).</summary>
        public Dictionary<string, InputField> HealthFields { get; } = new Dictionary<string, InputField>(StringComparer.Ordinal);

        /// <summary>Repository health: fetch before evaluating.</summary>
        public ToggleField FetchBeforeEvaluate { get; } = new ToggleField();

        /// <summary>Repository health scored criteria toggles.</summary>
        public Dictionary<VesselHealthCriterionEnum, ToggleField> Criteria { get; } = new Dictionary<VesselHealthCriterionEnum, ToggleField>();

        /// <summary>Vessel import allowed roots (one per line).</summary>
        public MultilineField AllowedRoots { get; } = new MultilineField();

        /// <summary>Vessel import excluded folder names (one per line).</summary>
        public MultilineField ExcludedNames { get; } = new MultilineField();

        /// <summary>Vessel import max depth.</summary>
        public InputField ImportMaxDepth { get; } = new InputField();

        /// <summary>Vessel import inline batch limit.</summary>
        public InputField ImportInlineLimit { get; } = new InputField();

        /// <summary>Vessel import categorization time limit.</summary>
        public InputField ImportCategorization { get; } = new InputField();

        /// <summary>Fleet actions max concurrency.</summary>
        public InputField FleetMaxConcurrency { get; } = new InputField();

        /// <summary>Fleet actions default timeout.</summary>
        public InputField FleetDefaultTimeout { get; } = new InputField();

        /// <summary>Fleet actions max output bytes.</summary>
        public InputField FleetMaxOutput { get; } = new InputField();

        /// <summary>Fleet actions run retention.</summary>
        public InputField FleetRetention { get; } = new InputField();

        /// <summary>Retention: archive Ask threads after.</summary>
        public InputField RetentionAskArchive { get; } = new InputField();

        /// <summary>Retention: delete Ask threads after.</summary>
        public InputField RetentionAskDelete { get; } = new InputField();

        /// <summary>Retention: jobs.</summary>
        public InputField RetentionJobs { get; } = new InputField();

        /// <summary>Retention: import batches.</summary>
        public InputField RetentionImports { get; } = new InputField();

        /// <summary>Retention: decided CLI tool permission requests.</summary>
        public InputField RetentionCliPermissions { get; } = new InputField();

        /// <summary>CLI tool permissions: Ask conversation default policy.</summary>
        public SelectField<string> PermissionsAskDefault { get; } = new SelectField<string>();

        /// <summary>CLI tool permissions: mission default policy.</summary>
        public SelectField<string> PermissionsMissionDefault { get; } = new SelectField<string>();

        /// <summary>CLI tool permissions: owners may approve their own requests.</summary>
        public ToggleField PermissionsOwnerApproval { get; } = new ToggleField();

        /// <summary>CLI tool permissions: prompt timeout in seconds.</summary>
        public InputField PermissionsTimeout { get; } = new InputField();

        /// <summary>Remote tunnel enabled.</summary>
        public ToggleField RemoteEnabled { get; } = new ToggleField();

        /// <summary>Tunnel URL.</summary>
        public InputField TunnelUrl { get; } = new InputField();

        /// <summary>Instance id override.</summary>
        public InputField InstanceId { get; } = new InputField();

        /// <summary>Enrollment token (masked).</summary>
        public InputField EnrollmentToken { get; } = new InputField();

        /// <summary>Proxy shared password (masked).</summary>
        public InputField ProxyPassword { get; } = new InputField();

        /// <summary>Connect timeout.</summary>
        public InputField ConnectTimeout { get; } = new InputField();

        /// <summary>Tunnel heartbeat interval.</summary>
        public InputField TunnelHeartbeat { get; } = new InputField();

        /// <summary>Reconnect base delay.</summary>
        public InputField ReconnectBase { get; } = new InputField();

        /// <summary>Reconnect max delay.</summary>
        public InputField ReconnectMax { get; } = new InputField();

        /// <summary>Allow invalid certificates.</summary>
        public ToggleField AllowInvalidCertificates { get; } = new ToggleField();

        /// <summary>Rebuild branch picker.</summary>
        public SelectField<string> BranchPicker { get; } = new SelectField<string>();

        /// <summary>Rebuild ref (branch, tag, or commit; blank for HEAD).</summary>
        public InputField BuildRef { get; } = new InputField();

        /// <summary>Read-only detail and path values by English label.</summary>
        public Dictionary<string, ServerTextField> Values { get; } = new Dictionary<string, ServerTextField>(StringComparer.Ordinal);

        /// <summary>Backup and restore buttons (null for non-admins).</summary>
        public ButtonRow? BackupButtons { get; private set; } = null;

        /// <summary>Server action buttons (null for non-admins).</summary>
        public List<ButtonRow> ActionRows { get; } = new List<ButtonRow>();

        #endregion

        #region Private-Members

        private static readonly VesselHealthCriterionEnum[] _AllCriteria = new VesselHealthCriterionEnum[]
        {
            VesselHealthCriterionEnum.GitDivergence, VesselHealthCriterionEnum.WorkingTree, VesselHealthCriterionEnum.Branches,
            VesselHealthCriterionEnum.CommitRecency, VesselHealthCriterionEnum.Dependencies, VesselHealthCriterionEnum.Vulnerabilities,
            VesselHealthCriterionEnum.TestInfrastructure, VesselHealthCriterionEnum.ContinuousIntegration,
            VesselHealthCriterionEnum.ArmadaReadiness, VesselHealthCriterionEnum.MissionOutcomes,
        };

        private readonly Dictionary<string, ServerSettingsGroup> _Groups = new Dictionary<string, ServerSettingsGroup>(StringComparer.Ordinal);
        private readonly Dictionary<IWidget, ServerSettingsGroup> _MemberGroups = new Dictionary<IWidget, ServerSettingsGroup>();
        private readonly List<KeyValuePair<FormRow, TextBlock>> _Notes = new List<KeyValuePair<FormRow, TextBlock>>();
        private readonly List<IWidget> _LockableFields = new List<IWidget>();
        private readonly List<IWidget> _HealthEditable = new List<IWidget>();
        private readonly List<IWidget> _AdminOnlyEditable = new List<IWidget>();
        private readonly TextBlock _HealthCross = new TextBlock("", t => t.Error);
        private readonly TextBlock _HealthNoCriteria = new TextBlock("", t => t.Warning);
        private readonly TextBlock _ProxyNote = new TextBlock("", t => t.Warning);
        private FormRow? _HealthCrossRow = null;
        private readonly Dictionary<string, TextBlock> _DirtyMarkers = new Dictionary<string, TextBlock>(StringComparer.Ordinal);
        private Button? _RestoreButton = null;
        private Button? _BackupButton = null;
        private Button? _SetupButton = null;
        private Button? _RestartButton = null;
        private Button? _RebuildButton = null;
        private Button? _BuildLogButton = null;
        private Button? _RollbackButton = null;
        private Button? _StopButton = null;
        private Button? _ResetButton = null;
        private bool _BackupLoading = false;
        private bool _SuppressRemoteConfirm = false;
        private string? _BranchesFor = null;
        private int _RebuildPollIntervalMs = 1500;
        private CancellationTokenSource? _PollCts = null;
        private LogViewer? _RebuildLog = null;
        private ViewerModal? _RebuildModal = null;
        private bool _Loaded = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and load.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public ServerSettingsScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
            Header = new ScreenHeader("Server Settings", "Admiral server health, configuration, and operational controls.");
            Header.AddButton("Refresh", LoadData, "F5");
            AddFixed(Header, w => Header.HeightFor(w));
            AddFixed(StatusCards, w => StatusCards.PreferredHeight);
            Form.ShowButtons = false;
            BuildForm();
            AddFill(Form);
            Scope.Focus(Form);
            Form.Scope.FocusFirst();
            UpdateCards();
            LoadData();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> TypingHints
        {
            get
            {
                return new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("Ctrl+S", "Save section"),
                    new KeyValuePair<string, string>("Ctrl+R", "Reveal")
                };
            }
        }

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                return new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("Tab", "Next field"),
                    new KeyValuePair<string, string>("Ctrl+S", "Save section"),
                    new KeyValuePair<string, string>("y", "Copy"),
                    new KeyValuePair<string, string>("Ctrl+R", "Reveal"),
                };
            }
        }

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return LoadData;
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            string p = ScreenKey + ".";
            List<ArmadaCommand> list = new List<ArmadaCommand>();
            list.Add(Cmd(p + "save", "Save section", SaveFocusedGroup, () => FocusedGroup() != null));
            if (IsAdmin)
            {
                list.Add(Cmd(p + "backup", "Backup Now", Backup, () => !_BackupLoading));
                list.Add(Cmd(p + "restore", "Restore from Backup", Restore, () => !ProxyMode));
                list.Add(Cmd(p + "setup", "Setup Wizard", OpenSetupWizard, () => !ProxyMode));
                list.Add(Cmd(p + "health", "Health Check", HealthCheck, null));
                list.Add(Cmd(p + "restart", "Restart Server", RestartServer, () => !ProxyMode));
                list.Add(Cmd(p + "rebuild", "Rebuild Armada", RebuildServer, () => !ProxyMode));
                list.Add(Cmd(p + "build-log", "Build Log", OpenBuildLog, () => Rebuild != null && Rebuild.Status != null));
                list.Add(Cmd(p + "rollback", "Roll Back", Rollback, () => !ProxyMode && Rebuild != null && Rebuild.Status == ServerRebuildStatusEnum.Succeeded && !String.IsNullOrEmpty(Rebuild.PreviousSlot)));
                list.Add(Cmd(p + "stop", "Stop Server", StopServer, () => !ProxyMode));
                list.Add(Cmd(p + "reset", "Factory Reset", FactoryReset, () => !ProxyMode));
            }

            return list;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Character && (key.Modifiers & KeyModifiers.Ctrl) != 0 && Char.ToLowerInvariant((char)key.Rune) == 's')
            {
                SaveFocusedGroup();
                return true;
            }

            return base.HandleKey(key);
        }

        /// <inheritdoc />
        public override void OnDeactivated()
        {
            StopPolling();
            base.OnDeactivated();
        }

        /// <summary>
        /// Load health, settings, and the proxy context (F5 and auto-refresh). Groups with unsaved edits keep them.
        /// </summary>
        public void LoadData()
        {
            Task.Run(async () =>
            {
                HealthResult? health = null;
                SettingsData? settings = null;
                ProxySessionContext? proxy = null;
                try { health = await Context.Client.GetHealthAsync().ConfigureAwait(false); } catch (Exception) { }
                try { settings = await Context.Client.GetSettingsAsync().ConfigureAwait(false); } catch (Exception) { }
                try { proxy = await Context.Client.GetProxySessionContextAsync().ConfigureAwait(false); } catch (Exception) { }
                Context.Dispatcher.Post(() =>
                {
                    if (health != null) Health = health;
                    ProxyContext = proxy;
                    if (settings != null)
                    {
                        Settings = settings;
                        ApplySettings(settings, null);
                    }

                    if (health == null && settings == null) Banner = Context.Loc.T("Failed to load server data.");
                    else if (settings == null) Banner = Context.Loc.T("Failed to load server settings. Health data is available, but configuration and backup sections could not be loaded.");
                    else Banner = "";
                    _Loaded = true;
                    ApplyLocks();
                    UpdateCards();
                    UpdateValues();
                    LoadBranches();
                });
            });
            ScreenOps.Quiet(Context, () => Context.Client.ListVesselsAsync(new ArmadaPageQuery(1, 9999)), r =>
            {
                string current = SelfVessel.Value ?? "";
                List<SelectOption<string>> options = new List<SelectOption<string>> { new SelectOption<string>("", Context.Loc.T("-- none --")) };
                foreach (Vessel v in r?.Objects ?? new List<Vessel>()) options.Add(new SelectOption<string>(v.Id, v.Name + " (" + v.Id + ")"));
                bool dirty = _Groups["rebuild"].IsDirty;
                SelfVessel.Options = options;
                SelfVessel.SetValue(current);
                if (!dirty) _Groups["rebuild"].MarkClean();
            });
        }

        /// <summary>
        /// Save the group that holds focus (what <c>Ctrl+S</c> does).
        /// </summary>
        public void SaveFocusedGroup()
        {
            ServerSettingsGroup? group = FocusedGroup();
            if (group != null) group.SaveButton.Press();
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void BeforeRender(int width, int height)
        {
            BannerStyle = ProxyMode ? (Func<Armada.Tui.Theming.ArmadaTheme, TUIKit.CellStyle>)(t => t.Warning) : t => t.Error;
            if (ProxyMode)
            {
                string instance = ProxyContext?.SelectedInstance?.InstanceId ?? ProxyContext?.SelectedInstanceId ?? Context.Session.Proxy?.SelectedInstanceId ?? "";
                Banner = Context.Loc.T("This page is connected through Armada.Proxy for {{instanceId}}. Local server settings, tunnel settings, setup, restore, shutdown, and factory reset are blocked in remote mode.",
                    LocalizationArgs.Of("instanceId", instance.Length > 0 ? instance : Context.Loc.T("the selected deployment")));
            }

            int dirtyGroups = _Groups.Values.Count(g => g.Editable && g.IsDirty);
            Header.Status = dirtyGroups > 0 ? Context.Loc.T("Unsaved changes") + " (" + dirtyGroups + ")" : "";
            Header.StatusStyle = t => t.Warning;
            foreach (ServerSettingsGroup g in _Groups.Values)
            {
                g.UpdateButtons();
                if (_DirtyMarkers.TryGetValue(g.Key, out TextBlock? marker)) marker.Text = g.Editable && g.IsDirty ? "Unsaved changes" : "";
            }

            List<string> cross = HealthCrossErrors();
            _HealthCross.Text = String.Join("\n", cross);
            if (_HealthCrossRow != null) _HealthCrossRow.Height = Math.Max(1, cross.Count);
            _HealthNoCriteria.Text = Criteria.Values.Any(c => c.Value) ? "" : "With no scored criteria every vessel is Unknown overall.";
            int noteWidth = Math.Max(20, width * 2 / 3 - 2);
            foreach (KeyValuePair<FormRow, TextBlock> note in _Notes)
            {
                note.Key.Height = Math.Max(1, note.Value.Lines(noteWidth).Count);
            }

            UpdateCards();
            UpdateActionButtons();
        }

        #endregion

        #region Private-Methods

        private static ArmadaCommand Cmd(string id, string title, Action handler, Func<bool>? enabled)
        {
            ArmadaCommand c = new ArmadaCommand(id, title, CommandMenuEnum.Actions, handler);
            c.IsEnabled = enabled;
            return c;
        }

        private ServerSettingsGroup? FocusedGroup()
        {
            IWidget? focused = Form.Scope.Focused;
            if (focused != null && _MemberGroups.TryGetValue(focused, out ServerSettingsGroup? group)) return group;
            return null;
        }

        private void BuildForm()
        {
            string[] details = new string[] { "Version", "API URL", "Admiral Port", "MCP Port", "WebSocket Port", "Tunnel State", "Tunnel Instance", "Tunnel Latency", "Tunnel Last Heartbeat" };
            string[] detailHelp = new string[]
            {
                "Currently running Armada server build version.",
                "Base URL of the Armada REST API serving this dashboard.",
                "REST API port currently serving the Armada dashboard and API.",
                "Port currently serving the Armada MCP HTTP endpoint.",
                "Port currently serving the Armada WebSocket endpoint.",
                "Current outbound tunnel connection state reported by the server.",
                "Remote tunnel instance identifier currently advertised to Armada.Proxy.",
                "Most recent measured round-trip latency for the remote tunnel.",
                "Timestamp of the most recent remote tunnel heartbeat.",
            };
            for (int i = 0; i < details.Length; i++) AddValue(details[i], null);

            // Server Configuration.
            Form.AddSection("Server Configuration");
            ServerSettingsGroup server = Group("server", "Save Server Config", SaveServerConfig, null);
            AddNumber(server, "Admiral Port", AdmiralPort, "REST API port (1-65535)", v => ServerSettingsRules.WholeNumberRange(Context.Loc, v, 1, 65535));
            AddNumber(server, "MCP Port", McpPort, "MCP server port (1-65535)", v => ServerSettingsRules.WholeNumberRange(Context.Loc, v, 1, 65535));
            AddNumber(server, "Max Captains", MaxCaptains, "Maximum captains (0 = unlimited)", v => ServerSettingsRules.AtLeast(Context.Loc, v, 0, null));
            AddButtons(server);

            // Rebuild Armada.
            Form.AddSection("Rebuild Armada");
            AddNote("Designate the vessel that holds Armada's own source so the \"Rebuild Armada\" button knows what to build. Rebuilding forces a server restart; if you are running Harbor, restart it manually afterward.");
            ServerSettingsGroup rebuild = Group("rebuild", "Save Rebuild Settings", SaveRebuildSettings, null);
            SelfVessel.ModalHost = Context.Modals;
            SelfVessel.PickerTitle = "Self Vessel ID";
            SelfVessel.Options = new List<SelectOption<string>> { new SelectOption<string>("", Context.Loc.T("-- none --")) };
            SelfVessel.SetValue("");
            AddTracked(rebuild, "Self Vessel ID", SelfVessel, "The vessel holding Armada source. Select none to disable rebuild.");
            AddNumber(rebuild, "Slot Retention", SlotRetention, "Number of published build slots to keep on disk for rollback.", v => ServerSettingsRules.AtLeast(Context.Loc, v, 1, null));
            AddButtons(rebuild);

            // Agent Settings.
            Form.AddSection("Agent Settings");
            ServerSettingsGroup agent = Group("agent", "Save Agent Settings", SaveAgentSettings, null);
            AddNumber(agent, "Heartbeat Interval (seconds)", HeartbeatInterval, "Health check interval, minimum 5 seconds", v => ServerSettingsRules.AtLeast(Context.Loc, v, 5, null));
            AddNumber(agent, "Stall Threshold (minutes)", StallThreshold, "Minutes before a captain is considered stalled", v => ServerSettingsRules.AtLeast(Context.Loc, v, 1, null));
            AddNumber(agent, "Idle Captain Timeout (seconds)", IdleCaptainTimeout, "Auto-remove idle captains after this many seconds (0 = disabled)", v => ServerSettingsRules.AtLeast(Context.Loc, v, 0, null));
            AutoCreatePr.Caption = "Auto-Create Pull Requests";
            AddTracked(agent, "Pull Requests", AutoCreatePr, "Automatically open pull requests when supported by the mission and vessel configuration.");
            AddButtons(agent);

            // Planning Session Settings.
            Form.AddSection("Planning Session Settings");
            AddNote("Idle planning sessions are only auto-ended when there is no running planning process. Set a value to 0 to disable the corresponding cleanup rule.");
            ServerSettingsGroup planning = Group("planning", "Save Planning Session Settings", SavePlanningSettings, null);
            AddNumber(planning, "Idle Session Timeout (minutes)", PlanningInactivity, "Minutes before an idle planning session is automatically ended (0 = disabled)", v => ServerSettingsRules.AtLeast(Context.Loc, v, 0, null));
            AddNumber(planning, "Abandonment Timeout (minutes)", PlanningAbandonment, "Minutes before a stale planning session is force-ended (0 = disabled)", v => ServerSettingsRules.AtLeast(Context.Loc, v, 0, null));
            AddNumber(planning, "Transcript Retention (days)", PlanningRetention, "Days to keep stopped or failed planning sessions before deleting them (0 = disabled)", v => ServerSettingsRules.AtLeast(Context.Loc, v, 0, null));
            AddButtons(planning);

            BuildRepositoryHealth();
            BuildImportAndFleet();
            BuildCliPermissions();
            BuildRetention();
            BuildRemoteControl();
            BuildMcp();

            Form.AddSection("System Paths");
            AddValue("Data Directory", "Path to the Armada data directory.");
            AddValue("Database Path", "Path to the Armada SQLite database file.");
            AddValue("Log Directory", "Path where Armada writes server and captain logs.");
            AddValue("Docks Directory", "Path where Armada stores captain worktrees and docks.");
            AddValue("Repos Directory", "Path where Armada stores cloned repositories.");

            if (IsAdmin)
            {
                Form.AddSection("Database Backup");
                BackupButtons = new ButtonRow();
                _BackupButton = BackupButtons.Add(new Button("Backup Now", Backup));
                _RestoreButton = BackupButtons.Add(new Button("Restore from Backup", Restore));
                Form.AddField("", BackupButtons, "Backup download remains available through the proxy relay, but restore is blocked remotely by proxy policy.");

                Form.AddSection("Server Actions");
                _ProxyNote.Text = "";
                AddNoteBlock(_ProxyNote);
                ButtonRow first = new ButtonRow();
                _SetupButton = first.Add(new Button("Setup Wizard", OpenSetupWizard));
                first.Add(new Button("Health Check", HealthCheck));
                _RestartButton = first.Add(new Button("Restart Server", RestartServer));
                Form.AddField("", first);
                ActionRows.Add(first);
                BranchPicker.ModalHost = Context.Modals;
                BranchPicker.PickerTitle = "Branch";
                BranchPicker.Options = new List<SelectOption<string>> { new SelectOption<string>("", Context.Loc.T("current HEAD")) };
                BranchPicker.SetValue("");
                BranchPicker.ValueChanged += (s, e) => { if (e.NewValue != null) BuildRef.Value = e.NewValue; };
                Form.AddField("Branch", BranchPicker, "Branch to build. Choose a branch or type a tag/commit in the box.");
                BuildRef.Placeholder = "ref (blank = HEAD)";
                Form.AddField("Ref", BuildRef, "Branch, tag, or commit to build. Leave blank to build the current HEAD.");
                ButtonRow rebuildRow = new ButtonRow();
                _RebuildButton = rebuildRow.Add(new Button("Rebuild Armada", RebuildServer));
                _BuildLogButton = rebuildRow.Add(new Button("Build Log", OpenBuildLog));
                _RollbackButton = rebuildRow.Add(new Button("Roll Back", Rollback));
                Form.AddField("", rebuildRow);
                ActionRows.Add(rebuildRow);
                ButtonRow danger = new ButtonRow();
                _StopButton = danger.Add(new Button("Stop Server", StopServer));
                _ResetButton = danger.Add(new Button("Factory Reset", FactoryReset));
                Form.AddField("", danger);
                ActionRows.Add(danger);
            }
        }

        private void BuildRepositoryHealth()
        {
            Form.AddSection("Repository Health");
            AddNote("Controls scheduled vessel health evaluation, dependency freshness, and the thresholds behind Warn and Fail. Changes apply immediately.");
            if (!IsAdmin) AddNote("Only administrators can change these settings.");
            ServerSettingsGroup g = Group("repositoryHealth", "Save Repository Health Settings", SaveRepositoryHealth, null);
            AddHealthNumber(g, "intervalMinutes", "Evaluation interval (minutes)", "Minutes between scheduled evaluations; 0 turns the schedule off.", 0, 10080);
            AddHealthNumber(g, "maxConcurrency", "Max concurrency", "Vessels evaluated at the same time within one job.", 1, 32);
            AddHealthNumber(g, "dependencyMaxAgeHours", "Dependency result max age (hours)", "Refresh dependency results after this long even when manifests are unchanged.", 1, 720);
            AddHealthNumber(g, "dependencyCommandTimeoutSeconds", "Dependency command timeout (seconds)", "Timeout for each dotnet or npm call; a timeout grades Unknown.", 10, 900);
            AddHealthNumber(g, "staleBranchDays", "Stale branch age (days)", "A branch whose last commit is older than this counts as stale.", 1, 3650);
            AddHealthNumber(g, "missionWindowDays", "Mission failure window (days)", "Window for counting failed and landing-failed missions.", 1, 90);
            FetchBeforeEvaluate.Caption = "Fetch before evaluating";
            AddTracked(g, "Fetch", FetchBeforeEvaluate, "Run git fetch before measuring divergence. If the fetch fails, divergence is Unknown.");
            _HealthEditable.Add(FetchBeforeEvaluate);
            Form.AddSection("Scored criteria");
            AddNote("The overall status is the worst status among these criteria.");
            foreach (VesselHealthCriterionEnum c in _AllCriteria)
            {
                ToggleField toggle = new ToggleField(false, CriterionLabel(c));
                Criteria[c] = toggle;
                AddTracked(g, "", toggle, null);
                _HealthEditable.Add(toggle);
            }

            AddNoteBlock(_HealthNoCriteria);
            Form.AddSection("Thresholds");
            AddHealthNumber(g, "thresholds.behindWarn", "Behind: warn at", "Commits behind the default branch that warn.", 1, 100000);
            AddHealthNumber(g, "thresholds.behindFail", "Behind: fail at", "Commits behind the default branch that fail.", 1, 100000);
            AddHealthNumber(g, "thresholds.staleBranchWarn", "Stale branches: warn at", "Stale branches that warn.", 1, 10000);
            AddHealthNumber(g, "thresholds.staleBranchFail", "Stale branches: fail at", "Stale branches that fail.", 1, 10000);
            AddHealthNumber(g, "thresholds.missionFailureWarn", "Failed missions: warn at", "Recent failed missions that warn.", 1, 1000);
            AddHealthNumber(g, "thresholds.missionFailureFail", "Failed missions: fail at", "Recent failed missions that fail.", 1, 1000);
            g.CrossErrors = HealthCrossErrors;
            Form.AddField("", _HealthCross);
            _HealthCrossRow = Form.Rows[Form.Rows.Count - 1];
            AddButtons(g);
        }

        private void BuildImportAndFleet()
        {
            Form.AddSection("Vessel Import");
            AddNote("Controls where the import wizard may browse and discover repositories on the Admiral host. Changes apply immediately.");
            ServerSettingsGroup import = Group("import", "Save Import Settings", SaveImport, () => DiscardGroup("import"));
            import.RequireDirty = true;
            AllowedRoots.Placeholder = "/Users/alex/Code";
            AllowedRoots.ExternalEditor = text => Context.External.EditTextAsync(text, ".txt");
            AllowedRoots.Dispatcher = Context.Dispatcher;
            AddTracked(import, "Allowed roots", AllowedRoots, "Absolute folders that browse and discover are limited to. Empty means the user profile folder of the account running the Admiral.", 4);
            ExcludedNames.Placeholder = "node_modules";
            ExcludedNames.ExternalEditor = text => Context.External.EditTextAsync(text, ".txt");
            ExcludedNames.Dispatcher = Context.Dispatcher;
            AddTracked(import, "Excluded folder names", ExcludedNames, "Folder names discovery never descends into. Names starting with a dot are always skipped.", 5);
            AddNumber(import, "Max depth", ImportMaxDepth, "Folder levels searched below each scan root (1-16, default 6).", v => ServerSettingsRules.WholeNumberRange(Context.Loc, v, 1, 16));
            AddNumber(import, "Inline batch limit", ImportInlineLimit, "Largest selection imported inside the request; larger imports run as a background job (1-500, default 25).", v => ServerSettingsRules.WholeNumberRange(Context.Loc, v, 1, 500));
            AddNumber(import, "Fleet categorization time limit (minutes)", ImportCategorization, "Longest a captain may spend recommending fleets for an import before it is stopped (1-240, default 20).", v => ServerSettingsRules.WholeNumberRange(Context.Loc, v, 1, 240));
            AddButtons(import);

            Form.AddSection("Fleet Actions");
            AddNote("Limits for commands and missions run across many vessels. Changes apply immediately.");
            ServerSettingsGroup fleet = Group("fleetActions", "Save Fleet Action Settings", SaveFleetActions, () => DiscardGroup("fleetActions"));
            fleet.RequireDirty = true;
            AddNumber(fleet, "Max concurrency", FleetMaxConcurrency, "Command targets executing at once across every run on the Admiral (1-32, default 8).", v => ServerSettingsRules.WholeNumberRange(Context.Loc, v, 1, 32));
            AddNumber(fleet, "Default timeout (seconds)", FleetDefaultTimeout, "Timeout for new Command actions that do not set one (5-7200, default 300).", v => ServerSettingsRules.WholeNumberRange(Context.Loc, v, 5, 7200));
            AddNumber(fleet, "Max output bytes", FleetMaxOutput, "Bytes kept per output stream per target; the end of the stream is kept (1024-1048576, default 65536).", v => ServerSettingsRules.WholeNumberRange(Context.Loc, v, 1024, 1048576));
            AddNumber(fleet, "Run retention (days)", FleetRetention, "Finished runs older than this are pruned (1-3650, default 30).", v => ServerSettingsRules.WholeNumberRange(Context.Loc, v, 1, 3650));
            AddButtons(fleet);
        }

        private void BuildCliPermissions()
        {
            Form.AddSection("CLI Tool Permissions");
            AddNote("How CLI captains handle shell commands, file edits, and fetches that need permission when neither the conversation nor the captain sets a policy. Refuse lets the CLI refuse them; Approve in Armada asks an approver here (Claude Code only; other runtimes fall back to Refuse); Bypass runs them without asking.");
            if (!IsAdmin) AddNote("Only administrators can change these settings.");
            ServerSettingsGroup g = Group("permissions", "Save CLI Tool Permissions", SavePermissions, () => DiscardGroup("permissions"));
            g.RequireDirty = true;
            foreach (SelectField<string> field in new[] { PermissionsAskDefault, PermissionsMissionDefault })
            {
                field.ModalHost = Context.Modals;
                // Bypass is always listed so a stored Bypass shows; only admins may choose it.
                field.Options = CliPermissionPolicyChoice.Options(Context.Loc, false, null, IsAdmin, CliPermissionPolicyEnum.Bypass);
                CliPermissionPolicyChoice.GuardBypass(Context, field, () => IsAdmin);
            }

            PermissionsAskDefault.PickerTitle = "Ask conversation default";
            PermissionsMissionDefault.PickerTitle = "Mission default";
            PermissionsAskDefault.SetValue(CliPermissionPolicyChoice.ValueOf(new CliPermissionSettings().AskDefaultPolicy));
            PermissionsMissionDefault.SetValue(CliPermissionPolicyChoice.ValueOf(new CliPermissionSettings().MissionDefaultPolicy));
            AddTracked(g, "Ask conversation default", PermissionsAskDefault, "Used by Ask conversation turns (default Approve in Armada). Narrations and summaries never ask; they refuse instead.");
            AddTracked(g, "Mission default", PermissionsMissionDefault, "Used by missions (default Bypass, the previous behavior). A vessel with auto-approve off caps missions at Refuse.");
            AddNumber(g, "Prompt timeout (seconds)", PermissionsTimeout, "How long a request waits for a decision before it expires and is denied (10-3600, default 600).", v => ServerSettingsRules.WholeNumberRange(Context.Loc, v, 10, 3600));
            PermissionsOwnerApproval.Caption = "Let owners approve their own requests";
            AddTracked(g, "Owner approval", PermissionsOwnerApproval, "When on, the owner of a conversation or mission may allow or deny its requests. Admins and tenant admins can always decide; only they can create rules.");
            foreach (IWidget w in new IWidget[] { PermissionsAskDefault, PermissionsMissionDefault, PermissionsTimeout, PermissionsOwnerApproval }) _AdminOnlyEditable.Add(w);
            AddButtons(g);
        }

        private void BuildRetention()
        {
            Form.AddSection("Data Retention");
            AddNote("How long Armada keeps Ask threads, finished background jobs, and import history. 0 means never. Changes apply immediately; pruning runs in the background about once an hour.");
            ServerSettingsGroup g = Group("retention", "Save Retention Settings", SaveRetention, () => DiscardGroup("retention"));
            g.RequireDirty = true;
            Func<string, string?> range = v => ServerSettingsRules.WholeNumberRange(Context.Loc, v, 0, 3650);
            AddNumber(g, "Archive Ask threads after (days)", RetentionAskArchive, "Threads with no activity for this long are archived; pinned threads never are (0-3650, default 90).", range);
            AddNumber(g, "Delete Ask threads after (days)", RetentionAskDelete, "Threads with no activity for this long are deleted with their messages; pinned threads never are (0-3650, default 0).", range);
            AddNumber(g, "Job retention (days)", RetentionJobs, "Finished background jobs older than this are deleted; the latest of each kind is kept (0-3650, default 30).", range);
            AddNumber(g, "Import history retention (days)", RetentionImports, "Finished vessel import batches older than this are deleted; imported vessels are not affected (0-3650, default 90).", range);
            AddNumber(g, "CLI permission request retention (days)", RetentionCliPermissions, "Decided, expired, and cancelled CLI tool permission requests older than this are deleted; pending ones are kept (0-3650, default 90).", range);
            AddButtons(g);
        }

        private void BuildRemoteControl()
        {
            Form.AddSection("Remote Control");
            AddNote("Outbound tunnel settings for connecting this Armada server to Armada.Proxy.");
            ServerSettingsGroup g = Group("remoteControl", "Save Remote Control Settings", SaveRemoteControl, null);
            RemoteEnabled.Caption = "Enable Remote Tunnel";
            RemoteEnabled.ValueChanged += (s, e) => OnRemoteEnabledChanged(e.NewValue);
            AddTracked(g, "Tunnel", RemoteEnabled, "Open an outbound remote-management tunnel from this Armada instance to Armada.Proxy.");
            AddTracked(g, "Tunnel URL", TunnelUrl, "Armada.Proxy base URL or explicit /tunnel endpoint used for remote management.");
            InstanceId.Placeholder = "Leave blank for auto-generated";
            AddTracked(g, "Instance ID Override", InstanceId, "Optional stable deployment identifier advertised to Armada.Proxy. Leave blank to let Armada derive one automatically.");
            EnrollmentToken.Masked = true;
            EnrollmentToken.Placeholder = "Optional bootstrap token";
            AddTracked(g, "Instance Enrollment Token", EnrollmentToken, "Optional extra admission token used only when Armada.Proxy requires instance enrollment tokens.");
            ProxyPassword.Masked = true;
            ProxyPassword.Placeholder = "Defaults to armadaadmin";
            AddTracked(g, "Proxy Shared Password", ProxyPassword, "Shared secret used to authenticate this Armada instance to Armada.Proxy and to unlock Armada.Proxy browser access.");
            AddNumber(g, "Connect Timeout (seconds)", ConnectTimeout, "How long Armada waits for the proxy tunnel connection to open before treating the attempt as failed.", v => ServerSettingsRules.WholeNumberRange(Context.Loc, v, 5, 300));
            AddNumber(g, "Heartbeat Interval (seconds)", TunnelHeartbeat, "How often Armada sends tunnel heartbeats to keep the connection alive and measure latency.", v => ServerSettingsRules.WholeNumberRange(Context.Loc, v, 5, 300));
            AddNumber(g, "Reconnect Base Delay (seconds)", ReconnectBase, "Initial reconnect backoff after a tunnel failure. Later retries grow from this base delay.", v => ServerSettingsRules.WholeNumberRange(Context.Loc, v, 1, 300));
            AddNumber(g, "Reconnect Max Delay (seconds)", ReconnectMax, "Maximum reconnect backoff between tunnel retry attempts.", v => ServerSettingsRules.WholeNumberRange(Context.Loc, v, 1, 3600));
            AllowInvalidCertificates.Caption = "Allow Invalid Certificates";
            AddTracked(g, "Certificates", AllowInvalidCertificates, "Allow self-signed or otherwise invalid TLS certificates for https/wss tunnel endpoints. Use only in trusted environments.");
            AddValue("Tunnel Status", null);
            AddValue("Tunnel Error", null);
            Values["Tunnel Error"].Style = t => t.Error;
            AddButtons(g);
        }

        private void BuildMcp()
        {
            Form.AddSection("MCP Configuration");
            AddNote("Client-specific MCP references for Claude, Codex, Gemini, and Cursor.");
            AddNote("MCP bootstrap commands are only valid when connected directly to an Armada server origin. Armada.Proxy does not relay the MCP endpoint.");
            foreach (string client in ServerMcpSnippets.Clients)
            {
                string title = ServerMcpSnippets.Title(client);
                TextBlock location = new TextBlock(ServerMcpSnippets.Location(client), t => t.Muted);
                location.Translate = false;
                Form.AddField(title, location);
                ServerTextField http = new ServerTextField(ServerMcpSnippets.Http(client, 7891));
                http.CopyRequested += (s, text) => Context.Clipboard.Copy(text, "Copied to clipboard");
                Values["mcp." + client + ".http"] = http;
                Form.AddField("HTTP", http, null, http.Lines().Count);
                ServerTextField stdio = new ServerTextField(ServerMcpSnippets.Stdio(client));
                stdio.CopyRequested += (s, text) => Context.Clipboard.Copy(text, "Copied to clipboard");
                Values["mcp." + client + ".stdio"] = stdio;
                Form.AddField("STDIO", stdio, null, stdio.Lines().Count);
            }
        }

        private ServerSettingsGroup Group(string key, string saveLabel, Action onSave, Action? onDiscard)
        {
            ServerSettingsGroup group = new ServerSettingsGroup(key, saveLabel, onSave, onDiscard);
            _Groups[key] = group;
            return group;
        }

        private void AddButtons(ServerSettingsGroup group)
        {
            Form.AddField("", group.Buttons);
            _MemberGroups[group.Buttons] = group;
            if (group.DiscardButton != null)
            {
                TextBlock marker = new TextBlock("", t => t.Warning);
                _DirtyMarkers[group.Key] = marker;
                Form.AddField("", marker);
            }

            group.MarkClean();
        }

        private T AddTracked<T>(ServerSettingsGroup group, string label, T field, string? hint, int height = 1) where T : IWidget
        {
            group.Track(field);
            Form.AddField(label, field, hint, height);
            _MemberGroups[field] = group;
            _LockableFields.Add(field);
            return field;
        }

        private void AddNumber(ServerSettingsGroup group, string label, InputField field, string hint, Func<string, string?> validator)
        {
            field.Validator = validator;
            field.ValueChanged += (s, e) => field.ValidateField();
            AddTracked(group, label, field, hint);
        }

        private void AddHealthNumber(ServerSettingsGroup group, string key, string label, string help, int min, int max)
        {
            InputField field = new InputField();
            HealthFields[key] = field;
            string hint = Context.Loc.T(help) + " " + Context.Loc.T("Range {{min}} to {{max}}.", LocalizationArgs.Of("min", Context.Loc.FormatNumber(min), "max", Context.Loc.FormatNumber(max)));
            AddNumber(group, label, field, hint, v => ServerSettingsRules.Between(Context.Loc, v, min, max));
            _HealthEditable.Add(field);
        }

        private void AddNote(string text)
        {
            AddNoteBlock(new TextBlock(text, t => t.Muted));
        }

        private void AddNoteBlock(TextBlock block)
        {
            Form.AddField("", block);
            _Notes.Add(new KeyValuePair<FormRow, TextBlock>(Form.Rows[Form.Rows.Count - 1], block));
        }

        private void AddValue(string label, string? help)
        {
            ServerTextField field = new ServerTextField("-");
            field.CopyRequested += (s, text) => Context.Clipboard.Copy(text, label);
            Values[label] = field;
            Form.AddField(label, field, help);
        }

        private static string CriterionLabel(VesselHealthCriterionEnum c)
        {
            switch (c)
            {
                case VesselHealthCriterionEnum.GitDivergence: return "Git divergence";
                case VesselHealthCriterionEnum.WorkingTree: return "Working tree";
                case VesselHealthCriterionEnum.Branches: return "Branches";
                case VesselHealthCriterionEnum.CommitRecency: return "Commit recency";
                case VesselHealthCriterionEnum.Dependencies: return "Dependencies";
                case VesselHealthCriterionEnum.Vulnerabilities: return "Vulnerabilities";
                case VesselHealthCriterionEnum.TestInfrastructure: return "Test infrastructure";
                case VesselHealthCriterionEnum.ContinuousIntegration: return "Continuous integration";
                case VesselHealthCriterionEnum.ArmadaReadiness: return "Armada readiness";
                case VesselHealthCriterionEnum.MissionOutcomes: return "Mission outcomes";
                default: return c.ToString();
            }
        }

        private List<string> HealthCrossErrors()
        {
            List<string> errors = new List<string>();
            Cross(errors, "behind", "Behind: fail at must be at least the warn value.");
            Cross(errors, "staleBranch", "Stale branches: fail at must be at least the warn value.");
            Cross(errors, "missionFailure", "Failed missions: fail at must be at least the warn value.");
            return errors;
        }

        private void Cross(List<string> errors, string stem, string message)
        {
            if (!HealthFields.TryGetValue("thresholds." + stem + "Warn", out InputField? warnField) || !HealthFields.TryGetValue("thresholds." + stem + "Fail", out InputField? failField)) return;
            int? warn = ServerSettingsRules.ParseInt(warnField.Value);
            int? fail = ServerSettingsRules.ParseInt(failField.Value);
            if (warnField.FieldError != null || failField.FieldError != null || !warn.HasValue || !fail.HasValue) return;
            if (fail.Value < warn.Value) errors.Add(Context.Loc.T(message));
        }

        private void ApplyLocks()
        {
            bool locked = ProxyMode;
            foreach (IWidget w in _LockableFields)
            {
                if (w is ArmadaWidget aw) aw.CanFocus = !locked;
            }

            foreach (IWidget w in _HealthEditable.Concat(_AdminOnlyEditable))
            {
                if (w is ArmadaWidget aw) aw.CanFocus = !locked && IsAdmin;
            }

            foreach (ServerSettingsGroup g in _Groups.Values) g.Editable = !locked && Settings != null;
            _Groups["repositoryHealth"].Editable = !locked && IsAdmin && Settings != null;
            // CLI tool permissions are global-admin settings; everyone else sees them read-only.
            _Groups["permissions"].Editable = !locked && IsAdmin && Settings != null;
            if (Form.Scope.Focused is ArmadaWidget focused && !focused.CanFocus) Form.Scope.FocusFirst();
        }

        private void ApplySettings(SettingsData s, ServerSettingsGroup? force)
        {
            Apply("server", force, () =>
            {
                AdmiralPort.Value = Num(s.AdmiralPort);
                McpPort.Value = Num(s.McpPort);
                MaxCaptains.Value = Num(s.MaxCaptains);
            });
            Apply("rebuild", force, () =>
            {
                string self = s.SelfVesselId ?? "";
                if (self.Length > 0 && !SelfVessel.Options.Any(o => o.Value == self))
                {
                    List<SelectOption<string>> options = new List<SelectOption<string>>(SelfVessel.Options);
                    options.Add(new SelectOption<string>(self, self));
                    SelfVessel.Options = options;
                }

                SelfVessel.SetValue(self);
                SlotRetention.Value = Num(s.RebuildSlotRetentionCount ?? 3);
            });
            Apply("agent", force, () =>
            {
                HeartbeatInterval.Value = Num(s.HeartbeatIntervalSeconds);
                StallThreshold.Value = Num(s.StallThresholdMinutes);
                IdleCaptainTimeout.Value = Num(s.IdleCaptainTimeoutSeconds);
                AutoCreatePr.SetValue(s.AutoCreatePr ?? false, false);
            });
            Apply("planning", force, () =>
            {
                PlanningInactivity.Value = Num(s.PlanningSessionInactivityTimeoutMinutes);
                PlanningAbandonment.Value = Num(s.PlanningSessionAbandonmentTimeoutMinutes);
                PlanningRetention.Value = Num(s.PlanningSessionRetentionDays);
            });
            Apply("repositoryHealth", force, () =>
            {
                RepositoryHealthSettings rh = s.RepositoryHealth ?? new RepositoryHealthSettings();
                RepositoryHealthThresholds th = rh.Thresholds ?? new RepositoryHealthThresholds();
                HealthFields["intervalMinutes"].Value = Num(rh.IntervalMinutes);
                HealthFields["maxConcurrency"].Value = Num(rh.MaxConcurrency);
                HealthFields["dependencyMaxAgeHours"].Value = Num(rh.DependencyMaxAgeHours);
                HealthFields["dependencyCommandTimeoutSeconds"].Value = Num(rh.DependencyCommandTimeoutSeconds);
                HealthFields["staleBranchDays"].Value = Num(rh.StaleBranchDays);
                HealthFields["missionWindowDays"].Value = Num(rh.MissionWindowDays);
                HealthFields["thresholds.behindWarn"].Value = Num(th.BehindWarn);
                HealthFields["thresholds.behindFail"].Value = Num(th.BehindFail);
                HealthFields["thresholds.staleBranchWarn"].Value = Num(th.StaleBranchWarn);
                HealthFields["thresholds.staleBranchFail"].Value = Num(th.StaleBranchFail);
                HealthFields["thresholds.missionFailureWarn"].Value = Num(th.MissionFailureWarn);
                HealthFields["thresholds.missionFailureFail"].Value = Num(th.MissionFailureFail);
                FetchBeforeEvaluate.SetValue(rh.FetchBeforeEvaluate, false);
                foreach (KeyValuePair<VesselHealthCriterionEnum, ToggleField> c in Criteria) c.Value.SetValue(rh.ScoredCriteria.Contains(c.Key), false);
            });
            Apply("import", force, () =>
            {
                VesselImportSettings imp = s.Import ?? new VesselImportSettings();
                AllowedRoots.Value = String.Join("\n", imp.AllowedRoots ?? new List<string>());
                ExcludedNames.Value = String.Join("\n", imp.ExcludedDirectoryNames ?? new List<string>());
                ImportMaxDepth.Value = Num(imp.MaxDepth);
                ImportInlineLimit.Value = Num(imp.InlineBatchLimit);
                ImportCategorization.Value = Num(imp.CategorizationTimeoutMinutes);
            });
            Apply("fleetActions", force, () =>
            {
                FleetActionSettings fa = s.FleetActions ?? new FleetActionSettings();
                FleetMaxConcurrency.Value = Num(fa.MaxConcurrency);
                FleetDefaultTimeout.Value = Num(fa.DefaultTimeoutSeconds);
                FleetMaxOutput.Value = Num(fa.MaxOutputBytes);
                FleetRetention.Value = Num(fa.RunRetentionDays);
            });
            Apply("retention", force, () =>
            {
                RetentionSettings r = s.Retention ?? new RetentionSettings();
                RetentionAskArchive.Value = Num(r.AskThreadArchiveAfterDays);
                RetentionAskDelete.Value = Num(r.AskThreadDeleteAfterDays);
                RetentionJobs.Value = Num(r.JobRetentionDays);
                RetentionImports.Value = Num(r.ImportBatchRetentionDays);
                RetentionCliPermissions.Value = Num(r.CliPermissionRequestRetentionDays);
            });
            Apply("permissions", force, () =>
            {
                CliPermissionSettings p = s.Permissions ?? new CliPermissionSettings();
                PermissionsAskDefault.SetValue(CliPermissionPolicyChoice.ValueOf(p.AskDefaultPolicy));
                PermissionsMissionDefault.SetValue(CliPermissionPolicyChoice.ValueOf(p.MissionDefaultPolicy));
                PermissionsTimeout.Value = Num(p.PromptTimeoutSeconds);
                PermissionsOwnerApproval.SetValue(p.AllowOwnerApproval, false);
            });
            Apply("remoteControl", force, () =>
            {
                RemoteControlSettings rc = s.RemoteControl ?? new RemoteControlSettings();
                _SuppressRemoteConfirm = true;
                RemoteEnabled.SetValue(rc.Enabled, false);
                _SuppressRemoteConfirm = false;
                TunnelUrl.Value = rc.TunnelUrl ?? "";
                InstanceId.Value = rc.InstanceId ?? "";
                EnrollmentToken.Value = rc.EnrollmentToken ?? "";
                ProxyPassword.Value = rc.Password ?? "";
                ConnectTimeout.Value = Num(rc.ConnectTimeoutSeconds);
                TunnelHeartbeat.Value = Num(rc.HeartbeatIntervalSeconds);
                ReconnectBase.Value = Num(rc.ReconnectBaseDelaySeconds);
                ReconnectMax.Value = Num(rc.ReconnectMaxDelaySeconds);
                AllowInvalidCertificates.SetValue(rc.AllowInvalidCertificates, false);
            });
            UpdateValues();
        }

        private void Apply(string key, ServerSettingsGroup? force, Action apply)
        {
            ServerSettingsGroup group = _Groups[key];
            if (!ReferenceEquals(group, force) && group.IsDirty && _Loaded) return;
            apply();
            group.MarkClean();
        }

        private static string Num(int? value)
        {
            return value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        }

        private void UpdateCards()
        {
            HealthResult? h = Health;
            bool tunnelEnabled = Settings?.RemoteControl?.Enabled ?? h?.RemoteTunnel?.Enabled ?? false;
            string tunnelState = Context.Loc.T(ServerSettingsRules.TunnelLabel(tunnelEnabled, h?.RemoteTunnel?.State.ToString()));
            List<KpiCard> cards = new List<KpiCard>();
            cards.Add(new KpiCard("Health", h != null ? Context.Loc.T(ServerSettingsRules.HealthLabel(h.Status)) : Context.Loc.T("Loading..."),
                h != null && String.Equals(h.Status, "healthy", StringComparison.OrdinalIgnoreCase) ? (Func<Armada.Tui.Theming.ArmadaTheme, TUIKit.CellStyle>)(t => t.Success) : t => t.Warning,
                h != null ? Context.Loc.T("Checked: {{timestamp}}", LocalizationArgs.Of("timestamp", ScreenOps.DateTimeText(Context, h.Timestamp))) : ""));
            cards.Add(new KpiCard("Uptime", h?.Uptime ?? "-", null,
                h?.StartUtc != null ? Context.Loc.T("Started: {{timestamp}}", LocalizationArgs.Of("timestamp", ScreenOps.DateTimeText(Context, h.StartUtc))) : ""));
            bool live = Context.Events.IsLive;
            cards.Add(new KpiCard("Connection", (live ? "* " : "o ") + Context.Loc.T(live ? "Live (WebSocket)" : "Online (HTTP)"), live ? (Func<Armada.Tui.Theming.ArmadaTheme, TUIKit.CellStyle>)(t => t.Success) : t => t.Muted));
            cards.Add(new KpiCard("Remote Tunnel", tunnelState, null,
                !String.IsNullOrEmpty(h?.RemoteTunnel?.TunnelUrl) ? h!.RemoteTunnel!.TunnelUrl! : Context.Loc.T("No tunnel URL configured")));
            StatusCards.SetCards(cards);
        }

        private void UpdateValues()
        {
            HealthResult? h = Health;
            SettingsData? s = Settings;
            bool tunnelEnabled = s?.RemoteControl?.Enabled ?? h?.RemoteTunnel?.Enabled ?? false;
            SetValue("Version", h?.Version);
            SetValue("API URL", Context.Client.BaseUrl);
            SetValue("Admiral Port", h?.Ports != null && h.Ports.Admiral > 0 ? h.Ports.Admiral.ToString(System.Globalization.CultureInfo.InvariantCulture) : PortOf(Context.Client.BaseUrl));
            SetValue("MCP Port", h?.Ports != null && h.Ports.Mcp > 0 ? h.Ports.Mcp.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
            SetValue("WebSocket Port", null);
            SetValue("Tunnel State", Context.Loc.T(ServerSettingsRules.TunnelLabel(tunnelEnabled, h?.RemoteTunnel?.State.ToString())));
            SetValue("Tunnel Instance", h?.RemoteTunnel?.InstanceId);
            SetValue("Tunnel Latency", h?.RemoteTunnel?.LatencyMs != null ? Context.Loc.FormatNumber(h.RemoteTunnel.LatencyMs.Value) + " ms" : null);
            SetValue("Tunnel Last Heartbeat", h?.RemoteTunnel?.LastHeartbeatUtc != null ? ScreenOps.DateTimeText(Context, h.RemoteTunnel.LastHeartbeatUtc) : null);
            SetValue("Tunnel Status", RemoteEnabled.Value ? Context.Loc.T(ServerSettingsRules.TunnelLabel(true, h?.RemoteTunnel?.State.ToString())) : Context.Loc.T("Disabled"));
            Values["Tunnel Error"].Text = h?.RemoteTunnel?.LastError ?? "";
            SetValue("Data Directory", s?.DataDirectory);
            SetValue("Database Path", s?.DatabasePath);
            SetValue("Log Directory", s?.LogDirectory);
            SetValue("Docks Directory", s?.DocksDirectory);
            SetValue("Repos Directory", s?.ReposDirectory);
            int mcpPort = h?.Ports?.Mcp ?? 0;
            if (mcpPort <= 0) mcpPort = s?.McpPort ?? 7891;
            foreach (string client in ServerMcpSnippets.Clients) Values["mcp." + client + ".http"].Text = ServerMcpSnippets.Http(client, mcpPort);
        }

        private void SetValue(string label, string? value)
        {
            if (Values.TryGetValue(label, out ServerTextField? field)) field.Text = String.IsNullOrEmpty(value) ? "-" : value!;
        }

        private static string? PortOf(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        }

        private void UpdateActionButtons()
        {
            bool proxy = ProxyMode;
            _ProxyNote.Text = proxy ? "Setup, shutdown, and factory reset are local-only server actions and are disabled when this dashboard is opened through Armada.Proxy." : "";
            if (_BackupButton != null)
            {
                _BackupButton.Enabled = !_BackupLoading;
                _BackupButton.Label = _BackupLoading ? "Backing up..." : "Backup Now";
            }

            if (_RestoreButton != null) _RestoreButton.Enabled = !proxy;
            if (_SetupButton != null) _SetupButton.Enabled = !proxy;
            if (_RestartButton != null) _RestartButton.Enabled = !proxy;
            if (_RebuildButton != null) _RebuildButton.Enabled = !proxy;
            if (_StopButton != null) _StopButton.Enabled = !proxy;
            if (_ResetButton != null) _ResetButton.Enabled = !proxy;
            if (_BuildLogButton != null) _BuildLogButton.Visible = Rebuild != null && Rebuild.Status != null;
            if (_RollbackButton != null)
            {
                _RollbackButton.Visible = Rebuild != null && Rebuild.Status == ServerRebuildStatusEnum.Succeeded && !String.IsNullOrEmpty(Rebuild.PreviousSlot);
                _RollbackButton.Enabled = !proxy;
            }

            BranchPicker.CanFocus = !proxy;
            BuildRef.CanFocus = !proxy;
        }

        private void LoadBranches()
        {
            string self = Settings?.SelfVesselId ?? "";
            if (!IsAdmin || String.Equals(self, _BranchesFor ?? "", StringComparison.Ordinal)) return;
            _BranchesFor = self;
            List<SelectOption<string>> head = new List<SelectOption<string>> { new SelectOption<string>("", Context.Loc.T("current HEAD")) };
            if (self.Length == 0)
            {
                BranchPicker.Options = head;
                BranchPicker.SetValue("");
                return;
            }

            ScreenOps.Quiet(Context, () => Context.Client.GetVesselBranchesAsync(self), r =>
            {
                List<SelectOption<string>> options = new List<SelectOption<string>>(head);
                foreach (BranchInfo b in r?.Branches ?? new List<BranchInfo>())
                {
                    options.Add(new SelectOption<string>(b.Name, b.Name + (b.IsDefault ? Context.Loc.T(" (default)") : "")));
                }

                BranchPicker.Options = options;
                string? preferred = r?.DefaultBranch;
                if (String.IsNullOrEmpty(preferred)) preferred = r?.Branches?.FirstOrDefault(b => b.IsDefault)?.Name;
                if (!String.IsNullOrEmpty(preferred) && BuildRef.Value.Length == 0)
                {
                    BuildRef.Value = preferred!;
                    BranchPicker.SetValue(preferred);
                }
            });
        }

        private void SaveWith(ServerSettingsGroup group, Func<SettingsData> build, string successMessage, Action? after = null)
        {
            if (!group.Editable || group.Saving) return;
            if (group.RequireDirty && !group.IsDirty) return;
            if (!group.Validate()) return;
            SettingsData body;
            try
            {
                body = build();
            }
            catch (ArgumentException ex)
            {
                ScreenOps.Toast(Context, NotificationSeverityEnum.Error, "Failed: {{message}}", LocalizationArgs.Of("message", ex.Message));
                return;
            }

            group.Saving = true;
            group.SaveButton.Label = "Saving...";
            string originalLabel = SaveLabelOf(group.Key);
            Task.Run(async () =>
            {
                SettingsData? updated = null;
                string? error = null;
                try
                {
                    updated = await Context.Client.UpdateSettingsAsync(body).ConfigureAwait(false);
                }
                catch (ArmadaApiException ex)
                {
                    error = ex.Message;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                Context.Dispatcher.Post(() =>
                {
                    group.Saving = false;
                    group.SaveButton.Label = originalLabel;
                    if (error != null)
                    {
                        ScreenOps.Toast(Context, NotificationSeverityEnum.Error, "Failed: {{message}}", LocalizationArgs.Of("message", error));
                        return;
                    }

                    if (updated != null)
                    {
                        Settings = updated;
                        ApplySettings(updated, group);
                    }
                    else
                    {
                        group.MarkClean();
                    }

                    ApplyLocks();
                    LoadBranches();
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Success, successMessage);
                    after?.Invoke();
                });
            });
        }

        private static string SaveLabelOf(string key)
        {
            switch (key)
            {
                case "server": return "Save Server Config";
                case "rebuild": return "Save Rebuild Settings";
                case "agent": return "Save Agent Settings";
                case "planning": return "Save Planning Session Settings";
                case "repositoryHealth": return "Save Repository Health Settings";
                case "import": return "Save Import Settings";
                case "fleetActions": return "Save Fleet Action Settings";
                case "retention": return "Save Retention Settings";
                case "permissions": return "Save CLI Tool Permissions";
                default: return "Save Remote Control Settings";
            }
        }

        private int IntOf(InputField field)
        {
            return ServerSettingsRules.ParseInt(field.Value) ?? 0;
        }

        private static List<string> LinesOf(MultilineField field)
        {
            return field.Value.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }

        private void SaveServerConfig()
        {
            SaveWith(_Groups["server"], () =>
            {
                SettingsData d = new SettingsData();
                d.AdmiralPort = IntOf(AdmiralPort);
                d.McpPort = IntOf(McpPort);
                d.MaxCaptains = IntOf(MaxCaptains);
                return d;
            }, "Server configuration saved");
        }

        private void SaveRebuildSettings()
        {
            SaveWith(_Groups["rebuild"], () =>
            {
                SettingsData d = new SettingsData();
                d.SelfVesselId = SelfVessel.Value ?? "";
                d.RebuildSlotRetentionCount = IntOf(SlotRetention);
                return d;
            }, "Rebuild settings saved");
        }

        private void SaveAgentSettings()
        {
            SaveWith(_Groups["agent"], () =>
            {
                SettingsData d = new SettingsData();
                d.HeartbeatIntervalSeconds = IntOf(HeartbeatInterval);
                d.StallThresholdMinutes = IntOf(StallThreshold);
                d.IdleCaptainTimeoutSeconds = IntOf(IdleCaptainTimeout);
                d.AutoCreatePr = AutoCreatePr.Value;
                return d;
            }, "Agent settings saved");
        }

        private void SavePlanningSettings()
        {
            SaveWith(_Groups["planning"], () =>
            {
                SettingsData d = new SettingsData();
                d.PlanningSessionInactivityTimeoutMinutes = IntOf(PlanningInactivity);
                d.PlanningSessionAbandonmentTimeoutMinutes = IntOf(PlanningAbandonment);
                d.PlanningSessionRetentionDays = IntOf(PlanningRetention);
                return d;
            }, "Planning session settings saved");
        }

        private void SaveRepositoryHealth()
        {
            SaveWith(_Groups["repositoryHealth"], () =>
            {
                RepositoryHealthSettings rh = new RepositoryHealthSettings();
                rh.IntervalMinutes = IntOf(HealthFields["intervalMinutes"]);
                rh.MaxConcurrency = IntOf(HealthFields["maxConcurrency"]);
                rh.DependencyMaxAgeHours = IntOf(HealthFields["dependencyMaxAgeHours"]);
                rh.DependencyCommandTimeoutSeconds = IntOf(HealthFields["dependencyCommandTimeoutSeconds"]);
                rh.StaleBranchDays = IntOf(HealthFields["staleBranchDays"]);
                rh.MissionWindowDays = IntOf(HealthFields["missionWindowDays"]);
                rh.FetchBeforeEvaluate = FetchBeforeEvaluate.Value;
                rh.ScoredCriteria = _AllCriteria.Where(c => Criteria[c].Value).ToList();
                RepositoryHealthThresholds th = new RepositoryHealthThresholds();
                th.BehindWarn = IntOf(HealthFields["thresholds.behindWarn"]);
                th.BehindFail = IntOf(HealthFields["thresholds.behindFail"]);
                th.StaleBranchWarn = IntOf(HealthFields["thresholds.staleBranchWarn"]);
                th.StaleBranchFail = IntOf(HealthFields["thresholds.staleBranchFail"]);
                th.MissionFailureWarn = IntOf(HealthFields["thresholds.missionFailureWarn"]);
                th.MissionFailureFail = IntOf(HealthFields["thresholds.missionFailureFail"]);
                rh.Thresholds = th;
                SettingsData d = new SettingsData();
                d.RepositoryHealth = rh;
                return d;
            }, "Repository health settings saved. They apply immediately.");
        }

        private void SaveImport()
        {
            SaveWith(_Groups["import"], () =>
            {
                VesselImportSettings imp = new VesselImportSettings();
                imp.AllowedRoots = LinesOf(AllowedRoots);
                imp.ExcludedDirectoryNames = LinesOf(ExcludedNames);
                imp.MaxDepth = IntOf(ImportMaxDepth);
                imp.InlineBatchLimit = IntOf(ImportInlineLimit);
                imp.CategorizationTimeoutMinutes = IntOf(ImportCategorization);
                SettingsData d = new SettingsData();
                d.Import = imp;
                return d;
            }, "Import settings saved and applied.");
        }

        private void SaveFleetActions()
        {
            SaveWith(_Groups["fleetActions"], () =>
            {
                FleetActionSettings fa = new FleetActionSettings();
                fa.MaxConcurrency = IntOf(FleetMaxConcurrency);
                fa.DefaultTimeoutSeconds = IntOf(FleetDefaultTimeout);
                fa.MaxOutputBytes = IntOf(FleetMaxOutput);
                fa.RunRetentionDays = IntOf(FleetRetention);
                SettingsData d = new SettingsData();
                d.FleetActions = fa;
                return d;
            }, "Fleet action settings saved and applied.");
        }

        private void SaveRetention()
        {
            SaveWith(_Groups["retention"], () =>
            {
                RetentionSettings r = new RetentionSettings();
                r.AskThreadArchiveAfterDays = IntOf(RetentionAskArchive);
                r.AskThreadDeleteAfterDays = IntOf(RetentionAskDelete);
                r.JobRetentionDays = IntOf(RetentionJobs);
                r.ImportBatchRetentionDays = IntOf(RetentionImports);
                r.CliPermissionRequestRetentionDays = IntOf(RetentionCliPermissions);
                SettingsData d = new SettingsData();
                d.Retention = r;
                return d;
            }, "Retention settings saved and applied.");
        }

        private void SavePermissions()
        {
            SaveWith(_Groups["permissions"], () =>
            {
                CliPermissionSettings p = new CliPermissionSettings();
                p.AskDefaultPolicy = CliPermissionPolicyChoice.Parse(PermissionsAskDefault.Value) ?? p.AskDefaultPolicy;
                p.MissionDefaultPolicy = CliPermissionPolicyChoice.Parse(PermissionsMissionDefault.Value) ?? p.MissionDefaultPolicy;
                p.AllowOwnerApproval = PermissionsOwnerApproval.Value;
                p.PromptTimeoutSeconds = IntOf(PermissionsTimeout);
                SettingsData d = new SettingsData();
                d.Permissions = p;
                return d;
            }, "CLI tool permission settings saved.");
        }

        private void SaveRemoteControl()
        {
            SaveWith(_Groups["remoteControl"], () =>
            {
                RemoteControlSettings rc = new RemoteControlSettings();
                rc.Enabled = RemoteEnabled.Value;
                rc.TunnelUrl = TunnelUrl.Value.Trim().Length > 0 ? TunnelUrl.Value.Trim() : null;
                rc.InstanceId = InstanceId.Value.Trim().Length > 0 ? InstanceId.Value.Trim() : null;
                rc.EnrollmentToken = EnrollmentToken.Value.Length > 0 ? EnrollmentToken.Value : null;
                rc.Password = ProxyPassword.Value.Length > 0 ? ProxyPassword.Value : null;
                rc.ConnectTimeoutSeconds = IntOf(ConnectTimeout);
                rc.HeartbeatIntervalSeconds = IntOf(TunnelHeartbeat);
                rc.ReconnectBaseDelaySeconds = IntOf(ReconnectBase);
                rc.ReconnectMaxDelaySeconds = IntOf(ReconnectMax);
                rc.AllowInvalidCertificates = AllowInvalidCertificates.Value;
                SettingsData d = new SettingsData();
                d.RemoteControl = rc;
                return d;
            }, "Remote control settings saved", () =>
            {
                ScreenOps.Quiet(Context, () => Context.Client.GetHealthAsync(), h =>
                {
                    if (h != null) Health = h;
                    UpdateValues();
                });
            });
        }

        private void DiscardGroup(string key)
        {
            ServerSettingsGroup group = _Groups[key];
            if (Settings == null) return;
            ApplySettings(Settings, group);
        }

        private void OnRemoteEnabledChanged(bool enabled)
        {
            if (_SuppressRemoteConfirm || !enabled) return;
            _SuppressRemoteConfirm = true;
            RemoteEnabled.SetValue(false, false);
            _SuppressRemoteConfirm = false;
            Context.Confirm("Enable Remote Tunnel", Context.Loc.T("Enabling remote tunnel will enable remote connectivity to this Armada instance. Are you sure?"), () =>
            {
                _SuppressRemoteConfirm = true;
                RemoteEnabled.SetValue(true, false);
                _SuppressRemoteConfirm = false;
                UpdateValues();
            });
        }

        private void HealthCheck()
        {
            ScreenOps.Run(Context, () => Context.Client.GetHealthAsync(), h =>
            {
                if (h == null) return;
                Health = h;
                UpdateValues();
                ScreenOps.Toast(Context, NotificationSeverityEnum.Info, "Health: {{status}} | Uptime: {{uptime}}",
                    LocalizationArgs.Of("status", Context.Loc.T(ServerSettingsRules.HealthLabel(h.Status)), "uptime", h.Uptime ?? "-"));
            }, "Health check failed");
        }

        private void OpenSetupWizard()
        {
            if (ProxyMode) return;
            Context.Navigate("/setup");
        }

        private void Backup()
        {
            if (!IsAdmin || _BackupLoading) return;
            _BackupLoading = true;
            Task.Run(async () =>
            {
                BackupFile? file = null;
                string? error = null;
                try
                {
                    file = await Context.Client.DownloadBackupAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                Context.Dispatcher.Post(() =>
                {
                    _BackupLoading = false;
                    if (file == null)
                    {
                        ScreenOps.Toast(Context, NotificationSeverityEnum.Error, "Backup failed: {{message}}", LocalizationArgs.Of("message", error ?? Context.Loc.T("Unknown error")));
                        return;
                    }

                    BackupFile downloaded = file;
                    PathPrompt.AskSave(Context, "Backup Now", downloaded.FileName, path =>
                    {
                        try
                        {
                            string saved = Context.External.SaveFile(path, downloaded.Content);
                            ScreenOps.Toast(Context, NotificationSeverityEnum.Success, "Backup saved to {{path}}", LocalizationArgs.Of("path", saved));
                        }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
                        {
                            ScreenOps.Toast(Context, NotificationSeverityEnum.Error, "Backup failed: {{message}}", LocalizationArgs.Of("message", ex.Message));
                        }
                    });
                });
            });
        }

        private void Restore()
        {
            if (!IsAdmin || ProxyMode) return;
            PathPrompt.AskOpen(Context, "Restore from Backup", "Restore the database from a backup ZIP file", path =>
            {
                string name = Path.GetFileName(path);
                Context.Confirm("Restore from Backup", Context.Loc.T("Restore the database from {{file}}? The current database is replaced. Server restart recommended afterward.", LocalizationArgs.Of("file", name)), () =>
                {
                    byte[] content;
                    try
                    {
                        content = Context.External.LoadFile(path);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
                    {
                        ScreenOps.Toast(Context, NotificationSeverityEnum.Error, "Restore failed: {{message}}", LocalizationArgs.Of("message", ex.Message));
                        return;
                    }

                    Task.Run(async () =>
                    {
                        string? error = null;
                        try
                        {
                            await Context.Client.RestoreBackupAsync(content, name).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            error = ex.Message;
                        }

                        Context.Dispatcher.Post(() =>
                        {
                            if (error != null)
                            {
                                ScreenOps.Toast(Context, NotificationSeverityEnum.Error, "Restore failed: {{message}}", LocalizationArgs.Of("message", error));
                                return;
                            }

                            ScreenOps.Toast(Context, NotificationSeverityEnum.Success, "Restore completed successfully. Server restart recommended.");
                            LoadData();
                        });
                    });
                }, "Restore");
            });
        }

        private void StopServer()
        {
            if (!IsAdmin || ProxyMode) return;
            Context.Confirm("Stop Server", Context.Loc.T("Stop the Admiral server? This will shut down everything."), () =>
            {
                ScreenOps.RunVoid(Context, () => Context.Client.StopServerAsync(), () =>
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Server shutting down..."), "Failed");
            }, "Stop Server");
        }

        private void RestartServer()
        {
            if (!IsAdmin || ProxyMode) return;
            Context.Confirm("Restart Server", Context.Loc.T("Restart the Admiral server? A replacement process starts and this instance shuts down; the dashboard will be briefly unavailable while it comes back up."), () =>
            {
                ScreenOps.RunVoid(Context, () => Context.Client.RestartServerAsync(), () =>
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Server restarting... the dashboard will reconnect shortly."), "Failed");
            }, "Restart Server");
        }

        private void FactoryReset()
        {
            if (!IsAdmin || ProxyMode) return;
            ConfirmDialog dialog = Context.Confirm("Factory Reset", Context.Loc.T("WARNING: Factory reset will delete ALL data including the database, logs, docks, and repos. Settings will be preserved. This cannot be undone. Continue?"), () =>
            {
                ScreenOps.RunVoid(Context, () => Context.Client.ResetServerAsync(), () =>
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Success, "Factory reset complete"), "Factory reset failed");
            }, "Factory Reset");
            dialog.Destructive = true;
        }

        private void RebuildServer()
        {
            if (!IsAdmin || ProxyMode) return;
            string trimmed = BuildRef.Value.Trim();
            string refLabel = trimmed.Length > 0 ? trimmed : Context.Loc.T("current HEAD");
            Context.Confirm("Rebuild Armada", Context.Loc.T("Rebuild the Admiral from source at {{ref}}? The new build is published into a fresh slot while this instance keeps running; on success the database is backed up and the server cuts over to the new build. A failed build will not disturb the running server.", LocalizationArgs.Of("ref", refLabel)), () =>
            {
                RebuildRequest request = new RebuildRequest();
                if (trimmed.Length > 0) request.Ref = trimmed;
                Task.Run(async () =>
                {
                    RebuildStatus? started = null;
                    string? error = null;
                    try
                    {
                        started = await Context.Client.RebuildServerAsync(request).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }

                    Context.Dispatcher.Post(() =>
                    {
                        if (started == null)
                        {
                            ScreenOps.Toast(Context, NotificationSeverityEnum.Error, "Rebuild failed to start: {{message}}", LocalizationArgs.Of("message", error ?? Context.Loc.T("Unknown error")));
                            return;
                        }

                        Rebuild = started;
                        OpenBuildLog();
                        ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Rebuild started; building slot {{slot}}...",
                            LocalizationArgs.Of("slot", String.IsNullOrEmpty(started.Slot) ? Context.Loc.T("(pending)") : started.Slot));
                        StartPolling();
                    });
                });
            }, "Rebuild Armada");
        }

        private void OpenBuildLog()
        {
            if (_RebuildLog == null)
            {
                _RebuildLog = new LogViewer();
                _RebuildLog.Follow = true;
            }

            UpdateLog();
            if (_RebuildModal != null && !_RebuildModal.IsClosed) return;
            LogViewer log = _RebuildLog;
            ViewerModal modal = new ViewerModal(RebuildTitle(), log, Context.Loc, Context.Theme.Current);
            modal.CopyRequested += (s, e) => Context.Clipboard.Copy(log.PlainText, "Build log");
            Context.Modals.Show(modal);
            _RebuildModal = modal;
            if (!RebuildPolling && Rebuild != null && !ServerSettingsRules.RebuildDone(Rebuild.Status)) StartPolling();
            else if (!RebuildPolling) RefreshRebuildOnce();
        }

        private string RebuildTitle()
        {
            string slot = Rebuild != null && !String.IsNullOrEmpty(Rebuild.Slot) ? " - " + Rebuild.Slot : "";
            return Context.Loc.T("Rebuild Armada{{slot}}", LocalizationArgs.Of("slot", slot));
        }

        private void UpdateLog()
        {
            if (_RebuildLog == null) return;
            string text = Rebuild?.Log ?? "";
            if (Rebuild != null && ServerSettingsRules.RebuildDone(Rebuild.Status)) text += (text.Length > 0 && !text.EndsWith("\n") ? "\n" : "") + "[" + Context.Loc.T(Rebuild.Status.ToString() ?? "") + "]" + (String.IsNullOrEmpty(Rebuild.Error) ? "" : " " + Rebuild.Error);
            _RebuildLog.SetText(text);
        }

        private void RefreshRebuildOnce()
        {
            ScreenOps.Quiet(Context, () => Context.Client.GetRebuildStatusAsync(), s =>
            {
                if (s == null) return;
                Rebuild = s;
                UpdateLog();
            });
        }

        private void StartPolling()
        {
            StopPolling();
            CancellationTokenSource cts = new CancellationTokenSource();
            _PollCts = cts;
            RebuildPolling = true;
            int interval = _RebuildPollIntervalMs;
            Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(interval, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    RebuildStatus? status = null;
                    try
                    {
                        status = await Context.Client.GetRebuildStatusAsync(cts.Token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // The server may be down mid-cutover; keep the last log and stop.
                        status = null;
                    }

                    bool stop = status == null || ServerSettingsRules.RebuildDone(status.Status);
                    Context.Dispatcher.Post(() =>
                    {
                        if (cts.IsCancellationRequested) return;
                        if (status != null) Rebuild = status;
                        UpdateLog();
                        if (stop)
                        {
                            RebuildPolling = false;
                            if (ReferenceEquals(_PollCts, cts)) _PollCts = null;
                        }
                    });
                    if (stop) break;
                }
            });
        }

        private void StopPolling()
        {
            CancellationTokenSource? cts = _PollCts;
            _PollCts = null;
            RebuildPolling = false;
            if (cts != null)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }
        }

        private void Rollback()
        {
            if (!IsAdmin || ProxyMode || Rebuild == null) return;
            string slot = !String.IsNullOrEmpty(Rebuild.PreviousSlot) ? Rebuild.PreviousSlot! : Context.Loc.T("previous slot");
            Context.Confirm("Roll Back Rebuild", Context.Loc.T("Roll back to the previous build ({{slot}})? If the last rebuild changed the database schema, the pre-rebuild backup is restored and any data written since the cutover is permanently lost. The server then restarts on the previous build.", LocalizationArgs.Of("slot", slot)), () =>
            {
                ScreenOps.Run(Context, () => Context.Client.RollbackServerAsync(), s =>
                {
                    if (s != null) Rebuild = s;
                    string target = s != null && !String.IsNullOrEmpty(s.PreviousSlot) ? s.PreviousSlot! : Context.Loc.T("previous slot");
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Rolling back to {{slot}}; the dashboard will reconnect shortly.", LocalizationArgs.Of("slot", target));
                }, "Rollback failed");
            }, "Roll Back");
        }

        #endregion
    }
}
