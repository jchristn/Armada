namespace Armada.Tui.Shell
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Ask;
    using Armada.Tui.Screens.Ask;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The Ask dock (W2.7, <c>Ctrl+J</c>): a bottom panel that shows the active Ask conversation's live tail (the same
    /// rendering as the transcript, including the streaming reply and cards), the turn state, and pending approvals
    /// while another screen is open, with a one-line composer: typing goes to it, <c>Enter</c> sends to the open
    /// conversation (or starts one with the remembered captain), <c>Ctrl+C</c> stops a running turn, and
    /// <c>Ctrl+A</c> opens the Approvals center. Not thread-safe.
    /// </summary>
    public class AskDockView : ArmadaWidget, IPasteTarget
    {
        #region Public-Members

        /// <summary>
        /// Height in rows when visible. Default 9; clamped to 4..20.
        /// </summary>
        public int PreferredHeight
        {
            get { return _Height; }
            set { _Height = Math.Clamp(value, 4, 20); }
        }

        /// <summary>
        /// The dock's composer.
        /// </summary>
        public TextInput Input { get; } = new TextInput();

        #endregion

        #region Private-Members

        private readonly TuiContext? _Context;
        private readonly AskTranscriptBuilder _Builder = new AskTranscriptBuilder();
        private readonly AskViewState _View = new AskViewState();
        private int _Height = 9;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate without a session (placeholder rendering only).
        /// </summary>
        public AskDockView()
        {
        }

        /// <summary>
        /// Instantiate over the Ask session.
        /// </summary>
        /// <param name="context">Services.</param>
        public AskDockView(TuiContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            Input.Placeholder = "Message the captain (Enter sends)";
            Input.Submitted += (s, e) => SendInput();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Send the dock input to the open conversation.
        /// </summary>
        /// <returns>True when sent.</returns>
        public bool SendInput()
        {
            AskController? ask = _Context?.Ask;
            if (ask == null) return false;
            if (!ask.Send(Input.Value)) return false;
            Input.Value = "";
            return true;
        }

        /// <inheritdoc />
        public bool HandlePaste(string text)
        {
            return Input.HandlePaste(text);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            AskController? ask = _Context?.Ask;
            if (ask == null) return false;
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            if (ctrl && key.Code == KeyCode.Character && Char.ToLowerInvariant((char)key.Rune) == 'c' && ask.Conversation.TurnActive)
            {
                ask.StopTurn();
                return true;
            }

            if (ctrl && key.Code == KeyCode.Character && Char.ToLowerInvariant((char)key.Rune) != 'u' && Char.ToLowerInvariant((char)key.Rune) != 'w') return false;
            if (key.Code == KeyCode.Tab || key.Code == KeyCode.Escape) return false;
            return Input.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            AskController? ask = _Context?.Ask;
            CellStyle rule = IsFocused ? Theme.Accent : Theme.Border;
            if (ask == null || _Context == null)
            {
                SurfaceText.Draw(surface, 0, 0, "-- " + T("Ask Armada") + " " + new string('-', Math.Max(0, width)), rule, width);
                return;
            }

            AskConversation conv = ask.Conversation;
            string title = conv.Thread != null ? conv.Thread.Title : T("New conversation");
            string state = ask.Stopping ? T("Stopping...") : conv.TurnActive ? T("Replying...") : "";
            int pending = _Context.Approvals.Count;
            string head = "-- " + T("Ask Armada") + ": " + title + (state.Length > 0 ? "  [" + state + "]" : "") + " ";
            SurfaceText.Draw(surface, 0, 0, head + new string('-', Math.Max(0, width)), rule, width);
            string keys = (pending > 0 ? "[!" + pending.ToString(CultureInfo.InvariantCulture) + " " + T("approvals") + ": Ctrl+A] " : "") + "Ctrl+J " + T("Hide dock") + " ";
            int kw = TextCells.Width(keys);
            if (kw + TextCells.Width(head) < width) SurfaceText.Draw(surface, width - kw, 0, keys, pending > 0 ? Theme.Warning : rule, kw);

            int tailRows = Math.Max(0, height - 2);
            if (tailRows > 0)
            {
                List<AskBlock> blocks = _Builder.Build(ask, _View, Theme, _Context.Loc, _Context.Clock.UtcNow, Math.Max(20, width - 2));
                List<StyledText> lines = blocks.SelectMany(b => b.Lines).ToList();
                while (lines.Count > 0 && lines[lines.Count - 1].Width == 0) lines.RemoveAt(lines.Count - 1);
                int start = Math.Max(0, lines.Count - tailRows);
                for (int i = start; i < lines.Count; i++) surface.DrawStyledText(1, 1 + i - start, lines[i], Theme.Text);
            }

            CellStyle input = IsFocused ? Theme.InputFocused : Theme.Input;
            SurfaceText.FillRow(surface, 0, height - 1, width, input);
            SurfaceText.Draw(surface, 0, height - 1, "> ", input.WithForeground(Theme.Accent.Foreground), 2);
            Input.ApplyTheme(Theme);
            Input.Localizer = Localizer;
            Input.Render(new SurfaceView(surface, new Rect(2, height - 1, Math.Max(1, width - 2), 1)));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void OnFocusChangedCore(bool focused)
        {
            Input.OnFocusChanged(focused);
        }

        #endregion
    }
}
