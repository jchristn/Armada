namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A read-only, focusable value on the Server settings page (detail fields, system paths, MCP snippets):
    /// shows one or more lines and copies its value with <c>Enter</c> or <c>y</c>. Focusable so the page's form
    /// scrolls to it. Not thread-safe.
    /// </summary>
    public class ServerTextField : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Displayed text (may span lines). Never null.
        /// </summary>
        public string Text
        {
            get { return _Text; }
            set { _Text = value ?? ""; }
        }

        /// <summary>
        /// Text copied, or null to copy <see cref="Text"/>. A value of "-" is never copied.
        /// </summary>
        public string? CopyValue { get; set; } = null;

        /// <summary>
        /// Style for the text, or null for the code style.
        /// </summary>
        public Func<ArmadaTheme, CellStyle>? Style { get; set; } = null;

        /// <summary>
        /// Raised with the value to copy.
        /// </summary>
        public event EventHandler<string>? CopyRequested;

        #endregion

        #region Private-Members

        private string _Text = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="text">Text.</param>
        public ServerTextField(string text = "")
        {
            Text = text;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Lines of the text.
        /// </summary>
        /// <returns>Lines.</returns>
        public List<string> Lines()
        {
            return new List<string>(_Text.Replace("\r\n", "\n").Split('\n'));
        }

        /// <summary>
        /// Raise <see cref="CopyRequested"/> when there is something to copy.
        /// </summary>
        /// <returns>True when a copy was requested.</returns>
        public bool RequestCopy()
        {
            string value = CopyValue ?? _Text;
            if (String.IsNullOrEmpty(value) || value == "-") return false;
            EventHandler<string>? handler = CopyRequested;
            if (handler != null) handler(this, value);
            return true;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            bool copy = key.Code == KeyCode.Enter || (key.Code == KeyCode.Character && key.Rune == 'y' && key.Modifiers == KeyModifiers.None);
            if (!copy) return false;
            RequestCopy();
            return true;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind != MouseEventKind.Press || mouse.Button != MouseButton.Left || mouse.ClickCount < 2) return false;
            return RequestCopy();
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            CellStyle style = Style != null ? Style(Theme) : Theme.Code;
            if (IsFocused) style = style.WithBackground(Theme.SelectionInactive.Background);
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), IsFocused ? Theme.SelectionInactive : Theme.Text);
            List<string> lines = Lines();
            for (int i = 0; i < lines.Count && i < height; i++) SurfaceText.Draw(surface, 0, i, lines[i], style, width);
            if (IsFocused && height > 0)
            {
                string hint = "y " + T("Copy");
                int hw = TextCells.Width(hint);
                if (width > hw + 4) SurfaceText.Draw(surface, width - hw, 0, hint, Theme.Muted.WithBackground(Theme.SelectionInactive.Background), hw);
            }
        }

        #endregion
    }
}
