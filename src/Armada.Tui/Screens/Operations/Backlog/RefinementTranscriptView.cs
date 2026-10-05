namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A refinement transcript: each message with its role and age and wrapped content; <c>Up</c>/<c>Down</c> select a
    /// message (the dashboard's click-to-select used for Summarize and Apply), <c>Home</c>/<c>End</c> jump, and the
    /// selected message stays in view. Not thread-safe.
    /// </summary>
    public class RefinementTranscriptView : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Messages in sequence order.
        /// </summary>
        public List<ObjectiveRefinementMessage> Messages { get; private set; } = new List<ObjectiveRefinementMessage>();

        /// <summary>
        /// Selected message id, or empty.
        /// </summary>
        public string SelectedId { get; private set; } = "";

        /// <summary>
        /// Formats a message age.
        /// </summary>
        public Func<DateTime, string>? FormatAge { get; set; } = null;

        /// <summary>
        /// Raised after the selection changes.
        /// </summary>
        public event EventHandler<string>? SelectionChanged;

        #endregion

        #region Private-Members

        private int _Scroll = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace the messages (selection kept when it still exists).
        /// </summary>
        /// <param name="messages">Messages.</param>
        public void SetMessages(IEnumerable<ObjectiveRefinementMessage>? messages)
        {
            Messages = (messages ?? Enumerable.Empty<ObjectiveRefinementMessage>()).OrderBy(m => m.Sequence).ToList();
        }

        /// <summary>
        /// Select a message by id.
        /// </summary>
        /// <param name="id">Id, or empty.</param>
        public void Select(string? id)
        {
            string next = id ?? "";
            if (next == SelectedId) return;
            SelectedId = next;
            SelectionChanged?.Invoke(this, next);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (Messages.Count == 0) return false;
            int idx = Messages.FindIndex(m => m.Id == SelectedId);
            switch (key.Code)
            {
                case KeyCode.Up:
                    Select(Messages[Math.Max(0, idx < 0 ? Messages.Count - 1 : idx - 1)].Id);
                    return true;
                case KeyCode.Down:
                    Select(Messages[Math.Min(Messages.Count - 1, idx < 0 ? Messages.Count - 1 : idx + 1)].Id);
                    return true;
                case KeyCode.Home:
                    Select(Messages[0].Id);
                    return true;
                case KeyCode.End:
                    Select(Messages[Messages.Count - 1].Id);
                    return true;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind == MouseEventKind.Wheel)
            {
                _Scroll = Math.Max(0, _Scroll + (mouse.Button == MouseButton.WheelUp ? -3 : 3));
                return true;
            }

            return mouse.Kind == MouseEventKind.Press;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 6 || height < 1) return;
            if (Messages.Count == 0)
            {
                SurfaceText.Draw(surface, 0, 0, T("No messages yet."), Theme.Muted, width);
                return;
            }

            List<KeyValuePair<int, List<string>>> blocks = new List<KeyValuePair<int, List<string>>>();
            int selectedTop = 0;
            int selectedBottom = 0;
            int y = 0;
            for (int i = 0; i < Messages.Count; i++)
            {
                ObjectiveRefinementMessage m = Messages[i];
                List<string> lines = new List<string>();
                string age = FormatAge != null ? FormatAge(m.LastUpdateUtc) : "";
                lines.Add((m.Id == SelectedId ? "> " : "  ") + m.Role + "  " + age + (m.IsSelected ? "  [" + T("selected") + "]" : ""));
                string content = String.IsNullOrEmpty(m.Content) ? T("Waiting for content...") : m.Content;
                foreach (string raw in content.Replace("\r\n", "\n").Split('\n'))
                {
                    foreach (string w in TextCells.Wrap(raw, Math.Max(1, width - 4))) lines.Add("    " + w);
                }

                if (m.Id == SelectedId)
                {
                    selectedTop = y;
                    selectedBottom = y + lines.Count;
                }

                blocks.Add(new KeyValuePair<int, List<string>>(i, lines));
                y += lines.Count + 1;
            }

            if (selectedTop < _Scroll) _Scroll = selectedTop;
            if (selectedBottom > _Scroll + height) _Scroll = Math.Max(selectedTop, selectedBottom - height);
            _Scroll = Math.Clamp(_Scroll, 0, Math.Max(0, y - height));
            int row = -_Scroll;
            foreach (KeyValuePair<int, List<string>> block in blocks)
            {
                ObjectiveRefinementMessage m = Messages[block.Key];
                bool selected = m.Id == SelectedId;
                bool assistant = String.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase);
                for (int j = 0; j < block.Value.Count; j++)
                {
                    if (row >= 0 && row < height)
                    {
                        CellStyle style = j == 0 ? (assistant ? Theme.Accent : Theme.Info) : Theme.Text;
                        if (selected) style = IsFocused ? Theme.Selection : Theme.SelectionInactive;
                        if (selected) SurfaceText.FillRow(surface, 0, row, width, style);
                        SurfaceText.Draw(surface, 0, row, block.Value[j], style, width);
                    }

                    row++;
                }

                row++;
            }
        }

        #endregion
    }
}
