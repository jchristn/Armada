namespace Armada.Tui.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Widgets;
    using TUIKit.Modals;

    /// <summary>
    /// Notifications and toasts (W1.10): the dashboard's entity-change notifications with the same text and severity
    /// mapping, a persistent history of the latest 100 (unread state, mark read, mark all read, clear), toasts with
    /// optional action keys (5 s auto-dismiss), the terminal bell, and an OS notification hook used while the terminal
    /// is unfocused. Persisted to <see cref="TuiPaths.NotificationsFile"/>. The active toasts live in a TUIKit
    /// <see cref="NotificationCenter"/> (<see cref="Toasts"/>: expiry, coalescing of repeats with a count, dismissal);
    /// Armada draws them itself (<c>ToastLayer</c>). Call on the UI loop thread.
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
            get { return Toasts.DefaultTimeoutMilliseconds; }
            set { Toasts.DefaultTimeoutMilliseconds = Math.Clamp(value, 1000, 60000); }
        }

        /// <summary>
        /// The active toasts: a TUIKit <see cref="NotificationCenter"/> that expires them, coalesces a toast raised
        /// again while it shows (<see cref="NotificationCenter.CoalesceRepeats"/>, with a repeat count), and dismisses
        /// them. Its own history is off; <see cref="History"/> is the persisted one. Never null.
        /// </summary>
        public NotificationCenter Toasts { get; } = CreateToastCenter();

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
        /// Opens a route (set by the app) so notification toasts can carry an Open action.
        /// </summary>
        public Action<string>? RouteOpener { get; set; } = null;

        /// <summary>
        /// Raised after history or toasts change.
        /// </summary>
        public event EventHandler? Changed;

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Json = CreateJson();
        private static readonly Dictionary<string, NotificationSeverityEnum> _SeverityByName = BuildSeverityMap();
        private readonly List<NotificationEntry> _History = new List<NotificationEntry>();
        private readonly Dictionary<NotificationAction, Action?> _ToastActions = new Dictionary<NotificationAction, Action?>();
        private readonly Dictionary<string, string> _LastSeen = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly IClock _Clock;
        private readonly ITextLocalizer _Loc;
        private readonly ITerminalOutput? _Terminal;
        private readonly IOsNotifier? _Os;
        private readonly string? _FilePath;

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
            Toasts.Changed += RaiseChanged;
            Load();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The dashboard's severity for a status name (<c>statusToSeverity</c>), by exact case-insensitive name only.
        /// Composed or unknown text (for example "Succeeded / Failed") is informational; use the typed overloads.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Severity.</returns>
        public static NotificationSeverityEnum SeverityFor(string? status)
        {
            if (String.IsNullOrWhiteSpace(status)) return NotificationSeverityEnum.Info;
            return _SeverityByName.TryGetValue(status!.Trim(), out NotificationSeverityEnum severity) ? severity : NotificationSeverityEnum.Info;
        }

        /// <summary>
        /// Notification severity for a typed status severity (running states are informational).
        /// </summary>
        /// <param name="severity">Status severity.</param>
        /// <returns>Notification severity.</returns>
        public static NotificationSeverityEnum SeverityFor(StatusSeverityEnum severity)
        {
            switch (severity)
            {
                case StatusSeverityEnum.Success: return NotificationSeverityEnum.Success;
                case StatusSeverityEnum.Error: return NotificationSeverityEnum.Error;
                case StatusSeverityEnum.Warning: return NotificationSeverityEnum.Warning;
                default: return NotificationSeverityEnum.Info;
            }
        }

        /// <summary>
        /// Notification severity for an entity change, computed from the typed status values of the event (for
        /// deployments the deployment status and the verification status together, so a succeeded deployment whose
        /// verification failed is an error). Falls back to the exact-name map when the status is not a known name.
        /// </summary>
        /// <param name="eventType">Socket event type (for example <see cref="ArmadaEventTypes.MissionChanged"/>).</param>
        /// <param name="data">Event payload.</param>
        /// <returns>Severity.</returns>
        public static NotificationSeverityEnum SeverityFor(string eventType, EntityChangedEvent data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Enum? typed = null;
            switch (eventType)
            {
                case ArmadaEventTypes.MissionChanged: typed = data.MissionStatus; break;
                case ArmadaEventTypes.VoyageChanged: typed = data.VoyageStatus; break;
                case ArmadaEventTypes.CaptainChanged: typed = data.CaptainState; break;
                case ArmadaEventTypes.ObjectiveChanged: typed = data.ObjectiveStatus; break;
                case ArmadaEventTypes.IncidentChanged: typed = data.IncidentStatus; break;
                case ArmadaEventTypes.DeploymentChanged:
                    DeploymentStatusEnum? deployment = data.DeploymentStatus;
                    if (deployment != null) return SeverityFor(StatusBadge.Severity(deployment.Value, data.DeploymentVerificationStatus));
                    break;
            }

            if (typed != null) return SeverityFor(StatusBadge.Severity(typed));
            return SeverityFor(eventType == ArmadaEventTypes.CaptainChanged ? (data.State ?? data.Status) : data.Status);
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
            NotificationSeverityEnum severity = SeverityFor(message.Type, data);
            if (asset == "Deployment" && !String.IsNullOrEmpty(data.VerificationStatus)) status += " / " + data.VerificationStatus;
            string name = asset == "Captain" ? (data.Name ?? data.Id ?? "") : (data.Title ?? data.Id ?? "");
            if (asset == "Deployment" && data.DeploymentStatus == DeploymentStatusEnum.PendingApproval)
                name = DeploymentApprovalLabel.Format(data.EnvironmentName, data.Title, data.Id, (template, args) => _Loc.T(template, args));
            return PushEntityChange(asset, data.Id ?? "", name, status!, severity);
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
            return PushEntityChange(assetType, id, name, status, SeverityFor(status));
        }

        /// <summary>
        /// Record an entity status change with an explicit severity (deduplicated per entity and status) and raise a
        /// toast. <paramref name="status"/> is display text only.
        /// </summary>
        /// <param name="assetType">Asset type.</param>
        /// <param name="id">Entity id.</param>
        /// <param name="name">Name or title.</param>
        /// <param name="status">Status display text.</param>
        /// <param name="severity">Severity computed from the typed status.</param>
        /// <returns>True when recorded (false for a repeat).</returns>
        public bool PushEntityChange(string assetType, string id, string name, string status, NotificationSeverityEnum severity)
        {
            string key = assetType + ":" + id;
            if (_LastSeen.TryGetValue(key, out string? seen) && seen == status) return false;
            _LastSeen[key] = status;
            string truncated = name.Length > 80 ? name.Substring(0, 80) + "..." : name;
            NotificationEntry entry = new NotificationEntry();
            entry.Severity = severity;
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
                Action<string>? opener = RouteOpener;
                bool actionable = route != null && opener != null;
                Toast(entry.Severity, Render(entry), actionable ? "Open" : null, actionable ? () => opener!(route!) : null);
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
            long now = NowMilliseconds();
            List<NotificationAction>? actions = null;
            if (actionLabel != null)
            {
                // TUIKit coalesces a repeat only when it carries the same action instances, and every caller builds a
                // fresh callback, so a repeat of a showing toast with the same action label reuses that toast's action
                // (whose callback becomes the newest one, as before).
                NotificationAction? shared = null;
                foreach (Notification showing in Toasts.Active(now))
                {
                    if (showing.Severity == ToTuiKit(severity) && String.Equals(showing.Text, text ?? "", StringComparison.Ordinal)
                        && showing.Title == null && showing.Actions.Count == 1 && String.Equals(showing.Actions[0].Label, actionLabel, StringComparison.Ordinal))
                    {
                        shared = showing.Actions[0];
                        break;
                    }
                }

                if (shared == null)
                {
                    NotificationAction? created = null;
                    created = new NotificationAction(actionLabel, () =>
                    {
                        if (created != null && _ToastActions.TryGetValue(created, out Action? callback) && callback != null) callback();
                    });
                    shared = created;
                }

                _ToastActions[shared] = action;
                actions = new List<NotificationAction> { shared };
            }

            Notification toast = Toasts.Add(text ?? "", ToTuiKit(severity), now, null, null, actions);
            return View(toast);
        }

        /// <summary>
        /// Toasts that have not expired, oldest first (expired ones are dropped).
        /// </summary>
        /// <returns>Toasts.</returns>
        public IReadOnlyList<ToastEntry> ActiveToasts()
        {
            IReadOnlyList<Notification> active = Toasts.Active(NowMilliseconds());
            List<ToastEntry> toasts = new List<ToastEntry>(active.Count);
            HashSet<NotificationAction> live = new HashSet<NotificationAction>();
            for (int i = active.Count - 1; i >= 0; i--)
            {
                toasts.Add(View(active[i]));
                foreach (NotificationAction a in active[i].Actions) live.Add(a);
            }

            foreach (NotificationAction stale in _ToastActions.Keys.Where(a => !live.Contains(a)).ToList()) _ToastActions.Remove(stale);
            return toasts;
        }

        /// <summary>
        /// Run the newest actionable toast's action and dismiss it.
        /// </summary>
        /// <returns>True when an action ran.</returns>
        public bool RunLatestToastAction()
        {
            foreach (Notification toast in Toasts.Active(NowMilliseconds()))
            {
                if (toast.Actions.Count == 0 || !_ToastActions.TryGetValue(toast.Actions[0], out Action? callback) || callback == null) continue;
                Toasts.InvokeAction(toast, 0);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Dismiss every toast.
        /// </summary>
        public void DismissToasts()
        {
            Toasts.DismissAll();
            _ToastActions.Clear();
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

        private static NotificationCenter CreateToastCenter()
        {
            NotificationCenter center = new NotificationCenter();
            center.DefaultTimeoutMilliseconds = 5000;
            center.MaxConcurrent = 100;
            center.HistoryLimit = 0;
            center.CoalesceRepeats = true;
            return center;
        }

        private static NotificationSeverity ToTuiKit(NotificationSeverityEnum severity)
        {
            switch (severity)
            {
                case NotificationSeverityEnum.Success: return NotificationSeverity.Success;
                case NotificationSeverityEnum.Warning: return NotificationSeverity.Warning;
                case NotificationSeverityEnum.Error: return NotificationSeverity.Error;
                default: return NotificationSeverity.Info;
            }
        }

        private static NotificationSeverityEnum FromTuiKit(NotificationSeverity severity)
        {
            switch (severity)
            {
                case NotificationSeverity.Success: return NotificationSeverityEnum.Success;
                case NotificationSeverity.Warning: return NotificationSeverityEnum.Warning;
                case NotificationSeverity.Error: return NotificationSeverityEnum.Error;
                default: return NotificationSeverityEnum.Info;
            }
        }

        private long NowMilliseconds()
        {
            return (long)(_Clock.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
        }

        private ToastEntry View(Notification toast)
        {
            string? label = toast.Actions.Count > 0 ? toast.Actions[0].Label : null;
            Action? action = null;
            if (toast.Actions.Count > 0) _ToastActions.TryGetValue(toast.Actions[0], out action);
            DateTime expires = DateTime.UnixEpoch.AddMilliseconds(toast.LastRaisedAtMilliseconds + toast.TimeoutMilliseconds);
            string suffix = toast.RepeatCount > 1 ? String.Format(CultureInfo.InvariantCulture, Toasts.RepeatSuffixFormat, toast.RepeatCount) : "";
            return new ToastEntry(toast.Id, FromTuiKit(toast.Severity), toast.Text, expires, label, action, toast.RepeatCount, suffix);
        }

        private static Dictionary<string, NotificationSeverityEnum> BuildSeverityMap()
        {
            Dictionary<string, NotificationSeverityEnum> map = new Dictionary<string, NotificationSeverityEnum>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in new string[] { "Completed", "Complete", "Landed", "Passed", "Succeeded" })
                map[name] = NotificationSeverityEnum.Success;
            foreach (string name in new string[] { "Failed", "Error", "LandingFailed", "VerificationFailed", "DependencyFailed", "MissionFailed" })
                map[name] = NotificationSeverityEnum.Error;
            foreach (string name in new string[] { "Cancelled", "Stalled", "Stopping", "RolledBack", "Denied", "ReviewDenied", "AccessDenied" })
                map[name] = NotificationSeverityEnum.Warning;
            return map;
        }

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
