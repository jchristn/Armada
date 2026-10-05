namespace Armada.Tui.Screens.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;
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

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                IWidget? focused = Scope.Focused;
                if (ReferenceEquals(focused, Composer))
                {
                    hints.Add(Hint("Enter", "Send"));
                    hints.Add(Hint("Ctrl+J", "Newline"));
                    hints.Add(Hint("/", "Quick actions"));
                    if (Ask.Conversation.TurnActive) hints.Add(Hint("Ctrl+C", "Stop"));
                    hints.Add(Hint("Esc", "Messages"));
                }
                else if (ReferenceEquals(focused, Transcript))
                {
                    hints.Add(Hint("Up/Down", "Focus"));
                    hints.Add(Hint("a/r", "Approve/Reject"));
                    hints.Add(Hint("x", "Arguments"));
                    hints.Add(Hint("Enter", "Open"));
                    hints.Add(Hint("End", "Live tail"));
                }
                else if (ReferenceEquals(focused, ThreadList))
                {
                    hints.Add(Hint("Enter", "Open"));
                    hints.Add(Hint("n", "New"));
                    hints.Add(Hint("/", "Search"));
                    hints.Add(Hint(".", "Actions"));
                }
                else if (Form != null && ReferenceEquals(focused, Form))
                {
                    hints.Add(Hint("Ctrl+S", "Submit"));
                    hints.Add(Hint("Esc", "Cancel"));
                }

                return hints;
            }
        }

        #endregion

        #region Private-Members

        private DateTime _LastEscapeUtc = DateTime.MinValue;
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
            list.Add(Cmd("ask.screen.summarize", "Summarize conversation", () => { if (Ask.Conversation.Thread != null) Ask.Summarize(Ask.Conversation.Thread); }, hasThread, "s"));
            list.Add(Cmd("ask.screen.more", "More conversation actions...", () => ShowMoreMenu(), hasThread, "."));
            list.Add(Cmd("ask.screen.pin", "Pin or unpin conversation", () => { if (Ask.Conversation.Thread != null) Ask.TogglePin(Ask.Conversation.Thread); }, hasThread));
            list.Add(Cmd("ask.screen.archive", "Archive or unarchive conversation", () => { if (Ask.Conversation.Thread != null) Ask.ToggleArchive(Ask.Conversation.Thread); }, hasThread));
            list.Add(Cmd("ask.screen.delete", "Delete conversation", () => { if (Ask.Conversation.Thread != null) Ask.Delete(Ask.Conversation.Thread); }, hasThread));
            list.Add(Cmd("ask.screen.work", "Next tracked work", () => NextWork(), () => Ask.Conversation.TrackedWork.Count > 0, "w"));
            list.Add(Cmd("ask.screen.threads", "Toggle conversation list", () => ToggleList(), null, "ctrl+t"));
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
            if (!narrow)
            {
                Scope.RenderChild(surface, ThreadList, new Rect(0, 0, listWidth, height));
                for (int y = 0; y < height; y++) surface.DrawText(listWidth, y, "|", Theme.Border);
            }

            int top = RenderHeader(surface, x0, cw);
            int composerHeight = Math.Min(Composer.PreferredHeight(cw), Math.Max(3, height / 3));
            int formHeight = Form != null ? Math.Min(Form.PreferredHeight, Math.Max(6, (height - top - composerHeight) * 2 / 3)) : 0;
            int transcriptHeight = Math.Max(1, height - top - composerHeight - formHeight - 1);
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
            string sep = new string('-', cw);
            if (_EscapeHint != null && Ask.Conversation.TurnActive) sep = "-- " + T(_EscapeHint) + " " + new string('-', cw);
            SurfaceText.Draw(surface, x0, y2, sep, Theme.Border, cw);
            y2++;
            if (Form != null)
            {
                Scope.RenderChild(surface, Form, new Rect(x0, y2, cw, formHeight));
                y2 += formHeight;
            }

            Scope.RenderChild(surface, Composer, new Rect(x0, y2, cw, Math.Max(2, height - y2)));
            if (narrow && ListOverlayOpen)
            {
                int ow = Math.Min(width - 4, 40);
                SurfaceText.FillRect(surface, new Rect(0, 0, ow + 1, height), Theme.Text);
                Scope.RenderChild(surface, ThreadList, new Rect(0, 0, ow, height));
                for (int y = 0; y < height; y++) surface.DrawText(ow, y, "|", Theme.Border);
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

        private static KeyValuePair<string, string> Hint(string key, string label)
        {
            return new KeyValuePair<string, string>(key, label);
        }

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
            if (thread != null && thread.AutoApprove)
            {
                SurfaceText.FillRow(surface, x0, y, cw, Theme.Warning.WithAttribute(CellAttributes.Reverse, true));
                SurfaceText.Draw(surface, x0 + 1, y++, "! " + T("Auto-approve is on: actions the captain proposes run immediately. Every action is still recorded below."), Theme.Warning.WithAttribute(CellAttributes.Reverse, true), cw - 2);
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

            SurfaceText.Draw(surface, x0, y++, new string('-', cw), Theme.Border, cw);
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
