namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A read-only, word-wrapped, scrollable list of styled paragraphs for the setup wizard's explanation and
    /// handoff steps. <c>Up</c>/<c>Down</c>/<c>PgUp</c>/<c>PgDn</c> scroll while focused. Not thread-safe.
    /// </summary>
    public class SetupWizardTextView : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Paragraphs. Never null.
        /// </summary>
        public List<SetupWizardLine> Lines { get; } = new List<SetupWizardLine>();

        /// <summary>
        /// First visible wrapped row.
        /// </summary>
        public int ScrollOffset { get; private set; } = 0;

        #endregion

        #region Private-Members

        private int _LastHeight = 1;
        private int _LastTotal = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace the paragraphs (keeps the scroll position when possible).
        /// </summary>
        /// <param name="lines">Paragraphs.</param>
        public void SetLines(IEnumerable<SetupWizardLine> lines)
        {
            Lines.Clear();
            if (lines != null) Lines.AddRange(lines);
        }

        /// <summary>
        /// Plain text of every paragraph, one per line.
        /// </summary>
        /// <returns>Text.</returns>
        public string PlainText()
        {
            List<string> parts = new List<string>();
            foreach (SetupWizardLine line in Lines) parts.Add(line.Text);
            return String.Join("\n", parts);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            int max = Math.Max(0, _LastTotal - _LastHeight);
            switch (key.Code)
            {
                case KeyCode.Up:
                    if (ScrollOffset == 0) return false;
                    ScrollOffset--;
                    return true;
                case KeyCode.Down:
                    if (ScrollOffset >= max) return false;
                    ScrollOffset++;
                    return true;
                case KeyCode.PageUp:
                    ScrollOffset = Math.Max(0, ScrollOffset - Math.Max(1, _LastHeight - 1));
                    return true;
                case KeyCode.PageDown:
                    ScrollOffset = Math.Min(max, ScrollOffset + Math.Max(1, _LastHeight - 1));
                    return true;
                case KeyCode.Home:
                    ScrollOffset = 0;
                    return true;
                case KeyCode.End:
                    ScrollOffset = max;
                    return true;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind != MouseEventKind.Wheel) return mouse.Kind == MouseEventKind.Press;
            int max = Math.Max(0, _LastTotal - _LastHeight);
            if (mouse.Button == MouseButton.WheelUp) ScrollOffset = Math.Max(0, ScrollOffset - 3);
            else if (mouse.Button == MouseButton.WheelDown) ScrollOffset = Math.Min(max, ScrollOffset + 3);
            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 4 || height < 1) return;
            List<KeyValuePair<SetupWizardLine, string>> rows = new List<KeyValuePair<SetupWizardLine, string>>();
            foreach (SetupWizardLine line in Lines)
            {
                if (line.Text.Length == 0)
                {
                    rows.Add(new KeyValuePair<SetupWizardLine, string>(line, ""));
                    continue;
                }

                foreach (string wrapped in TextCells.Wrap(line.Text, Math.Max(1, width - line.Indent - 1)))
                {
                    rows.Add(new KeyValuePair<SetupWizardLine, string>(line, wrapped));
                }
            }

            _LastHeight = height;
            _LastTotal = rows.Count;
            ScrollOffset = Math.Clamp(ScrollOffset, 0, Math.Max(0, rows.Count - height));
            for (int y = 0; y < height && ScrollOffset + y < rows.Count; y++)
            {
                KeyValuePair<SetupWizardLine, string> row = rows[ScrollOffset + y];
                CellStyle style = row.Key.Style != null ? row.Key.Style(Theme) : Theme.Text;
                SurfaceText.Draw(surface, row.Key.Indent, y, row.Value, style, width - row.Key.Indent);
            }

            if (rows.Count > height && IsFocused)
            {
                string more = ScrollOffset + height < rows.Count ? "v" : "^";
                surface.DrawText(width - 1, height - 1, more, Theme.Muted);
            }
        }

        #endregion
    }
}
