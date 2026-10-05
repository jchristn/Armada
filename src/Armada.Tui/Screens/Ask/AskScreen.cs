namespace Armada.Tui.Screens.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Approvals;
    using Armada.Tui.Ask;
    using Armada.Tui.Input;
    using Armada.Tui.Modals;
    using Armada.Tui.Routing;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Ask Armada (W2, route <c>/ask/:threadId?</c>): the conversation list, the conversation header (title edit,
    /// captain picker including "No captain (quick actions only)", auto-approve with its warning and banner, Summarize,
    /// More), the no-MCP note, the work strip, the transcript, inline quick-action forms, and the composer. All state
    /// lives in <see cref="AskController"/>, so leaving and returning keeps the conversation, its live turn, and the
    /// draft. Below 100 columns the list becomes an overlay toggled with <c>Ctrl+T</c>. <c>Esc</c> in the composer moves
    /// to the transcript (focusing the newest pending card); pressed twice while a turn runs it stops the turn.
    /// <c>Alt+Down</c> from anywhere on the screen focuses the oldest pending card. While a proposal or a CLI tool
    /// permission request in the open conversation awaits a decision, a strip above the composer says how many and how
    /// to decide (one line per kind), worded for where focus is, and the status bar hints follow the focused control
    /// (<see cref="ResolveHints"/>). The header's CLI tools line shows the conversation's CLI tool permission policy and
    /// its effective value; <c>p</c> (or a click on it) changes it, with Bypass offered only to admins and only after
    /// the strong warning.
    /// Not thread-safe.
    /// </summary>
    public class AskScreen : ScreenBase
    {
        #region Public-Members

        /// <summary>
        /// Width below which the conversation list becomes an overlay.
        /// </summary>
        public const int NarrowWidth = 100;

        /// <summary>
        /// Ask session.
        /// </summary>
        public AskController Ask { get; }

        /// <summary>
        /// Conversation list.
        /// </summary>
        public AskThreadListView ThreadList { get; }

        /// <summary>
        /// Transcript.
        /// </summary>
        public AskTranscriptView Transcript { get; }

        /// <summary>
        /// Composer.
        /// </summary>
        public AskComposerView Composer { get; }

        /// <summary>
        /// Open inline quick-action form, or null.
        /// </summary>
        public AskQuickActionForm? Form { get; private set; } = null;

        /// <summary>
        /// Title editor (while renaming from the header).
        /// </summary>
        public TextInput TitleInput { get; } = new TextInput();

        /// <summary>
        /// True while the header title is being edited.
        /// </summary>
        public bool EditingTitle { get; private set; } = false;

        /// <summary>
        /// The narrow-layout conversation list overlay is open.
        /// </summary>
        public bool ListOverlayOpen { get; private set; } = false;

        /// <summary>
        /// Index of the work strip item selected with <c>w</c>, or -1.
        /// </summary>
        public int WorkIndex { get; private set; } = -1;

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        /// <inheritdoc />
        public override string Title
        {
            get { return "Ask Armada"; }
        }

        /// <summary>
        /// Proposals in the open conversation waiting for a decision (not counting one whose approve or reject call
        /// is already in flight).
        /// </summary>
        public int PendingApprovals
        {
            get { return Ask.Conversation.PendingProposals().Count(p => !Ask.IsProposalBusy(p.Id)); }
        }

        /// <summary>
        /// CLI tool permission requests in the open conversation the user may decide (not counting one whose decision is
        /// already in flight).
        /// </summary>
        public int PendingPermissionRequests
        {
            get { return Ask.Conversation.PendingCliPermissions().Count(r => r.CanDecide && !Ask.IsCliPermissionBusy(r.Id)); }
        }

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get { return ResolveHints(null, false).Keys; }
        }

        #endregion

        #region Private-Members

        private DateTime _LastEscapeUtc = DateTime.MinValue;
        private int _CliPolicyRow = -1;
        private int _CliPolicyX = 0;
        private bool _Narrow = false;
        private string? _EscapeHint = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        /// <exception cref="InvalidOperationException">Thrown when the Ask session is not attached.</exception>
        public AskScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
            Ask = context.Ask ?? throw new InvalidOperationException("The Ask session is not attached.");
            ThreadList = new AskThreadListView(context, Ask);
            Transcript = new AskTranscriptView(context, Ask);
            Composer = new AskComposerView(context, Ask);
            TitleInput.MaxLength = 200;
            Composer.FormRequested += (s, action) => OpenForm(action);
            Composer.EscapePressed += (s, e) => OnComposerEscape();
            Composer.Sent += (s, e) => Transcript.ReturnToTail();
            ThreadList.CloseRequested += (s, e) => CloseOverlay();
            Ask.DraftChanged += OnDraftChanged;
            RebuildScope(Composer);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void OnActivated()
        {
            Ask.Viewing = true;
            Ask.Open(Route.Param("threadId"));
            Scope.Focus(Composer);
        }

        /// <inheritdoc />
        public override void OnDeactivated()
        {
            Ask.DraftChanged -= OnDraftChanged;
            Ask.Viewing = false;
            Ask.ComposerDraft = Composer.Text;
        }

        /// <inheritdoc />
        public override int DefaultRefreshSeconds()
        {
            return 0;
        }

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return () => Ask.Refresh();
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            List<ArmadaCommand> list = new List<ArmadaCommand>();
            Func<bool> hasThread = () => Ask.Conversation.Thread != null;
            list.Add(Cmd("ask.screen.new", "New conversation", () => Context.Navigate("/ask"), null, "n"));
            list.Add(Cmd("ask.screen.rename", "Rename conversation", () => BeginTitleEdit(), hasThread, "e"));
            list.Add(Cmd("ask.screen.captain", "Choose captain...", () => PickCaptain(), () => !Ask.Conversation.TurnActive, "c"));
            list.Add(Cmd("ask.screen.auto-approve", "Toggle auto-approve", () => Ask.ToggleAutoApprove(), hasThread, "ctrl+y"));
            list.Add(Cmd("ask.screen.cli-policy", "CLI tools permission policy...", () => PickCliPolicy(), hasThread, "p"));
            list.Add(Cmd("ask.screen.summarize", "Summarize conversation", () => { if (Ask.Conversation.Thread != null) Ask.Summarize(Ask.Conversation.Thread); }, hasThread, "s"));
            list.Add(Cmd("ask.screen.more", "More conversation actions...", () => ShowMoreMenu(), hasThread, "."));
            list.Add(Cmd("ask.screen.pin", "Pin or unpin conversation", () => { if (Ask.Conversation.Thread != null) Ask.TogglePin(Ask.Conversation.Thread); }, hasThread));
            list.Add(Cmd("ask.screen.archive", "Archive or unarchive conversation", () => { if (Ask.Conversation.Thread != null) Ask.ToggleArchive(Ask.Conversation.Thread); }, hasThread));
            list.Add(Cmd("ask.screen.delete", "Delete conversation", () => { if (Ask.Conversation.Thread != null) Ask.Delete(Ask.Conversation.Thread); }, hasThread));
            list.Add(Cmd("ask.screen.work", "Next tracked work", () => NextWork(), () => Ask.Conversation.TrackedWork.Count > 0, "w"));
            list.Add(Cmd("ask.screen.threads", "Toggle conversation list", () => ToggleList(), null, "ctrl+t"));
            list.Add(Cmd("ask.screen.review-approval", "Go to the oldest action waiting for approval", () => FocusOldestPending(), () => PendingApprovals + PendingPermissionRequests > 0, "alt+down"));
            list.Add(Cmd("ask.screen.stop", "Stop the captain", () => Ask.StopTurn(), () => Ask.Conversation.TurnActive, "ctrl+c"));
            list.Add(Cmd("ask.screen.thinking", "Toggle show thinking", () => Ask.ToggleShowThinking(), null, "alt+t", "ctrl+shift+t"));
            list.Add(Cmd("ask.screen.quick", "Quick actions...", () => { Scope.Focus(Composer); Composer.Text = "/"; }, null));
            list.Add(Cmd("ask.screen.copy", "Copy conversation as Markdown", () => Context.Clipboard.Copy(Transcript.ConversationMarkdown(), "Conversation"), hasThread));
            list.Add(Cmd("ask.screen.mcp-help", "How to connect a captain over MCP", () => Context.External.OpenUrl(AskController.InstructionsUrl(Ask.ActiveCaptain?.Runtime)), null));
            foreach (AskQuickAction action in Ask.QuickActions)
            {
                AskQuickAction a = action;
                ArmadaCommand qa = Cmd("ask.screen.qa." + a.Name, AskQuickActions.CommandOf(a) + " " + (String.IsNullOrEmpty(a.Title) ? a.Name : a.Title), () => { Scope.Focus(Composer); Composer.Choose(a); }, null);
                qa.SlashAliases.Add(AskQuickActions.CommandOf(a));
                list.Add(qa);
            }

            return list;
        }

        /// <summary>
        /// Open an inline quick-action form above the composer.
        /// </summary>
        /// <param name="action">Action.</param>
        /// <returns>The form, or null for actions without one.</returns>
        public AskQuickActionForm? OpenForm(AskQuickAction action)
        {
            AskQuickActionFormEnum kind = AskQuickActions.FormFor(action);
            AskQuickActionForm? form = kind == AskQuickActionFormEnum.Dispatch ? new AskDispatchFormView(Context, Ask, action)
                : kind == AskQuickActionFormEnum.FleetAction ? new AskFleetActionFormView(Context, Ask, action)
                : null;
            if (form == null) return null;
            CloseForm();
            Form = form;
            form.Closed += (s, e) => CloseForm();
            RebuildScope(form);
            form.Scope.FocusFirst();
            return form;
        }

        /// <summary>
        /// Close the inline form.
        /// </summary>
        public void CloseForm()
        {
            if (Form == null) return;
            Form = null;
            RebuildScope(Composer);
        }

        /// <inheritdoc />
        public override FocusHints ResolveHints(FocusHints? inner, bool textEntry)
        {
            IWidget? focused = Scope.Focused;
            int pending = PendingApprovals;
            int requests = PendingPermissionRequests;
            if (EditingTitle)
            {
                return FocusHints.Typing("Esc", "Cancel rename").Add("Enter", "Save");
            }

            if (ReferenceEquals(focused, Composer))
            {
                string leave = pending > 0 && requests == 0 ? "Leave the message box (then a approve, r reject)"
                    : requests > 0 && pending == 0 ? "Leave the message box (then a allow, d deny)"
                    : "Leave the message box";
                FocusHints hints = FocusHints.Typing("Esc", leave);
                if (pending + requests > 0) hints.Add("Alt+Down", "Go to approval");
                hints.Add("Enter", "Send");
                hints.Add("Ctrl+J", "Newline");
                hints.Add("/", "Quick actions");
                if (Ask.Conversation.TurnActive) hints.Add("Ctrl+C", "Stop");
                return hints;
            }

            if (ReferenceEquals(focused, Transcript))
            {
                if (Transcript.Searching) return FocusHints.Typing("Esc", "Cancel search").Add("Enter", "Find");
                FocusHints hints = new FocusHints();
                AskBlock? block = Transcript.Selected();
                AskActionProposal? proposal = block?.Proposal;
                List<AskPendingDecision> decisions = Transcript.SelectedDecisions();
                if (decisions.Count > 0)
                {
                    bool onlyCli = decisions.All(d => d.Kind == AskPendingDecisionKindEnum.CliPermission);
                    bool anyCli = decisions.Any(d => d.Kind == AskPendingDecisionKindEnum.CliPermission);
                    bool remember = decisions.Any(d => d.CliRequest != null && d.CliRequest.CanRemember);
                    if (onlyCli) hints.Add("a", "Allow once");
                    else hints.Add("a", "Approve").Add("r", "Reject");
                    if (anyCli && remember) hints.Add("A", "Allow and remember");
                    if (anyCli) hints.Add("d", "Deny");
                    if (proposal != null) hints.Add("x", "Arguments").Add("y", "Copy");
                }
                else if (proposal != null)
                {
                    hints.Add("x", "Arguments").Add("y", "Copy");
                }
                else if (block != null && block.WorkId != null)
                {
                    hints.Add("Enter", "Open").Add("o/l/d", "PR/Log/Diff");
                }
                else
                {
                    if (pending + requests > 0) hints.Add("Alt+Down", "Go to approval");
                    hints.Add("Enter", "Open");
                }

                hints.Add("Up/Down", "Select");
                hints.Add("Esc", "Back to the message box");
                hints.Add("End", "Live tail");
                return hints;
            }

            if (ReferenceEquals(focused, ThreadList))
            {
                if (ThreadList.RenamingId != null) return FocusHints.Typing("Esc", "Cancel rename").Add("Enter", "Save");
                if (ThreadList.SearchFocused) return FocusHints.Typing("Esc", "Back to the list").Add("Enter", "Search");
                return new FocusHints().Add("Enter", "Open").Add("n", "New").Add("/", "Search").Add(".", "Actions");
            }

            if (Form != null && ReferenceEquals(focused, Form))
            {
                FocusHints hints = new FocusHints(textEntry).Add("Esc", "Cancel").Add("Ctrl+S", "Submit");
                if (inner != null) hints.AddRange(inner.Keys);
                else hints.Add("Tab", "Next field");
                return hints;
            }

            return new FocusHints(textEntry);
        }

        /// <summary>
        /// Focus the transcript on the oldest action waiting for approval (<c>Alt+Down</c>, from anywhere on the
        /// screen, including the composer).
        /// </summary>
        /// <returns>True when a pending card was focused.</returns>
        public bool FocusOldestPending()
        {
            if (EditingTitle) EndTitleEdit(false);
            if (Form != null) CloseForm();
            Scope.Focus(Transcript);
            return Transcript.SelectOldestPending();
        }

        /// <summary>
        /// The pending-approval strip shown above the composer, worded for where focus is (from the composer: Esc
        /// first; on a selected pending card: a or r; elsewhere: Alt+Down), or null when nothing in this conversation
        /// waits for approval.
        /// </summary>
        /// <returns>Localized text or null.</returns>
        public string? PendingStripText()
        {
            List<string> lines = PendingStripLines();
            return lines.Count > 0 ? lines[0] : null;
        }

        /// <summary>
        /// The pending strip's lines: one for proposals and one for CLI tool permission requests, each worded for where
        /// focus is (when both kinds wait, the message box wording points at Alt+Down, which reaches the oldest of
        /// either). Empty when nothing waits.
        /// </summary>
        /// <returns>Localized lines.</returns>
        public List<string> PendingStripLines()
        {
            List<string> lines = new List<string>();
            int proposals = PendingApprovals;
            int requests = PendingPermissionRequests;
            bool both = proposals > 0 && requests > 0;
            List<AskPendingDecision> selected = ReferenceEquals(Scope.Focused, Transcript) && !Transcript.Searching ? Transcript.SelectedDecisions() : new List<AskPendingDecision>();
            if (proposals > 0) lines.Add(ProposalStripText(proposals, both, selected.Any(d => d.Kind == AskPendingDecisionKindEnum.Proposal)));
            if (requests > 0)
            {
                Dictionary<string, object?> args = Services.LocalizationArgs.Of("count", requests);
                bool selectedRequest = selected.Any(d => d.Kind == AskPendingDecisionKindEnum.CliPermission);
                if (ReferenceEquals(Scope.Focused, Composer) && !EditingTitle && !both)
                    lines.Add(Context.Loc.T("{count, plural, one {# CLI tool request waiting for permission: Esc, then a to allow once, A to allow and remember, or d to deny (Ctrl+A for all)} other {# CLI tool requests waiting for permission: Esc, then a to allow once, A to allow and remember, or d to deny (Ctrl+A for all)}}", args));
                else if (selectedRequest)
                    lines.Add(Context.Loc.T("{count, plural, one {# CLI tool request waiting for permission: a to allow once, A to allow and remember, or d to deny (Ctrl+A for all)} other {# CLI tool requests waiting for permission: a to allow once, A to allow and remember, or d to deny (Ctrl+A for all)}}", args));
                else
                    lines.Add(Context.Loc.T("{count, plural, one {# CLI tool request waiting for permission: Alt+Down to review it (Ctrl+A for all)} other {# CLI tool requests waiting for permission: Alt+Down to review the oldest (Ctrl+A for all)}}", args));
            }

            return lines;
        }

        private string ProposalStripText(int pending, bool both, bool selectedProposal)
        {
            Dictionary<string, object?> args = Services.LocalizationArgs.Of("count", pending);
            IWidget? focused = Scope.Focused;
            if (ReferenceEquals(focused, Composer) && !EditingTitle && !both)
            {
                return Context.Loc.T("{count, plural, one {# action waiting for approval: Esc, then a to approve or r to reject (Ctrl+A for all)} other {# actions waiting for approval: Esc, then a to approve or r to reject (Ctrl+A for all)}}", args);
            }

            if (selectedProposal)
            {
                return Context.Loc.T("{count, plural, one {# action waiting for approval: a to approve or r to reject (Ctrl+A for all)} other {# actions waiting for approval: a to approve or r to reject (Ctrl+A for all)}}", args);
            }

            return Context.Loc.T("{count, plural, one {# action waiting for approval: Alt+Down to review it (Ctrl+A for all)} other {# actions waiting for approval: Alt+Down to review the oldest (Ctrl+A for all)}}", args);
        }

        /// <summary>
        /// Focus the transcript (and the newest pending confirm card, when there is one).
        /// </summary>
        public void FocusTranscript()
        {
            Scope.Focus(Transcript);
            Transcript.SelectNewestPending();
        }

        /// <summary>
        /// Start editing the title in the header.
        /// </summary>
        /// <returns>True when editing.</returns>
        public bool BeginTitleEdit()
        {
            AskThread? thread = Ask.Conversation.Thread;
            if (thread == null) return false;
            EditingTitle = true;
            TitleInput.Value = thread.Title ?? "";
            RebuildScope(TitleInput);
            return true;
        }

        /// <summary>
        /// Pick the captain (includes "No captain (quick actions only)").
        /// </summary>
        /// <returns>The picker.</returns>
        public PickerModal<string> PickCaptain()
        {
            List<SelectOption<string>> options = new List<SelectOption<string>> { new SelectOption<string>("", Context.Loc.T("No captain (quick actions only)")) };
            foreach (Captain c in Ask.Captains)
            {
                string detail = !String.IsNullOrEmpty(c.Model) ? c.Model! : c.Runtime.ToString();
                options.Add(new SelectOption<string>(c.Id, c.Name + " (" + detail + ")", c.State.ToString()));
            }

            PickerModal<string> picker = new PickerModal<string>("Captain", options, Context.Loc, Context.Theme.Current);
            picker.List.SelectValue(Ask.ActiveCaptainId);
            Context.Modals.Show(picker, result =>
            {
                if (result is SelectOption<string> chosen) Ask.SetCaptain(chosen.Value);
            });
            return picker;
        }

        /// <summary>
        /// Change the conversation's CLI tool permission policy (<c>p</c>, or a click on the header's CLI tools line):
        /// Inherit, Refuse, Approve in Armada, or Bypass, which only admins may choose and only after the strong warning.
        /// </summary>
        /// <returns>The picker, or null without a conversation.</returns>
        public PickerModal<string>? PickCliPolicy()
        {
            AskThread? thread = Ask.Conversation.Thread;
            if (thread == null) return null;
            bool allowBypass = Context.Session.IsGlobalAdmin || Context.Session.IsTenantAdmin;
            CliPermissionPolicyEnum? current = thread.CliPermissionPolicy;
            List<SelectOption<string>> options = CliPermissionPolicyChoice.Options(Context.Loc, true, null, allowBypass, current);
            PickerModal<string> picker = new PickerModal<string>("CLI tools", options, Context.Loc, Context.Theme.Current);
            picker.List.SelectValue(CliPermissionPolicyChoice.ValueOf(current));
            Context.Modals.Show(picker, result =>
            {
                if (!(result is SelectOption<string> chosen)) return;
                CliPermissionPolicyEnum? next = CliPermissionPolicyChoice.Parse(chosen.Value);
                if (next == current) return;
                if (next == CliPermissionPolicyEnum.Bypass)
                {
                    CliPermissionPolicyChoice.ConfirmBypass(Context, () => Ask.SetCliPermissionPolicy(thread, next));
                    return;
                }

                Ask.SetCliPermissionPolicy(thread, next);
            });
            return picker;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind == MouseEventKind.Press && mouse.Button == MouseButton.Left && _CliPolicyRow >= 0 && mouse.Y == _CliPolicyRow && mouse.X >= _CliPolicyX)
            {
                PickCliPolicy();
                return true;
            }

            return base.HandleMouse(mouse);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (EditingTitle)
            {
                if (key.Code == KeyCode.Escape)
                {
                    EndTitleEdit(false);
                    return true;
                }

                if (key.Code == KeyCode.Enter)
                {
                    EndTitleEdit(true);
                    return true;
                }

                return TitleInput.HandleKey(key) || true;
            }

            if (ListOverlayOpen && ReferenceEquals(Scope.Focused, ThreadList) && key.Code == KeyCode.Escape && !ThreadList.SearchFocused && ThreadList.RenamingId == null)
            {
                CloseOverlay();
                return true;
            }

            if (Scope.HandleKey(key)) return true;
            if (key.Code == KeyCode.Escape && ReferenceEquals(Scope.Focused, Transcript))
            {
                if (Ask.Conversation.TurnActive && DoubleEscape()) return true;
                Scope.Focus(Composer);
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 30 || height < 8) return;
            bool narrow = width < NarrowWidth;
            if (narrow != _Narrow)
            {
                _Narrow = narrow;
                ListOverlayOpen = false;
                RebuildScope(Scope.Focused ?? Composer);
            }

            int listWidth = narrow ? 0 : Math.Clamp(width / 4, 26, 36);
            int x0 = narrow ? 0 : listWidth + 1;
            int cw = width - x0;
            // The conversation list, the transcript, the quick action form, and the composer are focus regions: each
            // sits in a box (see RegionFrames), so the column after the list and the rows between the regions stay
            // free for the box lines.
            if (!narrow) Scope.RenderChild(surface, ThreadList, new Rect(0, 0, listWidth, height));

            int top = RenderHeader(surface, x0, cw);
            int composerHeight = Math.Min(Composer.PreferredHeight(cw), Math.Max(3, height / 3));
            int formHeight = Form != null ? Math.Min(Form.PreferredHeight, Math.Max(6, (height - top - composerHeight) * 2 / 3)) : 0;
            // Each line wraps to a second row in narrow terminals so the keys at its end are never cut off.
            List<string> stripLines = new List<string>();
            foreach (string strip in PendingStripLines()) stripLines.AddRange(TextCells.Wrap("! " + strip, Math.Max(10, cw - 2)).Take(2));
            int stripHeight = stripLines.Count;
            int lines = 1 + (stripHeight > 0 ? 1 : 0) + (Form != null ? 1 : 0);
            int transcriptHeight = Math.Max(1, height - top - composerHeight - formHeight - stripHeight - lines);
            if (Ask.ConvError != null)
            {
                RenderConvError(surface, x0, top, cw, transcriptHeight);
            }
            else if (Ask.ConvLoading && Ask.Conversation.Messages.Count == 0)
            {
                SurfaceText.Draw(surface, x0 + 1, top + 1, T("Loading conversation..."), Theme.Muted, cw - 1);
                Scope.Place(Transcript, new Rect(x0, top, cw, transcriptHeight));
            }
            else
            {
                Scope.RenderChild(surface, Transcript, new Rect(x0, top, cw, transcriptHeight));
            }

            int y2 = top + transcriptHeight;
            SurfaceText.FillRow(surface, x0, y2, cw, Theme.Text);
            // The row under the transcript is the line between its box and the composer's; the stop hint is the
            // composer box's title.
            Composer.BoxTitle = _EscapeHint != null && Ask.Conversation.TurnActive ? T(_EscapeHint) : null;
            y2++;
            foreach (string stripLine in stripLines)
            {
                // Directly above the composer, so whoever is typing sees what waits on them and how to act on it;
                // "!" and bold keep it readable without color.
                CellStyle stripStyle = Theme.Warning.WithAttribute(CellAttributes.Bold, true);
                SurfaceText.FillRow(surface, x0, y2, cw, Theme.Text);
                SurfaceText.Draw(surface, x0 + 1, y2, stripLine, stripStyle, cw - 2);
                y2++;
            }

            if (stripHeight > 0) y2++;
            if (Form != null)
            {
                Scope.RenderChild(surface, Form, new Rect(x0, y2, cw, formHeight));
                y2 += formHeight + 1;
            }

            Scope.RenderChild(surface, Composer, new Rect(x0, y2, cw, Math.Max(2, height - y2)));
            if (narrow && ListOverlayOpen)
            {
                // The list opens over the conversation: only its box is drawn, so the conversation's regions are
                // not placed while it is open.
                int ow = Math.Min(width - 4, 40);
                SurfaceText.FillRect(surface, new Rect(0, 0, ow + 1, height), Theme.Text);
                foreach (IWidget child in Scope.Children) Scope.Place(child, Rect.Empty);
                Scope.RenderChild(surface, ThreadList, new Rect(0, 0, ow, height));
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void OnFocusChangedCore(bool focused)
        {
            base.OnFocusChangedCore(focused);
        }

        #endregion

        #region Private-Methods

        private ArmadaCommand Cmd(string id, string title, Action handler, Func<bool>? enabled, params string[] gestures)
        {
            ArmadaCommand c = new ArmadaCommand(id, title, CommandMenuEnum.Actions, handler, gestures);
            c.IsEnabled = enabled;
            c.Group = "Ask";
            return c;
        }

        private int RenderHeader(ISurface surface, int x0, int cw)
        {
            AskThread? thread = Ask.Conversation.Thread;
            int y = 0;
            SurfaceText.FillRow(surface, x0, y, cw, Theme.Header);
            string captainLabel;
            Captain? captain = Ask.ActiveCaptain;
            if (captain != null) captainLabel = captain.Name + " (" + (!String.IsNullOrEmpty(captain.Model) ? captain.Model : captain.Runtime.ToString()) + ")";
            else if (Ask.NoCaptain) captainLabel = T("No captain (quick actions only)");
            else captainLabel = Ask.ActiveCaptainId;
            // c works in the conversation, not while typing (it would type the letter); in the composer the hint says
            // Esc first. (s and . behave the same but are often cut off, so only the captain hint grows.)
            string prefix = ReferenceEquals(Scope.Focused, Composer) ? "Esc " : "";
            string right = T("Captain") + ": " + captainLabel + " [" + prefix + "c]";
            if (thread != null)
            {
                right += "   " + T("Auto-approve") + ": " + (thread.AutoApprove ? T("on") + " !" : T("off")) + " [Ctrl+Y]";
                right += "   [s] " + T("Summarize") + "  [.] " + T("More");
            }

            int rw = Math.Min(TextCells.Width(right), Math.Max(10, cw * 2 / 3));
            int titleWidth = Math.Max(8, cw - rw - 3);
            if (EditingTitle)
            {
                Scope.RenderChild(surface, TitleInput, new Rect(x0 + 1, y, titleWidth, 1));
            }
            else
            {
                string title = thread != null ? (String.IsNullOrEmpty(thread.Title) ? T("New conversation") : thread.Title) : T("New conversation");
                if (thread != null && thread.Pinned) title = "^ " + title;
                int tx = SurfaceText.Draw(surface, x0 + 1, y, title, Theme.HeaderAccent, titleWidth);
                if (thread != null && tx + 4 < titleWidth) SurfaceText.Draw(surface, x0 + 1 + tx + 1, y, "[e]", Theme.Header.WithForeground(Theme.Muted.Foreground), 3);
            }

            CellStyle rightStyle = thread != null && thread.AutoApprove ? Theme.Header.WithForeground(Theme.Warning.Foreground) : Theme.Header;
            SurfaceText.Draw(surface, x0 + cw - rw, y, right, rightStyle, rw);
            y++;
            // While the title is edited its field is a focus region; the row under it is its box's bottom line.
            if (EditingTitle) y++;
            if (thread != null && thread.AutoApprove)
            {
                SurfaceText.FillRow(surface, x0, y, cw, Theme.Warning.WithAttribute(CellAttributes.Reverse, true));
                SurfaceText.Draw(surface, x0 + 1, y++, "! " + T("Auto-approve is on: actions the captain proposes run immediately. Every action is still recorded below."), Theme.Warning.WithAttribute(CellAttributes.Reverse, true), cw - 2);
            }

            _CliPolicyRow = -1;
            if (thread != null)
            {
                // The conversation's CLI tool permission policy (the dashboard header's CLI tools select) and where its
                // effective value comes from; p (Esc first while typing) or a click changes it.
                CliPermissionResolution? resolution = thread.CliPermission;
                string current = thread.CliPermissionPolicy.HasValue ? CliPermissionText.Policy(Context.Loc, thread.CliPermissionPolicy) : CliPermissionText.Policy(Context.Loc, null);
                string key = ReferenceEquals(Scope.Focused, Composer) ? "Esc p" : "p";
                string line = T("CLI tools") + ": " + current + " [" + key + "]";
                string effective = CliPermissionText.Effective(Context.Loc, resolution, false);
                if (effective.Length > 0) line += "   " + effective;
                bool bypass = resolution != null ? resolution.Effective == CliPermissionPolicyEnum.Bypass : thread.CliPermissionPolicy == CliPermissionPolicyEnum.Bypass;
                _CliPolicyRow = y;
                _CliPolicyX = x0;
                SurfaceText.FillRow(surface, x0, y, cw, Theme.Text);
                SurfaceText.Draw(surface, x0 + 1, y++, line, bypass ? Theme.Warning : Theme.Muted, cw - 2);
                if (resolution?.FallbackReason != null)
                {
                    foreach (string note in TextCells.Wrap(CliPermissionText.Fallback(Context.Loc, resolution.FallbackReason.Value), cw - 2).Take(2))
                        SurfaceText.Draw(surface, x0 + 1, y++, note, Theme.Info, cw - 2);
                }
            }

            Ask.EnsureTools();
            if (Ask.McpMissing)
            {
                string note = T("This captain is not connected to Armada over MCP, so it can answer but cannot propose actions. Quick actions still work.");
                foreach (string line in TextCells.Wrap(note, cw - 2).Take(2)) SurfaceText.Draw(surface, x0 + 1, y++, line, Theme.Info, cw - 2);
                foreach (string line in TextCells.Wrap(T("How to connect") + ": " + AskController.InstructionsUrl(Ask.ActiveCaptain?.Runtime), cw - 2).Take(2))
                    SurfaceText.Draw(surface, x0 + 1, y++, line, Theme.Link, cw - 2);
            }

            List<AskTrackedWork> work = Ask.Conversation.TrackedWork;
            if (work.Count > 0)
            {
                int active = work.Count(w => AskWorkLogic.IsActive(w));
                string label = T("Work in this conversation") + " (" + (active > 0
                    ? Context.Loc.T("{{active}} active of {{total}}", Services.LocalizationArgs.Of("active", active, "total", work.Count))
                    : Context.Loc.T("{{total}} finished", Services.LocalizationArgs.Of("total", work.Count))) + ", w): ";
                int x = x0 + 1;
                x += SurfaceText.Draw(surface, x, y, label, Theme.Muted, cw - 2);
                for (int i = 0; i < work.Count && x < x0 + cw - 4; i++)
                {
                    AskTrackedWork item = work[i];
                    string chip = (AskWorkLogic.IsActive(item) ? "* " : "") + T(AskWorkLogic.EntityLabel(item.EntityType)) + " " + (String.IsNullOrEmpty(item.Title) ? item.EntityId : item.Title) + " " + StatusBadge.Label(item.Status ?? "");
                    CellStyle style = i == WorkIndex ? Theme.Selection : StatusBadge.Style(item.Status, Theme);
                    x += SurfaceText.Draw(surface, x, y, "[" + chip.Trim() + "]", style, x0 + cw - x) + 1;
                }

                y++;
            }

            // A blank row, not a rule: it is the top line of the transcript's box.
            y++;
            return y;
        }

        private void RenderConvError(ISurface surface, int x0, int top, int cw, int height)
        {
            int y = top + 1;
            foreach (string line in TextCells.Wrap("! " + T(Ask.ConvError ?? ""), cw - 2))
            {
                if (y >= top + height) break;
                SurfaceText.Draw(surface, x0 + 1, y++, line, Theme.Error, cw - 2);
            }

            y++;
            SurfaceText.Draw(surface, x0 + 1, y, "F5 " + T("Retry") + "   n " + T("Start a new conversation"), Theme.Muted, cw - 2);
            Scope.Place(Transcript, new Rect(x0, top, cw, height));
        }

        private void RebuildScope(IWidget? focus)
        {
            bool active = Scope.IsActive;
            if (active) Scope.SetActive(false);
            Scope.Clear();
            bool listInScope = !_Narrow || ListOverlayOpen;
            if (listInScope) AddChild(ThreadList);
            if (EditingTitle) AddChild(TitleInput);
            AddChild(Transcript);
            if (Form != null) AddChild(Form);
            AddChild(Composer);
            if (focus == null || !Scope.Focus(focus)) Scope.Focus(Composer);
            if (active) Scope.SetActive(true);
        }

        private void EndTitleEdit(bool commit)
        {
            EditingTitle = false;
            if (commit && Ask.Conversation.Thread != null) Ask.Rename(Ask.Conversation.Thread, TitleInput.Value);
            RebuildScope(Transcript);
        }

        private void ShowMoreMenu()
        {
            AskThread? thread = Ask.Conversation.Thread;
            if (thread == null) return;
            List<ActionMenuItem> items = new List<ActionMenuItem>
            {
                new ActionMenuItem("Rename", () => BeginTitleEdit(), "e"),
                new ActionMenuItem(thread.Pinned ? "Unpin" : "Pin", () => Ask.TogglePin(thread)),
                new ActionMenuItem(thread.Archived ? "Unarchive" : "Archive", () => Ask.ToggleArchive(thread)),
                new ActionMenuItem("Delete", () => Ask.Delete(thread)) { Destructive = true }
            };
            ActionMenu.Show(Context.Modals, "More conversation actions", items, Context.Loc, Theme);
        }

        private void NextWork()
        {
            List<AskTrackedWork> work = Ask.Conversation.TrackedWork;
            if (work.Count == 0) return;
            WorkIndex = (WorkIndex + 1) % work.Count;
            AskTrackedWork item = work[WorkIndex];
            if (Transcript.ScrollToWork(item.Id))
            {
                Scope.Focus(Transcript);
                return;
            }

            Context.Navigate(AskWorkLogic.Route(item.EntityType, item.EntityId));
        }

        private void ToggleList()
        {
            if (!_Narrow)
            {
                Scope.Focus(ReferenceEquals(Scope.Focused, ThreadList) ? (IWidget)Composer : ThreadList);
                return;
            }

            ListOverlayOpen = !ListOverlayOpen;
            RebuildScope(ListOverlayOpen ? (IWidget)ThreadList : Composer);
        }

        private void CloseOverlay()
        {
            if (!ListOverlayOpen) return;
            ListOverlayOpen = false;
            RebuildScope(Composer);
        }

        private void OnDraftChanged(object? sender, string draft)
        {
            Composer.Text = draft;
        }

        private void OnComposerEscape()
        {
            if (Ask.Conversation.TurnActive && DoubleEscape()) return;
            if (Ask.Conversation.TurnActive) return;
            FocusTranscript();
        }

        private bool DoubleEscape()
        {
            DateTime now = Context.Clock.UtcNow;
            if ((now - _LastEscapeUtc).TotalMilliseconds <= 1000)
            {
                _LastEscapeUtc = DateTime.MinValue;
                _EscapeHint = null;
                Ask.StopTurn();
                return true;
            }

            _LastEscapeUtc = now;
            _EscapeHint = "Press Esc again to stop the captain";
            return false;
        }

        #endregion
    }
}
