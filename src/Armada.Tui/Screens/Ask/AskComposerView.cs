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
    using ITextEntry = Armada.Tui.Widgets.ITextEntry;

    /// <summary>
    /// The Ask composer (W2.6, the dashboard's <c>AskComposer</c>): a multi-line TUIKit <see cref="TextEditor"/> where
    /// <c>Enter</c> sends and <c>Shift+Enter</c> or <c>Ctrl+J</c> inserts a newline (TUIKit's
    /// <see cref="SubmitKeyResolver"/>), <c>Ctrl+E</c> edits the draft in <c>$EDITOR</c>, <c>Up</c> on an empty composer
    /// recalls earlier messages, and <c>/</c> opens the quick-action menu (<c>Up</c>/<c>Down</c>, <c>Enter</c>/<c>Tab</c>
    /// choose, <c>Esc</c> dismisses). Choosing Dispatch or Fleet action raises <see cref="FormRequested"/>, Import opens
    /// the import route, and anything else runs at once. The footer carries Show thinking, the quick actions hint, the
    /// send or stop state, and "AI can make mistakes. Check answers." Not thread-safe.
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
            }
        }

        /// <summary>
        /// The quick-action menu is open.
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
        /// Quick actions matching the draft.
        /// </summary>
        /// <returns>Matches.</returns>
        public List<AskQuickAction> Matches()
        {
            return AskQuickActions.Filter(_Ask.QuickActions, Editor.Text);
        }

        /// <summary>
        /// Rows the composer wants at a width (menu, editor lines, footer).
        /// </summary>
        /// <param name="width">Width.</param>
        /// <returns>Rows.</returns>
        public int PreferredHeight(int width)
        {
            int menu = MenuOpen ? Math.Min(6, Matches().Count) + 1 : 0;
            int lines = Math.Clamp(Editor.VisualLineCount(Math.Max(10, width - 2)), 1, 6);
            return menu + lines + 1;
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
            List<AskQuickAction> matches = Matches();
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

                if ((key.Code == KeyCode.Enter && !alt) || (key.Code == KeyCode.Tab && key.Modifiers == KeyModifiers.None))
                {
                    Choose(matches[safe]);
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
            if (decision == SubmitDecision.Submit && !alt)
            {
                if (Editor.Text.Trim().StartsWith("/", StringComparison.Ordinal))
                {
                    List<AskQuickAction> exact = _Ask.QuickActions.Where(a => AskQuickActions.CommandOf(a) == Editor.Text.Trim()).ToList();
                    if (exact.Count == 1) Choose(exact[0]);
                    return true;
                }

                SendDraft();
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
            List<AskQuickAction> matches = Matches();
            if (!_MenuDismissed && matches.Count > 0)
            {
                int rows = Math.Min(6, matches.Count);
                SurfaceText.Draw(surface, 0, y++, T("Quick actions") + "  (Up/Down, Enter, Esc)", Theme.Muted, width);
                int safe = Math.Min(_MenuIndex, matches.Count - 1);
                int first = Math.Max(0, Math.Min(safe - rows + 1, matches.Count - rows));
                for (int i = first; i < first + rows && y < height - 2; i++)
                {
                    AskQuickAction a = matches[i];
                    CellStyle style = i == safe ? Theme.Selection : Theme.MenuDropdown;
                    SurfaceText.FillRow(surface, 0, y, width, style);
                    int x = SurfaceText.Draw(surface, 1, y, TextCells.PadRight(AskQuickActions.CommandOf(a), 15), style.WithForeground(Theme.Code.Foreground), width - 1) + 1;
                    x += SurfaceText.Draw(surface, x, y, TextCells.PadRight(String.IsNullOrEmpty(a.Title) ? a.Name : T(a.Title), 14), style, width - x);
                    if (!String.IsNullOrEmpty(a.Description)) SurfaceText.Draw(surface, x, y, T(a.Description), style.WithForeground(Theme.Muted.Foreground), width - x);
                    y++;
                }
            }

            int editorRows = Math.Max(1, height - y - 1);
            CellStyle input = IsFocused ? Theme.InputFocused : Theme.Input;
            SurfaceText.FillRect(surface, new Rect(0, y, width, editorRows), input);
            surface.DrawText(0, y, "> ", input.WithForeground(Theme.Accent.Foreground));
            Editor.NormalStyle = input;
            Editor.Render(new SurfaceView(surface, new Rect(2, y, width - 2, editorRows)));
            if (Editor.Text.Length == 0)
            {
                string placeholder = _Ask.NoCaptain ? T("Choose a captain to chat, or type / for quick actions") : T("Message the captain, or type / for quick actions");
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
                bool canSend = Editor.Text.Trim().Length > 0 && !_Ask.NoCaptain && !Editor.Text.Trim().StartsWith("/", StringComparison.Ordinal);
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
            x += SurfaceText.Draw(surface, x, y, "/ " + T("Quick actions") + "   Ctrl+E " + T("Editor") + "   ", Theme.Muted, width - rw - 1 - x);
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
        }

        #endregion
    }
}
