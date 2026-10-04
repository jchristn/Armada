namespace Armada.Tui.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Client.Socket;

    /// <summary>
    /// Notifications and toasts (W1.10): the dashboard's entity-change notifications with the same text and severity
    /// mapping, a persistent history of the latest 100 (unread state, mark read, mark all read, clear), toasts with
    /// optional action keys (5 s auto-dismiss), the terminal bell, and an OS notification hook used while the terminal
    /// is unfocused. Persisted to <see cref="TuiPaths.NotificationsFile"/>. Call on the UI loop thread.
    /// </summary>
    public class NotificationService
    {
        #region Public-Members

        /// <summary>
        /// Maximum history entries (100, like the dashboard).
        /// </summary>
        public const int MaxHistory = 100;

        /// <summary>
        /// Toast lifetime in milliseconds. Default 5000; clamped to 1000..60000.
        /// </summary>
        public int ToastTimeoutMs
        {
            get { return _ToastTimeoutMs; }
            set { _ToastTimeoutMs = Math.Clamp(value, 1000, 60000); }
        }

        /// <summary>
        /// History, newest first. Never null.
        /// </summary>
        public IReadOnlyList<NotificationEntry> History
        {
            get { return _History; }
        }

        /// <summary>
        /// Unread history entries.
        /// </summary>
        public int UnreadCount
        {
            get { return _History.Count(n => !n.Read); }
        }

        /// <summary>
        /// Ring the bell for errors and approvals.
        /// </summary>
        public bool BellEnabled { get; set; } = true;

        /// <summary>
        /// OS notification mechanism used while the terminal is unfocused.
        /// </summary>
        public OsNotificationModeEnum OsMode { get; set; } = OsNotificationModeEnum.Off;

        /// <summary>
        /// Terminal focus state (from TUIKit focus reporting). Default true.
        /// </summary>
        public bool TerminalFocused { get; set; } = true;

        /// <summary>
        /// Raised after history or toasts change.
        /// </summary>
        public event EventHandler? Changed;

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Json = CreateJson();
        private readonly List<NotificationEntry> _History = new List<NotificationEntry>();
        private readonly List<ToastEntry> _Toasts = new List<ToastEntry>();
        private readonly Dictionary<string, string> _LastSeen = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly IClock _Clock;
        private readonly ITextLocalizer _Loc;
        private readonly ITerminalOutput? _Terminal;
        private readonly IOsNotifier? _Os;
        private readonly string? _FilePath;
        private int _ToastTimeoutMs = 5000;
        private long _NextToastId = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="clock">Clock.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="terminal">Terminal for the bell, or null.</param>
        /// <param name="os">OS notifier, or null.</param>
        /// <param name="filePath">History file, or null to keep history in memory.</param>
        public NotificationService(IClock clock, ITextLocalizer localizer, ITerminalOutput? terminal, IOsNotifier? os, string? filePath)
        {
            _Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _Loc = localizer ?? throw new ArgumentNullException(nameof(localizer));
            _Terminal = terminal;
            _Os = os;
            _FilePath = filePath;
            Load();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The dashboard's severity for a status (<c>statusToSeverity</c>).
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Severity.</returns>
        public static NotificationSeverityEnum SeverityFor(string? status)
        {
            if (String.IsNullOrEmpty(status)) return NotificationSeverityEnum.Info;
            string s = status!.ToLowerInvariant();
            if (s == "completed" || s == "complete" || s == "landed" || s == "passed" || s.Contains("succeeded")) return NotificationSeverityEnum.Success;
            if (s == "failed" || s == "error" || s.Contains("failed")) return NotificationSeverityEnum.Error;
            if (s == "cancelled" || s == "stalled" || s == "stopping" || s.Contains("rolledback") || s.Contains("denied")) return NotificationSeverityEnum.Warning;
            return NotificationSeverityEnum.Info;
        }

        /// <summary>
        /// Handle a socket message: the six entity-change events become notifications and toasts (dashboard parity).
        /// </summary>
        /// <param name="message">Message.</param>
        /// <returns>True when a notification was raised.</returns>
        public bool HandleSocketMessage(ArmadaSocketMessage message)
        {
            if (message == null) return false;
            string? asset = message.Type switch
            {
                ArmadaEventTypes.MissionChanged => "Mission",
                ArmadaEventTypes.VoyageChanged => "Voyage",
                ArmadaEventTypes.CaptainChanged => "Captain",
                ArmadaEventTypes.DeploymentChanged => "Deployment",
                ArmadaEventTypes.ObjectiveChanged => "Objective",
                ArmadaEventTypes.IncidentChanged => "Incident",
                _ => null
            };
            if (asset == null) return false;
            EntityChangedEvent? data = message.GetData<EntityChangedEvent>();
            if (data == null) return false;
            string? status = asset == "Captain" ? (data.State ?? data.Status) : data.Status;
            if (String.IsNullOrEmpty(status)) return false;
            if (asset == "Deployment" && !String.IsNullOrEmpty(data.VerificationStatus)) status += " / " + data.VerificationStatus;
            string name = asset == "Captain" ? (data.Name ?? data.Id ?? "") : (data.Title ?? data.Id ?? "");
            return PushEntityChange(asset, data.Id ?? "", name, status!);
        }

        /// <summary>
        /// Record an entity status change (deduplicated per entity and status) and raise a toast.
        /// </summary>
        /// <param name="assetType">Asset type.</param>
        /// <param name="id">Entity id.</param>
        /// <param name="name">Name or title.</param>
        /// <param name="status">Status.</param>
        /// <returns>True when recorded (false for a repeat).</returns>
        public bool PushEntityChange(string assetType, string id, string name, string status)
        {
            string key = assetType + ":" + id;
            if (_LastSeen.TryGetValue(key, out string? seen) && seen == status) return false;
            _LastSeen[key] = status;
            string truncated = name.Length > 80 ? name.Substring(0, 80) + "..." : name;
            NotificationEntry entry = new NotificationEntry();
            entry.Severity = SeverityFor(status);
            entry.AssetType = assetType;
            entry.Name = truncated;
            entry.Status = status;
            entry.Title = assetType + " " + status;
            entry.Message = assetType + " \"" + truncated + "\" - " + status;
            entry.TimestampUtc = _Clock.UtcNow;
            entry.Route = RouteFor(assetType, id);
            Add(entry, true);
            return true;
        }

        /// <summary>
        /// Add a free-form notification to history and optionally toast it.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="toast">Also toast.</param>
        public void Add(NotificationEntry entry, bool toast)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            _History.Insert(0, entry);
            if (_History.Count > MaxHistory) _History.RemoveRange(MaxHistory, _History.Count - MaxHistory);
            Save();
            if (toast)
            {
                string? route = entry.Route;
                Toast(entry.Severity, Render(entry), route != null ? "Open" : null, null);
            }

            if (entry.Severity == NotificationSeverityEnum.Error) Attention(Render(entry));
            else RaiseChanged();
        }

        /// <summary>
        /// Show a toast.
        /// </summary>
        /// <param name="severity">Severity.</param>
        /// <param name="text">Text (already translated).</param>
        /// <param name="actionLabel">English action label, or null.</param>
        /// <param name="action">Action, or null.</param>
        /// <returns>The toast.</returns>
        public ToastEntry Toast(NotificationSeverityEnum severity, string text, string? actionLabel = null, Action? action = null)
        {
            ToastEntry toast = new ToastEntry();
            toast.Id = ++_NextToastId;
            toast.Severity = severity;
            toast.Text = text ?? "";
            toast.ExpiresUtc = _Clock.UtcNow.AddMilliseconds(_ToastTimeoutMs);
            toast.ActionLabel = actionLabel;
            toast.Action = action;
            _Toasts.Add(toast);
            RaiseChanged();
            return toast;
        }

        /// <summary>
        /// Toasts that have not expired, oldest first (expired ones are dropped).
        /// </summary>
        /// <returns>Toasts.</returns>
        public IReadOnlyList<ToastEntry> ActiveToasts()
        {
            DateTime now = _Clock.UtcNow;
            _Toasts.RemoveAll(t => t.ExpiresUtc <= now);
            return _Toasts.ToList();
        }

        /// <summary>
        /// Run the newest actionable toast's action and dismiss it.
        /// </summary>
        /// <returns>True when an action ran.</returns>
        public bool RunLatestToastAction()
        {
            ToastEntry? toast = ActiveToasts().LastOrDefault(t => t.Action != null);
            if (toast == null || toast.Action == null) return false;
            _Toasts.Remove(toast);
            toast.Action();
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// Dismiss every toast.
        /// </summary>
        public void DismissToasts()
        {
            _Toasts.Clear();
            RaiseChanged();
        }

        /// <summary>
        /// Mark one entry read.
        /// </summary>
        /// <param name="id">Entry id.</param>
        public void MarkRead(string id)
        {
            NotificationEntry? entry = _History.FirstOrDefault(n => n.Id == id);
            if (entry == null || entry.Read) return;
            entry.Read = true;
            Save();
            RaiseChanged();
        }

        /// <summary>
        /// Mark all read.
        /// </summary>
        public void MarkAllRead()
        {
            foreach (NotificationEntry entry in _History) entry.Read = true;
            Save();
            RaiseChanged();
        }

        /// <summary>
        /// Clear history (and the duplicate filter).
        /// </summary>
        public void Clear()
        {
            _History.Clear();
            _LastSeen.Clear();
            Save();
            RaiseChanged();
        }

        /// <summary>
        /// Translated one-line text of an entry (re-rendered from its semantic parts).
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <returns>Text.</returns>
        public string Render(NotificationEntry entry)
        {
            if (entry.AssetType != null && entry.Status != null)
                return _Loc.T(entry.AssetType) + " \"" + (entry.Name ?? "") + "\" - " + _Loc.T(entry.Status);
            return _Loc.T(entry.Message);
        }

        /// <summary>
        /// Translated title of an entry.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <returns>Title.</returns>
        public string RenderTitle(NotificationEntry entry)
        {
            if (entry.AssetType != null && entry.Status != null) return _Loc.T(entry.AssetType) + " " + _Loc.T(entry.Status);
            return _Loc.T(entry.Title);
        }

        /// <summary>
        /// Escalate something that needs the user: ring the bell (when enabled) and raise an OS notification when the
        /// terminal is unfocused.
        /// </summary>
        /// <param name="text">Text.</param>
        public void Attention(string text)
        {
            if (BellEnabled && _Terminal != null && _Terminal.IsInteractive) _Terminal.Write("\u0007");
            if (!TerminalFocused && _Os != null) _Os.Notify(OsMode, "Armada", text ?? "");
            RaiseChanged();
        }

        #endregion

        #region Private-Methods

        private static string? RouteFor(string assetType, string id)
        {
            if (String.IsNullOrEmpty(id)) return null;
            switch (assetType)
            {
                case "Mission": return "/missions/" + id;
                case "Voyage": return "/voyages/" + id;
                case "Captain": return "/captains/" + id;
                case "Deployment": return "/deployments/" + id;
                case "Objective": return "/backlog/" + id;
                case "Incident": return "/incidents/" + id;
                default: return null;
            }
        }

        private void Load()
        {
            if (String.IsNullOrEmpty(_FilePath) || !File.Exists(_FilePath)) return;
            try
            {
                List<NotificationEntry>? loaded = JsonSerializer.Deserialize<List<NotificationEntry>>(File.ReadAllText(_FilePath!), _Json);
                if (loaded != null) _History.AddRange(loaded.Take(MaxHistory));
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
            {
                // A corrupt history is not fatal.
            }
        }

        private void Save()
        {
            if (String.IsNullOrEmpty(_FilePath)) return;
            try
            {
                string? dir = Path.GetDirectoryName(_FilePath);
                if (!String.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir!);
                File.WriteAllText(_FilePath!, JsonSerializer.Serialize(_History, _Json));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Best effort.
            }
        }

        private void RaiseChanged()
        {
            EventHandler? handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private static JsonSerializerOptions CreateJson()
        {
            JsonSerializerOptions o = new JsonSerializerOptions();
            o.WriteIndented = true;
            o.Converters.Add(new JsonStringEnumConverter());
            return o;
        }

        #endregion
    }
}
