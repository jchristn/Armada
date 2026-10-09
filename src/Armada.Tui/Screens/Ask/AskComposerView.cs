namespace Armada.Tui.Screens.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Tui.Ask;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// The Ask composer (W2.6, the dashboard's <c>AskComposer</c>): a multi-line TUIKit <see cref="TextEditor"/> where
    /// <c>Enter</c> sends and <c>Shift+Enter</c> or <c>Ctrl+J</c> inserts a newline (TUIKit's
    /// <see cref="SubmitKeyResolver"/>), <c>Ctrl+E</c> edits the draft in <c>$EDITOR</c>, <c>Up</c> on an empty composer
    /// recalls earlier messages, and <c>/</c> opens the command menu: quick actions and the local commands
    /// (<see cref="AskCommands"/>; <c>Up</c>/<c>Down</c> move, <c>Enter</c>/<c>Tab</c> run the highlighted entry,
    /// <c>Esc</c> dismisses). <c>Enter</c> on an exact command runs it with its arguments; an unknown command shows a
    /// hint and keeps the text. Choosing Dispatch or Fleet action raises <see cref="FormRequested"/>, Import opens the
    /// import route, <c>/help</c> opens the full menu, the other local commands go to
    /// <see cref="LocalCommandHandler"/>, and other quick actions run at once. The footer carries Show thinking, the
    /// commands hint, the send or stop state, and "AI can make mistakes. Check answers." Not thread-safe.
    /// </summary>
    public class AskComposerView : ArmadaWidget, IPasteTarget, ITextEntry
    {
        #region Public-Members

        /// <inheritdoc />
        public virtual bool AcceptsText
        {
            get { return true; }
        }

        /// <summary>
        /// The editor.
        /// </summary>
        public TextEditor Editor { get; } = new TextEditor();

        /// <summary>
        /// Submit and newline rules.
        /// </summary>
        public SubmitKeyResolver Submit { get; } = new SubmitKeyResolver();

        /// <summary>
        /// Draft text.
        /// </summary>
        public string Text
        {
            get { return Editor.Text; }
            set
            {
                Editor.Text = value ?? "";
                _Ask.ComposerDraft = Editor.Text;
                _MenuDismissed = false;
                _MenuIndex = 0;
                Hint = null;
                HintArgs = null;
            }
        }

        /// <summary>
        /// English hint shown above the editor (an unknown command, or what a local command did), or null.
        /// </summary>
        public string? Hint { get; private set; } = null;

        /// <summary>
        /// Placeholders of <see cref="Hint"/>, or null.
        /// </summary>
        public IDictionary<string, object?>? HintArgs { get; private set; } = null;

        /// <summary>
        /// Runs a local command (everything but <c>/help</c>, which the composer handles); set by the screen.
        /// </summary>
        public Func<AskLocalCommand, string, AskCommandOutcome>? LocalCommandHandler { get; set; } = null;

        /// <summary>
        /// The command menu is open.
        /// </summary>
        public bool MenuOpen
        {
            get { return !_MenuDismissed && Matches().Count > 0; }
        }

        /// <summary>
        /// Active menu entry.
        /// </summary>
        public int MenuIndex
        {
            get { return _MenuIndex; }
        }

        /// <summary>
        /// Raised when a quick action that needs a form is chosen.
        /// </summary>
        public event EventHandler<AskQuickAction>? FormRequested;

        /// <summary>
        /// Raised when <c>Esc</c> is not used by the composer (the screen handles stop and focus).
        /// </summary>
        public event EventHandler? EscapePressed;

        /// <summary>
        /// Raised after the draft was sent to the captain (the screen returns the transcript to the live tail).
        /// </summary>
        public event EventHandler? Sent;

        #endregion

        #region Private-Members

        private readonly TuiContext _Context;
        private readonly AskController _Ask;
        private bool _MenuDismissed = false;
        private int _MenuIndex = 0;
        private int _HistoryIndex = -1;
        private bool _Editing = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Services.</param>
        /// <param name="ask">Ask session.</param>
        public AskComposerView(TuiContext context, AskController ask)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Ask = ask ?? throw new ArgumentNullException(nameof(ask));
            Editor.WordWrap = true;
            Editor.Text = ask.ComposerDraft ?? "";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Menu entries (quick actions and local commands) matching the draft, exact matches first.
        /// </summary>
        /// <returns>Matches.</returns>
        public List<AskCommandItem> Matches()
        {
            return AskCommands.Filter(AskCommands.Catalog(_Ask.QuickActions), Editor.Text);
        }

        /// <summary>
        /// What Enter would do with the draft now (the highlighted entry while the menu is open).
        /// </summary>
        /// <returns>Parse result.</returns>
        public AskCommandParse Pending()
        {
            List<AskCommandItem> matches = Matches();
            AskCommandItem? highlighted = !_MenuDismissed && matches.Count > 0 ? matches[Math.Min(_MenuIndex, matches.Count - 1)] : null;
            return AskCommands.Resolve(AskCommands.Catalog(_Ask.QuickActions), Editor.Text, highlighted);
        }

        /// <summary>
        /// Run what Enter runs: a command, the unknown-command hint, or a send.
        /// </summary>
        /// <returns>True when something ran or was sent.</returns>
        public bool SubmitDraft()
        {
            AskCommandParse pending = Pending();
            if (pending.Kind == AskCommandParseKindEnum.Command && pending.Item != null) return RunItem(pending.Item, pending.Args);
            if (pending.Kind == AskCommandParseKindEnum.Unknown)
            {
                ShowOutcome(AskCommands.Unknown(pending.Typed));
                return false;
            }

            return SendDraft();
        }

        /// <summary>
        /// Run a menu entry: a quick action as <see cref="Choose"/> does, <c>/help</c> opens the full menu, a command
        /// that needs arguments fills the composer, and other local commands go to <see cref="LocalCommandHandler"/>.
        /// </summary>
        /// <param name="item">Entry.</param>
        /// <param name="args">Arguments (empty for none).</param>
        /// <returns>True when the command ran.</returns>
        public bool RunItem(AskCommandItem item, string args)
        {
            if (item == null) return false;
            if (item.Action != null)
            {
                Choose(item.Action);
                return true;
            }

            AskLocalCommand? local = item.Local;
            if (local == null) return false;
            if (local.Name == AskLocalCommandEnum.Help)
            {
                Text = "/";
                return true;
            }

            if (local.RequiresArgs && String.IsNullOrEmpty(args))
            {
                Text = local.Command + " ";
                return true;
            }

            // A new conversation must not inherit the command as the old conversation's saved draft.
            if (local.Name == AskLocalCommandEnum.New) Text = "";
            AskCommandOutcome outcome = LocalCommandHandler != null ? LocalCommandHandler(local, args ?? "") : new AskCommandOutcome(false);
            if (outcome.Ok) Text = "";
            ShowOutcome(outcome);
            return outcome.Ok;
        }

        /// <summary>
        /// Show a command's hint (or clear it).
        /// </summary>
        /// <param name="outcome">Outcome.</param>
        public void ShowOutcome(AskCommandOutcome outcome)
        {
            Hint = outcome?.Hint;
            HintArgs = outcome?.HintArgs;
        }

        /// <summary>
        /// The hint as shown (translated), or empty.
        /// </summary>
        /// <returns>Text.</returns>
        public string HintText()
        {
            if (String.IsNullOrEmpty(Hint)) return "";
            return HintArgs != null ? Localizer.T(Hint!, HintArgs) : Localizer.T(Hint!);
        }

        /// <summary>
        /// Rows the composer wants at a width (menu, editor lines, footer).
        /// </summary>
        /// <param name="width">Width.</param>
        /// <returns>Rows.</returns>
        public int PreferredHeight(int width)
        {
            int menu = MenuOpen ? Math.Min(6, Matches().Count) + 1 : 0;
            int hint = String.IsNullOrEmpty(Hint) ? 0 : 1;
            int lines = Math.Clamp(Editor.VisualLineCount(Math.Max(10, width - 2)), 1, 6);
            return menu + hint + lines + 1;
        }

        /// <summary>
        /// Choose a quick action as if picked from the menu (empty-state chips and the palette).
        /// </summary>
        /// <param name="action">Action.</param>
        public void Choose(AskQuickAction action)
        {
            if (action == null) return;
            Text = "";
            _MenuIndex = 0;
            switch (AskQuickActions.FormFor(action))
            {
                case AskQuickActionFormEnum.Import:
                    _Context.Navigate("/vessels/import");
                    return;
                case AskQuickActionFormEnum.None:
                    _Ask.RunQuickAction(action, new JsonObject());
                    return;
                default:
                    FormRequested?.Invoke(this, action);
                    return;
            }
        }

        /// <summary>
        /// Send the draft to the captain (no-op when empty, a turn runs, no captain, or the draft is a slash command).
        /// </summary>
        /// <returns>True when sent.</returns>
        public bool SendDraft()
        {
            string text = Editor.Text.Trim();
            if (text.Length == 0) return false;
            if (!_Ask.Send(text)) return false;
            Text = "";
            _HistoryIndex = -1;
            Sent?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// Open the draft in <c>$EDITOR</c> and replace it with the result.
        /// </summary>
        public void EditExternally()
        {
            if (_Editing) return;
            _Editing = true;
            string initial = Editor.Text;
            _ = Task.Run(async () =>
            {
                string edited;
                try { edited = await _Context.External.EditTextAsync(initial, ".md").ConfigureAwait(false); }
                catch (Exception) { edited = initial; }
                _Context.Dispatcher.Post(() =>
                {
                    _Editing = false;
                    Text = edited.TrimEnd('\n', '\r');
                });
            });
        }

        /// <inheritdoc />
        public bool HandlePaste(string text)
        {
            if (String.IsNullOrEmpty(text)) return true;
            Editor.InsertText(text.Replace("\r\n", "\n").Replace('\r', '\n'));
            Changed();
            return true;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            bool alt = (key.Modifiers & KeyModifiers.Alt) != 0;
            List<AskCommandItem> matches = Matches();
            bool menu = !_MenuDismissed && matches.Count > 0;
            if (menu)
            {
                int safe = Math.Min(_MenuIndex, matches.Count - 1);
                if (key.Code == KeyCode.Down && !ctrl && !alt)
                {
                    _MenuIndex = (safe + 1) % matches.Count;
                    return true;
                }

                if (key.Code == KeyCode.Up && !ctrl && !alt)
                {
                    _MenuIndex = (safe - 1 + matches.Count) % matches.Count;
                    return true;
                }

                if (key.Code == KeyCode.Tab && key.Modifiers == KeyModifiers.None)
                {
                    RunItem(matches[safe], "");
                    return true;
                }

                if (key.Code == KeyCode.Escape)
                {
                    _MenuDismissed = true;
                    return true;
                }
            }

            if (key.Code == KeyCode.Escape)
            {
                EscapePressed?.Invoke(this, EventArgs.Empty);
                return true;
            }

            SubmitDecision decision = Submit.Resolve(key);
            if ((decision == SubmitDecision.Submit && !alt) || (menu && key.Code == KeyCode.Enter && !alt))
            {
                SubmitDraft();
                return true;
            }

            if (decision == SubmitDecision.InsertNewline)
            {
                Editor.InsertNewline();
                Changed();
                return true;
            }

            if (ctrl && key.Code == KeyCode.Character)
            {
                char c = Char.ToLowerInvariant((char)key.Rune);
                if (c == 'e')
                {
                    EditExternally();
                    return true;
                }

                if (c == 'z')
                {
                    Editor.HandleKey(key);
                    Changed();
                    return true;
                }

                if (c == 'u')
                {
                    Text = "";
                    return true;
                }

                return false;
            }

            if (alt) return false;
            if (key.Code == KeyCode.Up && (Editor.Text.Length == 0 || _HistoryIndex >= 0) && _Ask.SentHistory.Count > 0)
            {
                _HistoryIndex = _HistoryIndex < 0 ? _Ask.SentHistory.Count - 1 : Math.Max(0, _HistoryIndex - 1);
                Recall();
                return true;
            }

            if (key.Code == KeyCode.Down && _HistoryIndex >= 0)
            {
                _HistoryIndex++;
                if (_HistoryIndex >= _Ask.SentHistory.Count)
                {
                    _HistoryIndex = -1;
                    Text = "";
                }
                else
                {
                    Recall();
                }

                return true;
            }

            if (key.Code == KeyCode.Up || key.Code == KeyCode.Down)
            {
                if (Editor.CaretRow == 0 && key.Code == KeyCode.Up) return false;
                Editor.HandleKey(key);
                return true;
            }

            if (key.Code == KeyCode.Tab || key.Code == KeyCode.PageUp || key.Code == KeyCode.PageDown) return false;
            bool handled = Editor.HandleKey(key);
            if (handled && (key.Code == KeyCode.Character || key.Code == KeyCode.Backspace || key.Code == KeyCode.Delete))
            {
                _HistoryIndex = -1;
                Changed();
            }

            return handled;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            return mouse.Kind == MouseEventKind.Press;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 10 || height < 2) return;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            int y = 0;
            List<AskCommandItem> matches = Matches();
            if (!_MenuDismissed && matches.Count > 0)
            {
                int rows = Math.Min(6, matches.Count);
                SurfaceText.Draw(surface, 0, y++, T("Commands") + "  (Up/Down, Enter, Esc)", Theme.Muted, width);
                int safe = Math.Min(_MenuIndex, matches.Count - 1);
                int first = Math.Max(0, Math.Min(safe - rows + 1, matches.Count - rows));
                for (int i = first; i < first + rows && y < height - 2; i++)
                {
                    AskCommandItem a = matches[i];
                    CellStyle style = i == safe ? Theme.Selection : Theme.MenuDropdown;
                    SurfaceText.FillRow(surface, 0, y, width, style);
                    string command = a.Usage.Length > 0 ? a.Command + " " + a.Usage : a.Command;
                    int x = SurfaceText.Draw(surface, 1, y, TextCells.PadRight(command, 18), style.WithForeground(Theme.Code.Foreground), width - 1) + 1;
                    x += SurfaceText.Draw(surface, x, y, TextCells.PadRight(T(a.Title), 18), style, width - x);
                    if (!String.IsNullOrEmpty(a.Description)) SurfaceText.Draw(surface, x, y, T(a.Description), style.WithForeground(Theme.Muted.Foreground), width - x);
                    y++;
                }
            }

            if (!String.IsNullOrEmpty(Hint) && y < height - 2)
            {
                SurfaceText.Draw(surface, 0, y++, "! " + HintText(), Theme.Warning, width);
            }

            int editorRows = Math.Max(1, height - y - 1);
            CellStyle input = IsFocused ? Theme.InputFocused : Theme.Input;
            SurfaceText.FillRect(surface, new Rect(0, y, width, editorRows), input);
            surface.DrawText(0, y, "> ", input.WithForeground(Theme.Accent.Foreground));
            Editor.NormalStyle = input;
            Editor.Render(new SurfaceView(surface, new Rect(2, y, width - 2, editorRows)));
            if (Editor.Text.Length == 0)
            {
                string placeholder = _Ask.NoCaptain ? T("Choose a captain to chat, or type / for commands") : T("Message the captain, or type / for commands");
                SurfaceText.Draw(surface, IsFocused ? 3 : 2, y, placeholder, input.WithForeground(Theme.Muted.Foreground), width - 3);
            }

            int footer = y + editorRows;
            if (footer < height) RenderFooter(surface, footer, width);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void OnFocusChangedCore(bool focused)
        {
            Editor.OnFocusChanged(focused);
        }

        #endregion

        #region Private-Methods

        private void RenderFooter(ISurface surface, int y, int width)
        {
            string right;
            CellStyle rightStyle;
            if (_Ask.Stopping)
            {
                right = T("Stopping...");
                rightStyle = Theme.Warning;
            }
            else if (_Ask.Conversation.TurnActive)
            {
                right = "Ctrl+C " + T("Stop");
                rightStyle = Theme.Warning;
            }
            else if (_Ask.ActionBusy)
            {
                right = T("Working...");
                rightStyle = Theme.Info;
            }
            else
            {
                AskCommandParse pending = Pending();
                bool canSend = pending.Kind == AskCommandParseKindEnum.Command
                    || (pending.Kind == AskCommandParseKindEnum.Text && Editor.Text.Trim().Length > 0 && !_Ask.NoCaptain);
                right = "Enter " + T("Send");
                rightStyle = canSend ? Theme.Accent : Theme.Muted;
            }

            int rw = TextCells.Width(right);
            int x = 0;
            if (IsFocused)
            {
                // Not color alone: while the composer has focus it says so in words, so it is clear that letters type
                // here and single-key shortcuts wait until Esc.
                string marker = TypingMarker.Text(Localizer);
                x += SurfaceText.Draw(surface, x, y, marker, TypingMarker.Style(Theme), width - rw - 1 - x);
                x += SurfaceText.Draw(surface, x, y, "  ", Theme.Muted, width - rw - 1 - x);
            }

            x += SurfaceText.Draw(surface, x, y, (_Ask.ShowThinking ? "[x] " : "[ ] ") + T("Show thinking") + " (Alt+T)   ", Theme.Muted, width - rw - 1 - x);
            x += SurfaceText.Draw(surface, x, y, "/ " + T("Commands") + "   Ctrl+E " + T("Editor") + "   ", Theme.Muted, width - rw - 1 - x);
            SurfaceText.Draw(surface, x, y, T("AI can make mistakes. Check answers."), Theme.Muted, width - rw - 1 - x);
            SurfaceText.Draw(surface, width - rw, y, right, rightStyle, rw);
        }

        private void Recall()
        {
            if (_HistoryIndex < 0 || _HistoryIndex >= _Ask.SentHistory.Count) return;
            Editor.Text = _Ask.SentHistory[_HistoryIndex];
            _Ask.ComposerDraft = Editor.Text;
        }

        private void Changed()
        {
            _Ask.ComposerDraft = Editor.Text;
            _MenuDismissed = false;
            _MenuIndex = 0;
            Hint = null;
            HintArgs = null;
        }

        #endregion
    }
}
