namespace Armada.Tui.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Theming;

    /// <summary>
    /// Everything the TUI remembers between runs (<c>~/.armada/tui.json</c>). Secrets are never stored here; tokens go to the credential store.
    /// </summary>
    public class TuiPreferences
    {
        #region Public-Members

        /// <summary>
        /// File format version.
        /// </summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>
        /// Saved server profiles. Never null.
        /// </summary>
        public List<ServerProfile> Profiles { get; set; } = new List<ServerProfile>();

        /// <summary>
        /// Name of the profile used last, or null.
        /// </summary>
        public string? ActiveProfile { get; set; } = null;

        /// <summary>
        /// Theme mode. Default Auto.
        /// </summary>
        public ThemeModeEnum Theme { get; set; } = ThemeModeEnum.Auto;

        /// <summary>
        /// Locale code, or null to follow the environment.
        /// </summary>
        public string? Locale { get; set; } = null;

        /// <summary>
        /// Show the sidebar. Default true.
        /// </summary>
        public bool SidebarVisible { get; set; } = true;

        /// <summary>
        /// Sidebar sections collapsed by key. Never null.
        /// </summary>
        public Dictionary<string, bool> CollapsedSections { get; set; } = new Dictionary<string, bool>();

        /// <summary>
        /// Show the Ask dock. Default false.
        /// </summary>
        public bool AskDockVisible { get; set; } = false;

        /// <summary>
        /// Route open when the TUI exited, or null.
        /// </summary>
        public string? LastRoute { get; set; } = null;

        /// <summary>
        /// Last open Ask thread, or null.
        /// </summary>
        public string? LastAskThreadId { get; set; } = null;

        /// <summary>
        /// Captain chosen before a thread exists (the dashboard's armada_ask_captain), or null.
        /// </summary>
        public string? AskDraftCaptainId { get; set; } = null;

        /// <summary>
        /// Show captain thinking in Ask (armada_ask_show_thinking).
        /// </summary>
        public bool AskShowThinking { get; set; } = false;

        /// <summary>
        /// Grid preferences keyed by table id. Never null.
        /// </summary>
        public Dictionary<string, TablePreferences> Tables { get; set; } = new Dictionary<string, TablePreferences>();

        /// <summary>
        /// Auto-refresh interval in seconds keyed by screen (0 is off). Never null.
        /// </summary>
        public Dictionary<string, int> RefreshIntervals { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Ring the terminal bell for approvals and failures. Default true.
        /// </summary>
        public bool TerminalBell { get; set; } = true;

        /// <summary>
        /// OS notification hook used while the terminal is unfocused. Default Off.
        /// </summary>
        public OsNotificationModeEnum OsNotifications { get; set; } = OsNotificationModeEnum.Off;

        /// <summary>
        /// The setup wizard was completed or skipped.
        /// </summary>
        public bool SetupCompleted { get; set; } = false;

        /// <summary>
        /// Rebound chords keyed by command id. Never null.
        /// </summary>
        public Dictionary<string, string> KeyBindings { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Workspace vessels opened most recently, newest first (at most eight; the dashboard keeps these in local
        /// storage). Never null.
        /// </summary>
        public List<string> WorkspaceRecentVessels { get; set; } = new List<string>();

        /// <summary>
        /// Per-vessel Workspace state (expanded folders and recent files) keyed by vessel id. Never null.
        /// </summary>
        public Dictionary<string, WorkspacePreferences> Workspaces { get; set; } = new Dictionary<string, WorkspacePreferences>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TuiPreferences()
        {
        }

        #endregion
    }
}
