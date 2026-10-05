namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Approvals;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Needs You (W3.2, <c>/inbox</c>), the dashboard's Inbox page: KPIs (Total, Critical, Warning, Unread alerts), the
    /// inbox items (severity, title, detail; <c>Enter</c> opens the item), and Recent alerts (the latest unread
    /// notifications, up to eight; <c>Enter</c> marks one read and opens it; <c>m</c> marks all read). The same inbox
    /// also feeds the Approvals center and the sidebar badge. Auto-refresh defaults to 15 s and any socket message
    /// re-polls the badge. Not thread-safe.
    /// </summary>
    public class InboxScreen : OpsScreen
    {
        #region Public-Members

        /// <summary>
        /// Inbox items.
        /// </summary>
        public ArmadaGrid<InboxItem> Items { get; }

        /// <summary>
        /// Unread alerts.
        /// </summary>
        public ArmadaGrid<NotificationEntry> Alerts { get; }

        /// <summary>
        /// Items from the last load.
        /// </summary>
        public List<InboxItem> Loaded { get; private set; } = new List<InboxItem>();

        /// <summary>
        /// True while loading.
        /// </summary>
        public bool Loading { get; private set; } = true;

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                hints.Add(new KeyValuePair<string, string>("Enter", "Open"));
                if (UnreadAlerts().Count > 0) hints.Add(new KeyValuePair<string, string>("m", "Mark all read"));
                return hints;
            }
        }

        #endregion

        #region Private-Members


        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public InboxScreen(RouteMatch route, TuiContext context)
            : base(route, context, "InboxScreen", "Needs You")
        {
            Items = new ArmadaGrid<InboxItem>(i => i.Href + "|" + i.Kind + "|" + i.EntityId + "|" + i.Title);
            Items.MultiSelect = false;
            Items.ShowPagingBar = false;
            Items.PageSize = 250;
            Items.EmptyText = "Loading...";
            Items.Dispatcher = context.Dispatcher;
            Items.AddColumn(new GridColumn<InboxItem>("severity", "Severity", i => SeverityLabel(i.Severity)) { Width = 12, Style = (i, t) => SeverityStyle(i.Severity, t) });
            Items.AddColumn(new GridColumn<InboxItem>("title", "Title", i => TitleFor(Context.Loc, i)) { Weight = 3 });
            Items.AddColumn(new GridColumn<InboxItem>("detail", "Detail", i => i.Detail) { Weight = 3 });
            Items.Activated += (s, i) => Open(i);

            Alerts = new ArmadaGrid<NotificationEntry>(n => n.Id);
            Alerts.MultiSelect = false;
            Alerts.ShowPagingBar = false;
            Alerts.Dispatcher = context.Dispatcher;
            Alerts.AddColumn(new GridColumn<NotificationEntry>("title", "Title", n => Context.Notifications.RenderTitle(n)) { Weight = 2 });
            Alerts.AddColumn(new GridColumn<NotificationEntry>("message", "Message", n => Context.Notifications.Render(n)) { Weight = 4 });
            Alerts.AddColumn(new GridColumn<NotificationEntry>("when", "When", n => Context.Loc.FormatRelative(n.TimestampUtc, Context.Clock.UtcNow)) { Width = 16 });
            Alerts.Activated += (s, n) => OpenAlert(n);
            AddChild(Items);
            AddChild(Alerts);
            Scope.Focus(Items);

            context.Notifications.Changed += NotificationsChanged;
            Track(new OpsCallbackDisposable(() => context.Notifications.Changed -= NotificationsChanged));
            SyncAlerts();
            Load();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Display title of an inbox item: a deployment approval reads "Deploy to {environment}: {title}" in the
        /// active locale (the same label as the Approvals queue and the confirmation dialogs); other kinds show the
        /// server's title.
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="item">Inbox item.</param>
        /// <returns>Title.</returns>
        public static string TitleFor(ITextLocalizer? loc, InboxItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (item.Kind == InboxItemKinds.DeploymentApproval) return DeploymentApprovalText.ForInbox(loc, item);
            return item.Title;
        }

        /// <summary>
        /// The latest unread notifications (at most eight), as the dashboard shows.
        /// </summary>
        /// <returns>Entries.</returns>
        public List<NotificationEntry> UnreadAlerts()
        {
            return Context.Notifications.History.Where(n => !n.Read).Take(8).ToList();
        }

        /// <summary>
        /// Load the inbox.
        /// </summary>
        public void Load()
        {
            Loading = true;
            Call((c, t) => c.GetInboxAsync(t), result =>
            {
                Loading = false;
                Loaded = result ?? new List<InboxItem>();
                Items.EmptyText = "You are all caught up.";
                Items.SetLocalRows(Loaded);
                Context.Status.NudgeInbox();
            }, null, ex =>
            {
                Loading = false;
                Items.SetError(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to load inbox.") : ex.Message);
            });
        }

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return Load;
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            List<ArmadaCommand> list = new List<ArmadaCommand>();
            ArmadaCommand open = new ArmadaCommand("inbox.open", "Open the selected item", CommandMenuEnum.Actions, () =>
            {
                if (ReferenceEquals(Scope.Focused, Alerts) && Alerts.Current != null) OpenAlert(Alerts.Current);
                else if (Items.Current != null) Open(Items.Current);
            });
            open.Group = Title;
            open.Dispatch = false;
            list.Add(open);
            ArmadaCommand mark = new ArmadaCommand("inbox.mark-all-read", "Mark all read", CommandMenuEnum.Actions, () => Context.Notifications.MarkAllRead(), "m");
            mark.Group = Title;
            mark.Dispatch = false;
            mark.IsEnabled = () => UnreadAlerts().Count > 0;
            list.Add(mark);
            return list;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None && key.Rune == 'm')
            {
                if (UnreadAlerts().Count == 0) return false;
                Context.Notifications.MarkAllRead();
                return true;
            }

            return Scope.HandleKey(key);
        }

        /// <inheritdoc />
        public override void OnDeactivated()
        {
            Context.Notifications.Changed -= NotificationsChanged;
            base.OnDeactivated();
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 20 || height < 6) return;
            int y = 0;
            int x = SurfaceText.Draw(surface, 0, y, Tr("Needs You"), Theme.Accent.WithAttribute(CellAttributes.Bold, true), width);
            SurfaceText.Draw(surface, x + 2, y, Tr("Everything across the fleet that is waiting on a decision or intervention from you."), Theme.Muted, width - x - 2);
            y++;
            string refresh = Context.Refresh.StatusText;
            string right = (refresh.Length > 0 ? "[" + Tr(refresh) + "]  " : "") + "F5 " + Tr("Refresh");
            SurfaceText.Draw(surface, Math.Max(0, width - TextCells.Width(right)), y, right, Theme.Muted, width);
            int critical = Loaded.Count(i => i.Severity == InboxSeverityEnum.Critical);
            int warning = Loaded.Count(i => i.Severity == InboxSeverityEnum.Warning);
            int kx = 0;
            kx += Kpi(surface, kx, y, "Total", Loaded.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), Theme.Text, width);
            kx += Kpi(surface, kx, y, "Critical", critical.ToString(System.Globalization.CultureInfo.InvariantCulture), Theme.Error, width);
            kx += Kpi(surface, kx, y, "Warning", warning.ToString(System.Globalization.CultureInfo.InvariantCulture), Theme.Warning, width);
            Kpi(surface, kx, y, "Unread alerts", Context.Notifications.UnreadCount.ToString(System.Globalization.CultureInfo.InvariantCulture), Theme.Text, width);
            y += 2;

            List<NotificationEntry> alerts = UnreadAlerts();
            int alertRows = alerts.Count > 0 ? Math.Min(alerts.Count + 2, Math.Max(3, (height - y) / 3)) : 0;
            // Both lists are focus regions with a box line above and below (see RegionFrames); the alert list is only
            // a Tab stop while there are alerts.
            Alerts.Visible = alertRows > 0;
            int itemsHeight = Math.Max(2, height - y - (alertRows > 0 ? alertRows + 2 : 0));
            Rect items = new Rect(0, y, width, itemsHeight);
            if (!Loading && Loaded.Count == 0 && Items.State != GridStateEnum.Error)
            {
                // Nothing to list: the list's box holds the all-clear message.
                SurfaceText.Draw(surface, 0, y, Tr("You are all caught up."), Theme.Success.WithAttribute(CellAttributes.Bold, true), width);
                SurfaceText.Draw(surface, 0, y + 1, Tr("Nothing needs your attention right now."), Theme.Muted, width);
                Scope.Place(Items, items);
            }
            else
            {
                Scope.RenderChild(surface, Items, items);
            }

            y += itemsHeight + 1;
            if (alertRows > 0 && y < height)
            {
                SurfaceText.Draw(surface, 0, y, Tr("Recent alerts"), Theme.Accent, width);
                string mark = "m " + Tr("Mark all read");
                SurfaceText.Draw(surface, Math.Max(0, width - TextCells.Width(mark)), y, mark, Theme.Muted, width);
                y += 2;
                if (y < height) Scope.RenderChild(surface, Alerts, new Rect(0, y, width, height - y));
            }
        }

        #endregion

        #region Private-Methods

        private static string SeverityLabel(InboxSeverityEnum severity)
        {
            string marker = severity == InboxSeverityEnum.Critical ? "x " : severity == InboxSeverityEnum.Warning ? "! " : "- ";
            return marker + severity;
        }

        private static CellStyle? SeverityStyle(InboxSeverityEnum severity, Armada.Tui.Theming.ArmadaTheme theme)
        {
            if (severity == InboxSeverityEnum.Critical) return theme.Error;
            if (severity == InboxSeverityEnum.Warning) return theme.Warning;
            return theme.Muted;
        }

        private int Kpi(ISurface surface, int x, int y, string label, string value, CellStyle style, int width)
        {
            if (x >= width) return 0;
            int used = SurfaceText.Draw(surface, x, y, Tr(label) + " ", Theme.Muted, width - x);
            used += SurfaceText.Draw(surface, x + used, y, value, style.WithAttribute(CellAttributes.Bold, true), width - x - used);
            return used + 4;
        }

        private void Open(InboxItem item)
        {
            if (item == null || String.IsNullOrEmpty(item.Href)) return;
            Context.Navigate(item.Href);
        }

        private void OpenAlert(NotificationEntry entry)
        {
            if (entry == null) return;
            Context.Notifications.MarkRead(entry.Id);
            if (!String.IsNullOrEmpty(entry.Route)) Context.Navigate(entry.Route!);
        }

        private void NotificationsChanged(object? sender, EventArgs e)
        {
            Context.Dispatcher.Post(() =>
            {
                if (IsLive) SyncAlerts();
            });
        }

        private void SyncAlerts()
        {
            Alerts.SetLocalRows(UnreadAlerts());
        }

        #endregion
    }
}
