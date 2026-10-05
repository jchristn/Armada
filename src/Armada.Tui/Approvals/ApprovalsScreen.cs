namespace Armada.Tui.Approvals
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The Approvals center (W3.3, route <c>/approvals</c>, <c>Ctrl+A</c>): one queue of everything waiting on the user,
    /// sorted by urgency, with single-key decisions per item: Ask proposals (<c>a</c> approve, <c>r</c> reject,
    /// <c>x</c> arguments), mission reviews (<c>a</c> approve, <c>c</c> conditionally approve, <c>m</c> more work
    /// required, <c>d</c> deny, through the Resolve Review dialog), deployments pending approval (<c>a</c> approve,
    /// <c>d</c> deny, both confirmed), failed landings (<c>l</c> retry landing), stalled captains (<c>s</c> stop,
    /// <c>R</c> recall, <c>t</c> restart, confirmed), and CLI permission prompts (<c>a</c> allow once, <c>A</c> allow
    /// and remember with an editable rule and scope, <c>d</c> deny with an optional message; requests the user cannot
    /// decide say an admin must). <c>Enter</c> opens the item's screen; <c>F5</c> re-polls the
    /// inbox. Not thread-safe.
    /// </summary>
    public class ApprovalsScreen : ScreenBase
    {
        #region Public-Members

        /// <summary>
        /// Decision executor.
        /// </summary>
        public ApprovalActions Actions { get; }

        /// <summary>
        /// Cursor row.
        /// </summary>
        public int Cursor { get; private set; } = 0;

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        /// <inheritdoc />
        public override string Title
        {
            get { return "Approvals"; }
        }

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                ApprovalItem? item = Current();
                hints.Add(new KeyValuePair<string, string>("Enter", "Open"));
                if (item != null)
                {
                    foreach (KeyValuePair<string, string> k in KeysFor(item)) hints.Add(k);
                }

                return hints;
            }
        }

        #endregion

        #region Private-Members

        private string? _CursorKey = null;
        private int _Top = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public ApprovalsScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
            Actions = new ApprovalActions(context);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// English label of a kind.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <returns>Label.</returns>
        public static string KindLabel(ApprovalKindEnum kind)
        {
            switch (kind)
            {
                case ApprovalKindEnum.AskProposal: return "Ask proposal";
                case ApprovalKindEnum.MissionReview: return "Mission review";
                case ApprovalKindEnum.DeploymentApproval: return "Deployment approval";
                case ApprovalKindEnum.FailedLanding: return "Failed landing";
                case ApprovalKindEnum.StalledCaptain: return "Stalled captain";
                case ApprovalKindEnum.CliPermission: return "CLI permission";
                default: return kind.ToString();
            }
        }

        /// <summary>
        /// Decision keys of a kind (key, English label).
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <returns>Keys.</returns>
        public static List<KeyValuePair<string, string>> KeysFor(ApprovalKindEnum kind)
        {
            List<KeyValuePair<string, string>> keys = new List<KeyValuePair<string, string>>();
            switch (kind)
            {
                case ApprovalKindEnum.AskProposal:
                    keys.Add(new KeyValuePair<string, string>("a", "Approve"));
                    keys.Add(new KeyValuePair<string, string>("r", "Reject"));
                    keys.Add(new KeyValuePair<string, string>("x", "Arguments"));
                    break;
                case ApprovalKindEnum.MissionReview:
                    keys.Add(new KeyValuePair<string, string>("a", "Approve"));
                    keys.Add(new KeyValuePair<string, string>("c", "Conditionally Approve"));
                    keys.Add(new KeyValuePair<string, string>("m", "More Work Required"));
                    keys.Add(new KeyValuePair<string, string>("d", "Deny"));
                    break;
                case ApprovalKindEnum.DeploymentApproval:
                    keys.Add(new KeyValuePair<string, string>("a", "Approve"));
                    keys.Add(new KeyValuePair<string, string>("d", "Deny"));
                    break;
                case ApprovalKindEnum.FailedLanding:
                    keys.Add(new KeyValuePair<string, string>("l", "Retry Landing"));
                    break;
                case ApprovalKindEnum.StalledCaptain:
                    keys.Add(new KeyValuePair<string, string>("s", "Stop"));
                    keys.Add(new KeyValuePair<string, string>("R", "Recall"));
                    keys.Add(new KeyValuePair<string, string>("t", "Restart"));
                    break;
                case ApprovalKindEnum.CliPermission:
                    keys.Add(new KeyValuePair<string, string>("a", "Allow once"));
                    keys.Add(new KeyValuePair<string, string>("A", "Allow and remember"));
                    keys.Add(new KeyValuePair<string, string>("d", "Deny"));
                    break;
            }

            return keys;
        }

        /// <summary>
        /// Decision keys of an item (key, English label): the kind's keys, except that a CLI permission request the
        /// user cannot decide has none and one the user cannot remember has no <c>A</c>.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <returns>Keys.</returns>
        public static List<KeyValuePair<string, string>> KeysFor(ApprovalItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            List<KeyValuePair<string, string>> keys = KeysFor(item.Kind);
            if (item.Kind != ApprovalKindEnum.CliPermission) return keys;
            if (item.CliPermission == null || !item.CliPermission.CanDecide) return new List<KeyValuePair<string, string>>();
            if (!item.CliPermission.CanRemember) keys.RemoveAll(k => k.Key == "A");
            return keys;
        }

        /// <summary>
        /// The item under the cursor, or null.
        /// </summary>
        /// <returns>Item or null.</returns>
        public ApprovalItem? Current()
        {
            IReadOnlyList<ApprovalItem> items = Context.Approvals.Items;
            SyncCursor(items);
            return Cursor >= 0 && Cursor < items.Count ? items[Cursor] : null;
        }

        /// <summary>
        /// Run a decision key on the current item.
        /// </summary>
        /// <param name="key">Key character.</param>
        /// <returns>True when a decision started.</returns>
        public bool Decide(char key)
        {
            ApprovalItem? item = Current();
            if (item == null) return false;
            switch (item.Kind)
            {
                case ApprovalKindEnum.AskProposal:
                    if (key == 'a' || key == 'r') return Actions.DecideProposal(item, key == 'a');
                    if (key == 'x') return Actions.ShowArguments(item) != null;
                    return false;
                case ApprovalKindEnum.MissionReview:
                    if (key == 'a') return Actions.ResolveReview(item, ReviewVerdictEnum.Approve) != null;
                    if (key == 'c') return Actions.ResolveReview(item, ReviewVerdictEnum.Conditional) != null;
                    if (key == 'm') return Actions.ResolveReview(item, ReviewVerdictEnum.MoreWork) != null;
                    if (key == 'd') return Actions.ResolveReview(item, ReviewVerdictEnum.Deny) != null;
                    return false;
                case ApprovalKindEnum.DeploymentApproval:
                    if (key == 'a' || key == 'd') return Actions.DecideDeployment(item, key == 'a') != null;
                    return false;
                case ApprovalKindEnum.FailedLanding:
                    if (key == 'l') return Actions.RetryLanding(item);
                    return false;
                case ApprovalKindEnum.StalledCaptain:
                    if (key == 's') return Actions.CaptainAction(item, "stop") != null;
                    if (key == 'R') return Actions.CaptainAction(item, "recall") != null;
                    if (key == 't') return Actions.CaptainAction(item, "restart") != null;
                    return false;
                case ApprovalKindEnum.CliPermission:
                    if (key == 'a') return Actions.AllowCliPermissionOnce(item);
                    if (key == 'A') return Actions.RememberCliPermission(item) != null;
                    if (key == 'd') return Actions.DenyCliPermission(item) != null;
                    return false;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override int DefaultRefreshSeconds()
        {
            return 0;
        }

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return () => _ = Task.Run(() => Context.Status.PollAllAsync());
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            List<ArmadaCommand> list = new List<ArmadaCommand>();
            list.Add(Cmd("approvals.open", "Open the selected item", () => OpenCurrent()));
            list.Add(Cmd("approvals.approve", "Approve the selected item", () => Decide('a')));
            list.Add(Cmd("approvals.reject", "Reject the selected Ask proposal", () => Decide('r')));
            list.Add(Cmd("approvals.deny", "Deny the selected review or deployment", () => Decide('d')));
            list.Add(Cmd("approvals.retry-landing", "Retry the selected landing", () => Decide('l')));
            return list;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            IReadOnlyList<ApprovalItem> items = Context.Approvals.Items;
            SyncCursor(items);
            bool plain = (key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == 0;
            switch (key.Code)
            {
                case KeyCode.Up:
                    if (!plain) return false;
                    Move(items, Cursor - 1);
                    return true;
                case KeyCode.Down:
                    if (!plain) return false;
                    Move(items, Cursor + 1);
                    return true;
                case KeyCode.Home:
                    Move(items, 0);
                    return true;
                case KeyCode.End:
                    Move(items, items.Count - 1);
                    return true;
                case KeyCode.Enter:
                    if (!plain) return false;
                    return OpenCurrent();
                case KeyCode.Character:
                    if (!plain) return false;
                    return Decide((char)key.Rune);
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind != MouseEventKind.Press || mouse.Y < 2) return mouse.Kind == MouseEventKind.Press;
            IReadOnlyList<ApprovalItem> items = Context.Approvals.Items;
            Move(items, _Top + (mouse.Y - 2) / 2);
            if (mouse.ClickCount >= 2) OpenCurrent();
            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            IReadOnlyList<ApprovalItem> items = Context.Approvals.Items;
            SyncCursor(items);
            SurfaceText.FillRow(surface, 0, 0, width, Theme.Header);
            string head = T("Approvals") + "  (" + Context.Loc.T("{{count}} waiting", LocalizationArgs.Of("count", items.Count)) + ")";
            SurfaceText.Draw(surface, 1, 0, head, Theme.HeaderAccent, width - 2);
            string right = "F5 " + T("Refresh") + "  Enter " + T("Open");
            SurfaceText.Draw(surface, Math.Max(TextCells.Width(head) + 3, width - TextCells.Width(right) - 1), 0, right, Theme.Header.WithForeground(Theme.Muted.Foreground), width);
            if (items.Count == 0)
            {
                SurfaceText.Draw(surface, 2, 2, T("Nothing needs your decision right now."), Theme.Muted, width - 4);
                SurfaceText.Draw(surface, 2, 3, T("Ask proposals, mission reviews, deployments pending approval, failed landings, and stalled captains appear here."), Theme.Muted, width - 4);
                return;
            }

            int detailRows = Math.Min(10, Math.Max(4, height / 3));
            int listRows = Math.Max(1, (height - 2 - detailRows) / 2);
            if (Cursor < _Top) _Top = Cursor;
            if (Cursor >= _Top + listRows) _Top = Cursor - listRows + 1;
            int y = 2;
            for (int i = _Top; i < items.Count && i < _Top + listRows; i++)
            {
                RenderItem(surface, items[i], i == Cursor, y, width);
                y += 2;
            }

            ApprovalItem? current = Cursor >= 0 && Cursor < items.Count ? items[Cursor] : null;
            if (current != null) RenderDetail(surface, current, height - detailRows, detailRows, width);
        }

        #endregion

        #region Private-Methods

        private ArmadaCommand Cmd(string id, string title, Action handler)
        {
            ArmadaCommand c = new ArmadaCommand(id, title, CommandMenuEnum.Actions, handler);
            c.Group = "Approvals";
            return c;
        }

        private void SyncCursor(IReadOnlyList<ApprovalItem> items)
        {
            if (_CursorKey != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Key == _CursorKey)
                    {
                        Cursor = i;
                        return;
                    }
                }
            }

            Cursor = Math.Clamp(Cursor, 0, Math.Max(0, items.Count - 1));
            _CursorKey = Cursor < items.Count ? items[Cursor].Key : null;
        }

        private void Move(IReadOnlyList<ApprovalItem> items, int index)
        {
            Cursor = Math.Clamp(index, 0, Math.Max(0, items.Count - 1));
            _CursorKey = Cursor < items.Count ? items[Cursor].Key : null;
        }

        private bool OpenCurrent()
        {
            ApprovalItem? item = Current();
            if (item?.Route == null) return false;
            Context.Navigate(item.Route);
            return true;
        }

        private void RenderItem(ISurface surface, ApprovalItem item, bool cursor, int y, int width)
        {
            CellStyle row = cursor ? (IsFocused ? Theme.Selection : Theme.SelectionInactive) : Theme.Text;
            SurfaceText.FillRow(surface, 0, y, width, row);
            if (cursor) SurfaceText.FillRow(surface, 0, y + 1, width, row);
            string urgency = item.Urgency >= 3 ? "!!" : item.Urgency >= 2 ? "! " : "  ";
            string kind = TextCells.PadRight(T(KindLabel(item.Kind)), 20);
            int x = SurfaceText.Draw(surface, 1, y, urgency + " ", row.WithForeground(Theme.Warning.Foreground), 3) + 1;
            x += SurfaceText.Draw(surface, x, y, kind + " ", row.WithForeground(Theme.Accent.Foreground), width - x);
            string age = item.Kind == ApprovalKindEnum.CliPermission && item.ExpiresUtc != null
                ? CliPermissionText.ExpiresIn(Context.Loc, item.ExpiresUtc.Value, Context.Clock.UtcNow)
                : Context.Loc.FormatRelative(item.CreatedUtc, Context.Clock.UtcNow);
            int aw = TextCells.Width(age);
            SurfaceText.Draw(surface, x, y, item.Title, row.WithAttribute(CellAttributes.Bold, true), Math.Max(1, width - x - aw - 2));
            SurfaceText.Draw(surface, width - aw - 1, y, age, row.WithForeground(Theme.Muted.Foreground), aw);
            List<KeyValuePair<string, string>> itemKeys = KeysFor(item);
            string keys = itemKeys.Count > 0 ? "[" + String.Join("  ", itemKeys.Select(k => k.Key + " " + T(k.Value))) + "]" : "";
            string detail = String.IsNullOrEmpty(item.Detail) ? "" : item.Detail + "   ";
            SurfaceText.Draw(surface, 4, y + 1, detail + keys, row.WithForeground(Theme.Muted.Foreground), width - 5);
        }

        private void RenderDetail(ISurface surface, ApprovalItem item, int top, int rows, int width)
        {
            SurfaceText.Draw(surface, 0, top, new string('-', width), Theme.Border, width);
            int y = top + 1;
            SurfaceText.Draw(surface, 1, y++, T(KindLabel(item.Kind)) + ": " + (item.EntityName ?? item.Title) + "   (" + item.EntityId + ")", Theme.Accent, width - 2);
            if (!String.IsNullOrEmpty(item.Source)) SurfaceText.Draw(surface, 1, y++, T("Source") + ": " + T(item.Source) + (item.Route != null ? "   " + T("Opens") + ": " + item.Route : ""), Theme.Muted, width - 2);
            if (item.ExpiresUtc != null)
            {
                string expires = T("Expires") + ": " + Context.Loc.FormatDateTime(item.ExpiresUtc.Value);
                if (item.Kind == ApprovalKindEnum.CliPermission) expires += "  (" + CliPermissionText.ExpiresIn(Context.Loc, item.ExpiresUtc.Value, Context.Clock.UtcNow) + ")";
                SurfaceText.Draw(surface, 1, y++, expires, Theme.Muted, width - 2);
            }

            if (item.Kind == ApprovalKindEnum.CliPermission && item.CliPermission != null)
            {
                RenderCliPermissionDetail(surface, item.CliPermission, y, top + rows, width);
            }
            else if (item.Kind == ApprovalKindEnum.AskProposal && !String.IsNullOrEmpty(item.Arguments))
            {
                SurfaceText.Draw(surface, 1, y++, T("Exact arguments") + " (x):", Theme.Muted, width - 2);
                foreach (string line in ApprovalActions.Pretty(item.Arguments).Split('\n'))
                {
                    if (y >= top + rows) break;
                    SurfaceText.Draw(surface, 3, y++, line, Theme.Code, width - 4);
                }
            }
            else if (!String.IsNullOrEmpty(item.Detail))
            {
                foreach (string line in TextCells.Wrap(item.Detail, width - 2))
                {
                    if (y >= top + rows) break;
                    SurfaceText.Draw(surface, 1, y++, line, Theme.Text, width - 2);
                }
            }
        }

        private void RenderCliPermissionDetail(ISurface surface, Armada.Core.Models.CliPermissionRequest request, int y, int bottom, int width)
        {
            string where = CliPermissionText.Where(Context.Loc, request);
            if (where.Length > 0 && y < bottom) SurfaceText.Draw(surface, 1, y++, where, Theme.Text, width - 2);
            if (!request.CanDecide && y < bottom) SurfaceText.Draw(surface, 1, y++, T("An admin must decide this request."), Theme.Warning, width - 2);
            else if (!String.IsNullOrEmpty(request.SuggestedRule) && y < bottom) SurfaceText.Draw(surface, 1, y++, T("Suggested rule") + ": " + request.SuggestedRule, Theme.Muted, width - 2);
            if (y < bottom) SurfaceText.Draw(surface, 1, y++, T("Input") + ":", Theme.Muted, width - 2);
            foreach (string line in ApprovalActions.Pretty(request.InputText).Split('\n'))
            {
                if (y >= bottom) break;
                SurfaceText.Draw(surface, 3, y++, line, Theme.Code, width - 4);
            }
        }

        #endregion
    }
}
