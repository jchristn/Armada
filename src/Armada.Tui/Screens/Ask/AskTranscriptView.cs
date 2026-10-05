namespace Armada.Tui.Screens.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Ask;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The Ask transcript (W2.3, W2.4, W2.5): a scrolling view over the blocks laid out by
    /// <see cref="AskTranscriptBuilder"/>, with a focused block (and focused work card row), smart scroll lock, keeping
    /// the reader's place when older pages load, and search. Follow rules: the view follows new content whenever the
    /// viewport is at the bottom, whether or not a block is selected; selecting a block that is on screen at the tail
    /// does not detach, only scrolling the viewport up does (PgUp, Home, the wheel, search, or a selection that moves
    /// the viewport); while detached it shows "N new below"; End, a decision on a card, and a sent message return to
    /// the tail (keeping the selection only while it is still on screen). Confirm cards carry clickable [Approve],
    /// [Reject], and [Arguments] buttons and CLI permission cards [Allow once], [Allow and remember], and [Deny], which
    /// work whatever has keyboard focus; a selected pending card also spells out its keys. Keys: <c>Up</c>/<c>Down</c>
    /// focus messages and card rows (every pending card is its own stop), <c>PgUp</c>/<c>PgDn</c> scroll,
    /// <c>Home</c> top (loads earlier messages), <c>End</c> live tail, <c>Enter</c> acts on the focused item,
    /// <c>a</c>/<c>r</c> approve or reject the pending decision the highlighted block belongs to (its confirm card, the
    /// captain reply of the turn that proposed it, a CLI permission card, or a work card row of a mission waiting on a
    /// permission or a review; a chooser opens when there are several), <c>A</c> allow and remember and <c>d</c> deny a
    /// CLI permission request, and on anything else a hint says nothing waits there, <c>x</c> arguments, <c>t</c> thinking,
    /// <c>y</c> copy (arguments on a card, otherwise the message as Markdown), <c>Y</c> copy the conversation,
    /// <c>o</c> open the row's pull request, <c>l</c> mission log, <c>d</c> mission diff, <c>Ctrl+F</c> or <c>/</c> search
    /// with <c>n</c>/<c>N</c>. Not thread-safe.
    /// </summary>
    public class AskTranscriptView : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Display choices (expanded sections, highlight).
        /// </summary>
        public AskViewState ViewState { get; } = new AskViewState();

        /// <summary>
        /// Blocks of the last layout. Never null.
        /// </summary>
        public IReadOnlyList<AskBlock> Blocks
        {
            get { return _Blocks; }
        }

        /// <summary>
        /// The block builder (cache statistics for diagnostics and tests).
        /// </summary>
        public AskTranscriptBuilder Builder
        {
            get { return _Builder; }
        }

        /// <summary>
        /// Key of the focused block, or null while following the live tail.
        /// </summary>
        public string? SelectedKey { get; private set; } = null;

        /// <summary>
        /// Focused work card row within the focused block, or -1.
        /// </summary>
        public int SelectedRow { get; private set; } = -1;

        /// <summary>
        /// True while the view follows the live tail.
        /// </summary>
        public bool Following { get; private set; } = true;

        /// <summary>
        /// First visible line.
        /// </summary>
        public int ScrollOffset
        {
            get { return _Scroll; }
        }

        /// <summary>
        /// Lines added below the viewport since the reader scrolled away from the tail.
        /// </summary>
        public int NewBelow
        {
            get { return Following ? 0 : Math.Max(0, _Flat.Count - _DetachedTotal); }
        }

        /// <summary>
        /// Active search text, or empty.
        /// </summary>
        public string SearchText
        {
            get { return _Search; }
        }

        /// <summary>
        /// The search prompt is open.
        /// </summary>
        public bool Searching
        {
            get { return _Searching; }
        }

        #endregion

        #region Private-Members

        private readonly TuiContext _Context;
        private readonly AskController _Ask;
        private readonly AskTranscriptBuilder _Builder = new AskTranscriptBuilder();
        private List<AskBlock> _Blocks = new List<AskBlock>();
        private List<StyledText> _Flat = new List<StyledText>();
        private List<int> _FlatBlock = new List<int>();
        private string _LayoutKey = "";
        private int _Scroll = 0;
        private int _Height = 10;
        private int _DetachedTotal = 0;
        private string _Search = "";
        private bool _Searching = false;
        private int _Match = -1;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Services.</param>
        /// <param name="ask">Ask session.</param>
        public AskTranscriptView(TuiContext context, AskController ask)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Ask = ask ?? throw new ArgumentNullException(nameof(ask));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The focused block, or null.
        /// </summary>
        /// <returns>Block or null.</returns>
        public AskBlock? Selected()
        {
            return SelectedKey == null ? null : _Blocks.FirstOrDefault(b => b.Key == SelectedKey);
        }

        /// <summary>
        /// Lay out at a width now (tests and key handling between frames).
        /// </summary>
        /// <param name="width">Total width including the gutter.</param>
        public void Layout(int width)
        {
            // A selected pending card in the focused transcript spells out its keys.
            ViewState.Focus(IsFocused ? SelectedKey : null);
            int contentWidth = Math.Max(20, width - 2);
            DateTime now = _Context.Clock.UtcNow;
            string key = _Ask.Conversation.Version + "|" + contentWidth + "|" + ViewState.Version + "|" + _Ask.BusyProposalsVersion + "|" + _Ask.LoadingOlder
                + "|" + (now.Ticks / TimeSpan.TicksPerSecond) + "|" + Theme.Name + "|" + _Ask.Stopping + "|" + _Ask.QuickActions.Count + "|" + _Ask.Captains.Count
                + "|" + _Ask.ActiveCaptainId + "|" + _Context.Loc.Locale + "|" + (_Ask.Conversation.Thread?.Id ?? "");
            if (key == _LayoutKey) return;
            _LayoutKey = key;
            string? anchorKey = null;
            int anchorOffset = 0;
            if (!Following && _FlatBlock.Count > 0)
            {
                int idx = Math.Clamp(_Scroll, 0, _FlatBlock.Count - 1);
                AskBlock anchor = _Blocks[_FlatBlock[idx]];
                anchorKey = anchor.Key;
                anchorOffset = _Scroll - anchor.Top;
            }

            _Blocks = _Builder.Build(_Ask, ViewState, Theme, _Context.Loc, now, contentWidth);
            _Flat = new List<StyledText>();
            _FlatBlock = new List<int>();
            for (int i = 0; i < _Blocks.Count; i++)
            {
                _Blocks[i].Top = _Flat.Count;
                foreach (StyledText line in _Blocks[i].Lines)
                {
                    _Flat.Add(line);
                    _FlatBlock.Add(i);
                }
            }

            if (SelectedKey != null && !_Blocks.Any(b => b.Key == SelectedKey))
            {
                SelectedKey = null;
                SelectedRow = -1;
            }

            if (anchorKey != null)
            {
                AskBlock? again = _Blocks.FirstOrDefault(b => b.Key == anchorKey);
                if (again != null)
                {
                    int moved = again.Top + anchorOffset - _Scroll;
                    _Scroll = again.Top + anchorOffset;
                    _DetachedTotal += moved;
                }
            }
        }

        /// <summary>
        /// Return to the live tail.
        /// </summary>
        public void FollowTail()
        {
            Following = true;
            SelectedKey = null;
            SelectedRow = -1;
            _Scroll = Math.Max(0, _Flat.Count - _Height);
        }

        /// <summary>
        /// Return to the live tail and follow it (after a decision or a send, and on <c>End</c>), keeping the selected
        /// block only while it is still on screen at the tail.
        /// </summary>
        public void ReturnToTail()
        {
            Following = true;
            _Scroll = MaxScroll();
            AskBlock? block = Selected();
            if (block != null && !VisibleAt(block, _Scroll))
            {
                SelectedKey = null;
                SelectedRow = -1;
            }
        }

        /// <summary>
        /// Approve or reject a pending proposal (the <c>a</c> and <c>r</c> keys and the card buttons), then return to
        /// the live tail so the outcome and anything that follows stay in view.
        /// </summary>
        /// <param name="proposal">Proposal.</param>
        /// <param name="approve">Approve (true) or reject (false).</param>
        /// <returns>True when the decision was sent.</returns>
        public bool Decide(AskActionProposal proposal, bool approve)
        {
            if (proposal == null || proposal.Status != AskProposalStatusEnum.Pending || _Ask.Conversation.ThreadId == null) return false;
            _Ask.Decide(_Ask.Conversation.ThreadId, proposal.Id, approve);
            ReturnToTail();
            return true;
        }

        /// <summary>
        /// Focus the oldest pending confirm card (if any).
        /// </summary>
        /// <returns>True when one was focused.</returns>
        public bool SelectOldestPending()
        {
            Layout(LastWidth());
            AskBlock? block = _Blocks.FirstOrDefault(b => IsPendingCard(b) && b.Buttons.Count > 0);
            if (block == null) block = _Blocks.FirstOrDefault(b => IsPendingCard(b));
            if (block == null) return false;
            Select(block.Key, -1);
            return true;
        }

        /// <summary>
        /// Focus the newest pending confirm card (if any).
        /// </summary>
        /// <returns>True when one was focused.</returns>
        public bool SelectNewestPending()
        {
            Layout(LastWidth());
            AskBlock? block = _Blocks.LastOrDefault(b => IsPendingCard(b));
            if (block == null) return false;
            Select(block.Key, -1);
            return true;
        }

        /// <summary>
        /// Whether a block is a card waiting on the user: a pending confirm card, or a pending CLI permission card the
        /// user may decide.
        /// </summary>
        /// <param name="block">Block, or null.</param>
        /// <returns>True for a pending card.</returns>
        public static bool IsPendingCard(AskBlock? block)
        {
            if (block == null) return false;
            if (block.Proposal != null && block.Proposal.Status == AskProposalStatusEnum.Pending) return true;
            return block.CliRequest != null && block.CliRequest.Status == CliPermissionRequestStatusEnum.Pending && block.CliRequest.CanDecide;
        }

        /// <summary>
        /// The pending decisions the selected part of the transcript belongs to (see <see cref="DecisionsFor"/>).
        /// </summary>
        /// <returns>Decisions, oldest first; empty when nothing waits there.</returns>
        public List<AskPendingDecision> SelectedDecisions()
        {
            return DecisionsFor(Selected(), SelectedRow);
        }

        /// <summary>
        /// The pending decisions a block (and work card row) belongs to: the proposal of a confirm card; the request of a
        /// CLI permission card; for a captain reply (or the streaming reply), the proposals and requests of the same turn
        /// (the messages between the user message before it and the next one, linked by the conversation's typed message
        /// order, never by text); for a work card row, the CLI permission requests and the review of its mission.
        /// Decisions with a call in flight, and requests the user may not decide, are left out.
        /// </summary>
        /// <param name="block">Block, or null.</param>
        /// <param name="row">Selected work card row, or -1.</param>
        /// <returns>Decisions, oldest first.</returns>
        public List<AskPendingDecision> DecisionsFor(AskBlock? block, int row)
        {
            List<AskPendingDecision> decisions = new List<AskPendingDecision>();
            if (block == null) return decisions;
            AskConversation conv = _Ask.Conversation;
            if (block.Proposal != null)
            {
                if (block.Proposal.Status == AskProposalStatusEnum.Pending && !_Ask.IsProposalBusy(block.Proposal.Id)) decisions.Add(new AskPendingDecision(block.Proposal));
                return decisions;
            }

            if (block.CliRequest != null)
            {
                if (Decidable(block.CliRequest)) decisions.Add(new AskPendingDecision(block.CliRequest));
                return decisions;
            }

            if (block.WorkId != null)
            {
                string? missionId = row >= 0 && row < block.RowMissionIds.Count ? block.RowMissionIds[row] : (block.RowMissionIds.Count == 1 ? block.RowMissionIds[0] : null);
                if (missionId == null) return decisions;
                foreach (CliPermissionRequest request in conv.PendingCliPermissions().Where(r => r.MissionId == missionId && Decidable(r))) decisions.Add(new AskPendingDecision(request));
                foreach (ApprovalItem item in _Context.Approvals.Items.Where(i => i.Kind == ApprovalKindEnum.CliPermission && i.CliPermission != null && i.CliPermission.MissionId == missionId))
                {
                    if (!decisions.Any(d => d.Id == item.EntityId) && Decidable(item.CliPermission!)) decisions.Add(new AskPendingDecision(item.CliPermission!));
                }

                ApprovalItem? review = _Context.Approvals.Find(ApprovalKindEnum.MissionReview, missionId);
                if (review != null) decisions.Add(new AskPendingDecision(review));
                return decisions;
            }

            bool reply = block.Kind == AskBlockKindEnum.Stream
                || (block.Message != null && block.Message.Role == AskMessageRoleEnum.Assistant && block.Message.Kind == AskMessageKindEnum.Text);
            if (!reply) return decisions;
            List<AskMessage> messages = conv.Messages;
            int index = block.Message != null ? messages.FindIndex(m => m.Id == block.Message.Id) : messages.Count;
            int start = index;
            while (start > 0 && messages[start - 1].Role != AskMessageRoleEnum.User) start--;
            int end = index;
            while (end + 1 < messages.Count && messages[end + 1].Role != AskMessageRoleEnum.User) end++;
            for (int i = start; i <= end && i < messages.Count; i++)
            {
                if (i == index) continue;
                AskMessage m = messages[i];
                if (m.Kind == AskMessageKindEnum.ActionProposal)
                {
                    AskActionProposal? proposal = conv.ProposalFor(m);
                    if (proposal != null && proposal.Status == AskProposalStatusEnum.Pending && !_Ask.IsProposalBusy(proposal.Id)) decisions.Add(new AskPendingDecision(proposal));
                }
                else if (m.Kind == AskMessageKindEnum.CliPermission)
                {
                    CliPermissionRequest? request = conv.CliPermissionFor(m);
                    if (request != null && Decidable(request)) decisions.Add(new AskPendingDecision(request));
                }
            }

            return decisions;
        }

        /// <summary>
        /// Act on the pending decision the selection belongs to: approve (<c>a</c>; allow once for a CLI permission
        /// request), reject (<c>r</c>; deny), remember (<c>A</c>, CLI permission requests only), or deny (<c>d</c>, CLI
        /// permission requests only). Several decisions open a chooser; none shows a hint and leaves the selection as is.
        /// </summary>
        /// <param name="key">a, r, A, or d.</param>
        /// <returns>True when the key was handled (acted, chose, or hinted).</returns>
        public bool DecideSelected(char key)
        {
            List<AskPendingDecision> all = SelectedDecisions();
            List<AskPendingDecision> candidates = key == 'A' || key == 'd'
                ? all.Where(d => d.Kind == AskPendingDecisionKindEnum.CliPermission).ToList()
                : all;
            if (candidates.Count == 0)
            {
                ShowNothingHint(key);
                return true;
            }

            if (candidates.Count == 1) return Act(candidates[0], key);
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            foreach (AskPendingDecision decision in candidates)
            {
                AskPendingDecision d = decision;
                items.Add(new ActionMenuItem(VerbLabel(d, key) + ": " + DecisionLabel(d), () => Act(d, key)));
            }

            ActionMenu.Show(_Context.Modals, "Choose what to decide", items, _Context.Loc, _Context.Theme.Current);
            return true;
        }

        /// <summary>
        /// Focus the card hosting a tracked item and highlight it; false when its card is not loaded.
        /// </summary>
        /// <param name="workId">Work id.</param>
        /// <returns>True when found.</returns>
        public bool ScrollToWork(string workId)
        {
            Layout(LastWidth());
            AskBlock? block = _Blocks.FirstOrDefault(b => b.WorkId == workId);
            if (block == null) return false;
            ViewState.Highlight(workId, _Context.Clock.UtcNow);
            Select(block.Key, -1);
            int cardTop = block.Top + (block.RowLines.Count > 0 ? Math.Max(0, block.RowLines[0] - 3) : 0);
            _Scroll = Math.Max(0, Math.Min(cardTop, Math.Max(0, _Flat.Count - _Height)));
            SyncFollow();
            return true;
        }

        /// <summary>
        /// Plain text of the visible frame region (diagnostics).
        /// </summary>
        /// <returns>Lines.</returns>
        public IReadOnlyList<string> PlainLines()
        {
            return _Flat.Select(l => l.ToPlainString()).ToList();
        }

        /// <summary>
        /// The whole conversation as Markdown (copied by <c>Y</c>).
        /// </summary>
        /// <returns>Markdown.</returns>
        public string ConversationMarkdown()
        {
            StringBuilder sb = new StringBuilder();
            string title = _Ask.Conversation.Thread?.Title ?? T("New conversation");
            sb.Append("# ").Append(title).Append("\n\n");
            foreach (AskMessage m in _Ask.Conversation.Messages)
            {
                string who = m.Role == AskMessageRoleEnum.User ? T("You") : m.Role == AskMessageRoleEnum.System ? T("System") : (_Ask.CaptainName(m.CaptainId) ?? T("Captain"));
                string kind = m.Kind == AskMessageKindEnum.Text ? "" : " (" + m.Kind + ")";
                sb.Append("**").Append(who).Append("**").Append(kind).Append(": ").Append(m.ContentText).Append("\n\n");
                AskActionProposal? p = _Ask.Conversation.ProposalFor(m);
                if (p != null) sb.Append("```json\n").Append(Approvals.ApprovalActions.Pretty(p.ArgumentsText)).Append("\n```\n").Append(p.ToolName).Append(": ").Append(p.Status).Append("\n\n");
            }

            return sb.ToString();
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (_Searching) return HandleSearchKey(key);
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            bool alt = (key.Modifiers & KeyModifiers.Alt) != 0;
            switch (key.Code)
            {
                case KeyCode.Up:
                    if (alt || ctrl) return false;
                    MoveSelection(-1);
                    return true;
                case KeyCode.Down:
                    if (alt || ctrl) return false;
                    MoveSelection(1);
                    return true;
                case KeyCode.PageUp:
                    ScrollBy(-Math.Max(1, _Height - 1));
                    return true;
                case KeyCode.PageDown:
                    ScrollBy(Math.Max(1, _Height - 1));
                    return true;
                case KeyCode.Home:
                    Detach();
                    _Scroll = 0;
                    if (_Ask.Conversation.HasMore) _Ask.LoadOlder();
                    return true;
                case KeyCode.End:
                    ReturnToTail();
                    return true;
                case KeyCode.Enter:
                    if (alt || ctrl) return false;
                    return Activate();
                case KeyCode.Character:
                    if (ctrl && Char.ToLowerInvariant((char)key.Rune) == 'f')
                    {
                        BeginSearch();
                        return true;
                    }

                    if (ctrl || alt) return false;
                    return HandleChar((char)key.Rune);
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind == MouseEventKind.Wheel)
            {
                if (mouse.Button == MouseButton.WheelUp) ScrollBy(-3);
                else if (mouse.Button == MouseButton.WheelDown) ScrollBy(3);
                else return false;
                return true;
            }

            if (mouse.Kind == MouseEventKind.Press)
            {
                int line = _Scroll + mouse.Y;
                if (line >= 0 && line < _FlatBlock.Count)
                {
                    AskBlock block = _Blocks[_FlatBlock[line]];
                    AskCardButton? button = mouse.Button == MouseButton.Left ? ButtonAt(block, line - block.Top, mouse.X - 2) : null;
                    if (button != null)
                    {
                        // A click on a card button selects the card and acts at once, whatever had keyboard focus.
                        SelectedKey = block.Key;
                        SelectedRow = -1;
                        Press(block, button);
                        return true;
                    }

                    if (block.Focusable)
                    {
                        // Selecting with the mouse does not move the viewport, so it does not stop following the tail.
                        SelectedKey = block.Key;
                        SelectedRow = RowAt(block, line - block.Top);
                        if (mouse.ClickCount >= 2) Activate();
                    }
                }

                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 4 || height < 1) return;
            _LastWidth = width;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            Layout(width);
            bool prompt = _Searching || _Search.Length > 0;
            int indicator = NewBelow > 0 ? 1 : 0;
            _Height = Math.Max(1, height - (prompt ? 1 : 0) - indicator);
            int max = Math.Max(0, _Flat.Count - _Height);
            if (Following) _Scroll = max;
            _Scroll = Math.Clamp(_Scroll, 0, max);
            if (!Following && _Scroll >= max) Following = true;

            AskBlock? selected = Selected();
            for (int row = 0; row < _Height; row++)
            {
                int idx = _Scroll + row;
                if (idx >= _Flat.Count) break;
                AskBlock block = _Blocks[_FlatBlock[idx]];
                bool inBlock = selected != null && ReferenceEquals(block, selected);
                int within = idx - block.Top;
                bool inRow = inBlock && SelectedRow >= 0 && SelectedRow < block.RowLines.Count
                    && within >= block.RowLines[SelectedRow] && within < block.RowLines[SelectedRow] + block.RowHeights[SelectedRow];
                CellStyle baseStyle = idx == _Match ? Theme.Selection : inRow ? Theme.SelectionInactive : Theme.Text;
                if (idx == _Match || inRow) SurfaceText.FillRow(surface, 0, row, width, baseStyle);
                if (inBlock)
                {
                    string gutter = within == 0 || (inRow && within == block.RowLines[SelectedRow]) ? "> " : "| ";
                    surface.DrawText(0, row, gutter, IsFocused ? Theme.Accent.WithAttribute(CellAttributes.Bold, true) : Theme.Muted);
                }

                surface.DrawStyledText(2, row, _Flat[idx], baseStyle);
            }

            int y = _Height;
            if (indicator > 0 && y < height)
            {
                string text = _Context.Loc.T("{{count}} new below", Services.LocalizationArgs.Of("count", NewBelow)) + "  (End)";
                SurfaceText.FillRow(surface, 0, y, width, Theme.StatusBar);
                SurfaceText.Draw(surface, Math.Max(0, (width - TextCells.Width(text)) / 2), y, text, Theme.StatusBar.WithForeground(Theme.Accent.Foreground), width);
                y++;
            }

            if (prompt && y < height)
            {
                string p = (_Searching ? "/" : T("Search") + ": ") + _Search + (_Searching ? "_" : "  (n/N, Esc)");
                SurfaceText.FillRow(surface, 0, y, width, Theme.StatusBar);
                SurfaceText.Draw(surface, 0, y, p, Theme.StatusBar, width);
            }
        }

        #endregion

        #region Private-Methods

        private int _LastWidth = 80;

        private int LastWidth()
        {
            return _LastWidth;
        }

        private void Select(string key, int row)
        {
            SelectedKey = key;
            SelectedRow = row;
            if (Following) _Scroll = MaxScroll();
            EnsureVisible();
            SyncFollow();
        }

        /// <summary>
        /// Follow the tail exactly when the viewport is at the bottom: a selection on screen in the tail keeps
        /// following; one that moved the viewport up detaches (and starts counting new lines below).
        /// </summary>
        private void SyncFollow()
        {
            if (_Scroll >= MaxScroll()) Following = true;
            else Detach();
        }

        private void Detach()
        {
            if (!Following) return;
            Following = false;
            _DetachedTotal = _Flat.Count;
        }

        private int MaxScroll()
        {
            return Math.Max(0, _Flat.Count - _Height);
        }

        private bool VisibleAt(AskBlock block, int scroll)
        {
            int top = block.Top;
            int bottom = block.Top + block.Lines.Count;
            return bottom > scroll && top < scroll + _Height;
        }

        private static AskCardButton? ButtonAt(AskBlock block, int within, int x)
        {
            foreach (AskCardButton b in block.Buttons)
            {
                if (b.Line == within && x >= b.X && x < b.X + b.Width) return b;
            }

            return null;
        }

        private bool Press(AskBlock block, AskCardButton button)
        {
            if (!String.IsNullOrEmpty(button.RequestId))
            {
                CliPermissionRequest? request = block.CliRequest;
                if (request == null || request.Id != button.RequestId || !Decidable(request)) return false;
                if (button.Action == AskCardActionEnum.AllowOnce) return Act(new AskPendingDecision(request), 'a');
                if (button.Action == AskCardActionEnum.AllowAndRemember) return Act(new AskPendingDecision(request), 'A');
                return Act(new AskPendingDecision(request), 'd');
            }

            AskActionProposal? proposal = block.Proposal;
            if (proposal == null || proposal.Id != button.ProposalId) return false;
            if (button.Action == AskCardActionEnum.Arguments)
            {
                ViewState.Toggle(ViewState.ExpandedArguments, proposal.Id);
                return true;
            }

            return Decide(proposal, button.Action == AskCardActionEnum.Approve);
        }

        private void MoveSelection(int delta)
        {
            Layout(LastWidth());
            List<AskBlock> focusable = _Blocks.Where(b => b.Focusable).ToList();
            if (focusable.Count == 0)
            {
                ScrollBy(delta);
                return;
            }

            AskBlock? current = Selected();
            if (current == null)
            {
                if (delta > 0) return;
                AskBlock last = focusable[focusable.Count - 1];
                Select(last.Key, last.RowLines.Count > 0 ? last.RowLines.Count - 1 : -1);
                return;
            }

            if (current.RowLines.Count > 0)
            {
                int next = SelectedRow + delta;
                if (next >= -1 && next < current.RowLines.Count)
                {
                    SelectedRow = next;
                    if (Following) _Scroll = MaxScroll();
                    EnsureVisible();
                    SyncFollow();
                    return;
                }
            }

            int i = focusable.IndexOf(current) + delta;
            if (i < 0)
            {
                if (_Ask.Conversation.HasMore) _Ask.LoadOlder();
                _Scroll = 0;
                return;
            }

            if (i >= focusable.Count)
            {
                FollowTail();
                return;
            }

            AskBlock target = focusable[i];
            Select(target.Key, delta < 0 && target.RowLines.Count > 0 ? target.RowLines.Count - 1 : -1);
        }

        private void EnsureVisible()
        {
            AskBlock? block = Selected();
            if (block == null) return;
            int top = block.Top;
            int bottom = block.Top + block.Lines.Count - 1;
            if (SelectedRow >= 0 && SelectedRow < block.RowLines.Count)
            {
                top = block.Top + block.RowLines[SelectedRow];
                bottom = top + block.RowHeights[SelectedRow] - 1;
            }

            if (top < _Scroll) _Scroll = top;
            else if (bottom >= _Scroll + _Height) _Scroll = Math.Max(top - Math.Max(0, _Height - (bottom - top + 1)), Math.Min(top, bottom - _Height + 1));
            if (bottom - top + 1 > _Height) _Scroll = top;
        }

        private void ScrollBy(int delta)
        {
            if (Following && delta < 0) _DetachedTotal = _Flat.Count;
            _Scroll = Math.Max(0, _Scroll + delta);
            int max = Math.Max(0, _Flat.Count - _Height);
            if (delta < 0) Following = false;
            if (_Scroll >= max && delta > 0) Following = true;
            if (_Scroll == 0 && delta < 0 && _Ask.Conversation.HasMore) _Ask.LoadOlder();
        }

        private static int RowAt(AskBlock block, int within)
        {
            for (int r = 0; r < block.RowLines.Count; r++)
            {
                if (within >= block.RowLines[r] && within < block.RowLines[r] + block.RowHeights[r]) return r;
            }

            return -1;
        }

        private bool Activate()
        {
            AskBlock? block = Selected();
            if (block == null) return false;
            if (block.Kind == AskBlockKindEnum.Older)
            {
                _Ask.LoadOlder();
                return true;
            }

            if (block.WorkId != null && SelectedRow >= 0 && SelectedRow < block.RowRoutes.Count)
            {
                string? route = block.RowRoutes[SelectedRow];
                if (route != null) _Context.Navigate(route);
                return true;
            }

            if (block.WorkId != null && SelectedRow < 0 && block.Proposal == null)
            {
                OpenWork(block.WorkId);
                return true;
            }

            if (block.LinkedWorkId != null)
            {
                if (!ScrollToWork(block.LinkedWorkId)) OpenWork(block.LinkedWorkId);
                return true;
            }

            if (block.Proposal != null)
            {
                ViewState.Toggle(ViewState.ExpandedArguments, block.Proposal.Id);
                return true;
            }

            if (block.Kind == AskBlockKindEnum.Stream || (block.Message != null && block.Message.ToolCalls != null && block.Message.ToolCalls.Count > 0))
            {
                ViewState.Toggle(ViewState.ExpandedTools, block.Key);
                return true;
            }

            if (block.WorkId != null)
            {
                OpenWork(block.WorkId);
                return true;
            }

            return true;
        }

        private void OpenWork(string workId)
        {
            AskTrackedWork? work = _Ask.Conversation.Work(workId);
            if (work == null) return;
            _Context.Navigate(AskWorkLogic.Route(work.EntityType, work.EntityId));
        }

        private bool HandleChar(char c)
        {
            AskBlock? block = Selected();
            switch (c)
            {
                case '/':
                    BeginSearch();
                    return true;
                case 'n':
                case 'N':
                    if (_Search.Length == 0) return false;
                    FindNext(c == 'n');
                    return true;
                case 'a':
                case 'r':
                case 'A':
                    return DecideSelected(c);
                case 'x':
                    if (block?.Proposal == null) return false;
                    ViewState.Toggle(ViewState.ExpandedArguments, block.Proposal.Id);
                    return true;
                case 't':
                    if (block == null) return false;
                    ViewState.Toggle(ViewState.ExpandedThinking, block.Key);
                    return true;
                case 'y':
                    if (block == null) return false;
                    if (block.Proposal != null) _Context.Clipboard.Copy(block.Proposal.ArgumentsText ?? "", "Arguments");
                    else _Context.Clipboard.Copy(block.CopyText, "Message");
                    return true;
                case 'Y':
                    _Context.Clipboard.Copy(ConversationMarkdown(), "Conversation");
                    return true;
                case 'o':
                    {
                        string? url = RowValue(block, b => b.RowPrUrls);
                        if (url == null) return false;
                        _Context.External.OpenUrl(url);
                        return true;
                    }

                case 'l':
                    {
                        string? missionId = RowValue(block, b => b.RowMissionIds);
                        if (missionId == null) return false;
                        ShowLog(missionId);
                        return true;
                    }

                case 'd':
                    {
                        // A CLI permission card (or the reply of its turn) denies; a work card row opens the diff.
                        if (block != null && block.WorkId == null) return DecideSelected('d');
                        string? missionId = RowValue(block, b => b.RowMissionIds);
                        if (missionId == null) return DecideSelected('d');
                        ShowDiff(missionId);
                        return true;
                    }

                default:
                    return false;
            }
        }

        private bool Decidable(CliPermissionRequest request)
        {
            return request.Status == CliPermissionRequestStatusEnum.Pending && request.CanDecide && !_Ask.IsCliPermissionBusy(request.Id);
        }

        private bool Act(AskPendingDecision decision, char key)
        {
            bool positive = key == 'a';
            if (decision.Kind == AskPendingDecisionKindEnum.Proposal) return key == 'a' || key == 'r' ? Decide(decision.Proposal!, positive) : false;
            if (decision.Kind == AskPendingDecisionKindEnum.CliPermission)
            {
                CliPermissionDecisionEnum kind = key == 'a' ? CliPermissionDecisionEnum.AllowOnce : key == 'A' ? CliPermissionDecisionEnum.AllowAndRemember : CliPermissionDecisionEnum.Deny;
                if (kind == CliPermissionDecisionEnum.AllowAndRemember && !decision.CliRequest!.CanRemember)
                {
                    _Context.Notifications.Toast(Services.NotificationSeverityEnum.Warning, _Context.Loc.T("Only an admin can save a permission rule. Allow once or deny instead."));
                    return true;
                }

                bool started = _Ask.DecideCliPermission(decision.CliRequest!, kind);
                if (started && kind == CliPermissionDecisionEnum.AllowOnce) ReturnToTail();
                return started;
            }

            if (key != 'a' && key != 'r') return false;
            new Approvals.ApprovalActions(_Context).ResolveReview(decision.Review!, positive ? Approvals.ReviewVerdictEnum.Approve : Approvals.ReviewVerdictEnum.Deny);
            return true;
        }

        private string VerbLabel(AskPendingDecision decision, char key)
        {
            if (decision.Kind == AskPendingDecisionKindEnum.CliPermission)
                return T(key == 'a' ? "Allow once" : key == 'A' ? "Allow and remember" : "Deny");
            return T(key == 'a' ? "Approve" : decision.Kind == AskPendingDecisionKindEnum.MissionReview ? "Deny" : "Reject");
        }

        private string DecisionLabel(AskPendingDecision decision)
        {
            if (decision.Kind == AskPendingDecisionKindEnum.Proposal)
                return decision.Proposal!.ToolName + (String.IsNullOrWhiteSpace(decision.Proposal.SummaryText) ? "" : " - " + decision.Proposal.SummaryText);
            if (decision.Kind == AskPendingDecisionKindEnum.CliPermission) return Approvals.CliPermissionText.Title(decision.CliRequest!);
            return decision.Review!.Title;
        }

        private void ShowNothingHint(char key)
        {
            int pending = _Ask.Conversation.PendingProposals().Count(p => !_Ask.IsProposalBusy(p.Id))
                + _Ask.Conversation.PendingCliPermissions().Count(r => Decidable(r));
            string text = key == 'A' || key == 'd'
                ? (pending > 0 ? "No CLI permission request here. Pending: Alt+Down jumps to it" : "No CLI permission request here.")
                : (pending > 0 ? "Nothing to approve here. Pending: Alt+Down jumps to it" : "Nothing to approve here.");
            _Context.Notifications.Toast(Services.NotificationSeverityEnum.Info, _Context.Loc.T(text));
        }

        private string? RowValue(AskBlock? block, Func<AskBlock, List<string?>> pick)
        {
            if (block == null || block.WorkId == null) return null;
            List<string?> values = pick(block);
            if (SelectedRow >= 0 && SelectedRow < values.Count) return values[SelectedRow];
            if (values.Count == 1) return values[0];
            return null;
        }

        private void ShowLog(string missionId)
        {
            ArmadaClient client = _Context.Client;
            _ = Task.Run(async () =>
            {
                try
                {
                    LogResult? log = await client.GetMissionLogAsync(missionId).ConfigureAwait(false);
                    _Context.Dispatcher.Post(() =>
                    {
                        LogViewer viewer = new LogViewer();
                        viewer.SetText(log?.Log ?? "");
                        _Context.Modals.Show(new ViewerModal(_Context.Loc.T("Mission log") + ": " + missionId, viewer, _Context.Loc, _Context.Theme.Current));
                    });
                }
                catch (ArmadaApiException ex)
                {
                    _Context.Dispatcher.Post(() => _Context.ShowError("Failed to load the mission log.", ex));
                }
            });
        }

        private void ShowDiff(string missionId)
        {
            ArmadaClient client = _Context.Client;
            _ = Task.Run(async () =>
            {
                try
                {
                    DiffResult? diff = await client.GetMissionDiffAsync(missionId).ConfigureAwait(false);
                    _Context.Dispatcher.Post(() =>
                    {
                        DiffViewer viewer = new DiffViewer(diff?.Diff ?? "");
                        _Context.Modals.Show(new ViewerModal(_Context.Loc.T("Mission Diff") + ": " + missionId, viewer, _Context.Loc, _Context.Theme.Current));
                    });
                }
                catch (ArmadaApiException ex)
                {
                    _Context.Dispatcher.Post(() => _Context.ShowError("Failed to load the diff.", ex));
                }
            });
        }

        private void BeginSearch()
        {
            _Searching = true;
            _Search = "";
            _Match = -1;
        }

        private bool HandleSearchKey(KeyEvent key)
        {
            switch (key.Code)
            {
                case KeyCode.Escape:
                    _Searching = false;
                    _Search = "";
                    _Match = -1;
                    return true;
                case KeyCode.Enter:
                    _Searching = false;
                    _Match = -1;
                    FindNext(true);
                    return true;
                case KeyCode.Backspace:
                    if (_Search.Length > 0) _Search = _Search.Substring(0, _Search.Length - 1);
                    return true;
                case KeyCode.Character:
                    if ((key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0 || key.Rune < 32) return true;
                    _Search += Char.ConvertFromUtf32(key.Rune);
                    return true;
                default:
                    return true;
            }
        }

        private void FindNext(bool forward)
        {
            if (_Search.Length == 0 || _Flat.Count == 0) return;
            int count = _Flat.Count;
            int start = _Match < 0 ? (forward ? 0 : count - 1) : _Match + (forward ? 1 : -1);
            for (int step = 0; step < count; step++)
            {
                int idx = ((start + (forward ? step : -step)) % count + count) % count;
                if (_Flat[idx].ToPlainString().IndexOf(_Search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _Match = idx;
                    Detach();
                    _Scroll = Math.Max(0, idx - _Height / 2);
                    return;
                }
            }
        }

        #endregion
    }
}
