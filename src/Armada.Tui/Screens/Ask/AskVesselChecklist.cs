namespace Armada.Tui.Screens.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The vessel picker of the Fleet action form: a filterable checklist (case-insensitive name filter like the
    /// dashboard), <c>Space</c> toggles, <c>Ctrl+A</c> selects or clears every visible vessel, typing filters,
    /// <c>Backspace</c> edits the filter. Selection order is kept. Not thread-safe.
    /// </summary>
    public class AskVesselChecklist : ArmadaWidget, IPasteTarget
    {
        #region Public-Members

        /// <summary>
        /// Vessels as id and name pairs, in display order. Never null.
        /// </summary>
        public List<KeyValuePair<string, string>> Vessels { get; private set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Chosen vessel ids in selection order. Never null.
        /// </summary>
        public List<string> Selected { get; } = new List<string>();

        /// <summary>
        /// Filter text.
        /// </summary>
        public string Filter { get; private set; } = "";

        /// <summary>
        /// Cursor within the visible vessels.
        /// </summary>
        public int Cursor { get; private set; } = 0;

        /// <summary>
        /// True when every visible vessel is selected (and at least one is visible).
        /// </summary>
        public bool AllVisibleSelected
        {
            get
            {
                List<KeyValuePair<string, string>> visible = VisibleVessels();
                return visible.Count > 0 && visible.All(v => Selected.Contains(v.Key));
            }
        }

        #endregion

        #region Private-Members

        private int _Top = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace the vessels (sorted by name).
        /// </summary>
        /// <param name="vessels">Id and name pairs.</param>
        public void SetVessels(IEnumerable<KeyValuePair<string, string>> vessels)
        {
            Vessels = vessels.OrderBy(v => v.Value, StringComparer.OrdinalIgnoreCase).ToList();
            Cursor = 0;
        }

        /// <summary>
        /// Vessels matching the filter.
        /// </summary>
        /// <returns>Visible vessels.</returns>
        public List<KeyValuePair<string, string>> VisibleVessels()
        {
            string term = Filter.Trim();
            if (term.Length == 0) return Vessels;
            return Vessels.Where(v => v.Value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        /// <summary>
        /// Set the filter.
        /// </summary>
        /// <param name="text">Filter.</param>
        public void SetFilter(string text)
        {
            Filter = text ?? "";
            Cursor = 0;
            _Top = 0;
        }

        /// <summary>
        /// Toggle a vessel.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        public void Toggle(string id)
        {
            if (!Selected.Remove(id)) Selected.Add(id);
        }

        /// <summary>
        /// Select every visible vessel, or clear them when all are selected (Select visible / Clear visible).
        /// </summary>
        public void ToggleAllVisible()
        {
            List<KeyValuePair<string, string>> visible = VisibleVessels();
            if (AllVisibleSelected)
            {
                foreach (KeyValuePair<string, string> v in visible) Selected.Remove(v.Key);
            }
            else
            {
                foreach (KeyValuePair<string, string> v in visible)
                {
                    if (!Selected.Contains(v.Key)) Selected.Add(v.Key);
                }
            }
        }

        /// <inheritdoc />
        public bool HandlePaste(string text)
        {
            SetFilter(Filter + (text ?? "").Replace("\n", " ").Replace("\r", ""));
            return true;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            List<KeyValuePair<string, string>> visible = VisibleVessels();
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            switch (key.Code)
            {
                case KeyCode.Up:
                    if (Cursor == 0) return false;
                    Cursor--;
                    return true;
                case KeyCode.Down:
                    if (Cursor >= visible.Count - 1) return false;
                    Cursor++;
                    return true;
                case KeyCode.Backspace:
                    if (Filter.Length > 0) SetFilter(Filter.Substring(0, Filter.Length - 1));
                    return true;
                case KeyCode.Character:
                    if (ctrl && Char.ToLowerInvariant((char)key.Rune) == 'a')
                    {
                        ToggleAllVisible();
                        return true;
                    }

                    if (ctrl && Char.ToLowerInvariant((char)key.Rune) == 'u')
                    {
                        SetFilter("");
                        return true;
                    }

                    if ((key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0) return false;
                    if (key.Rune == ' ')
                    {
                        if (Cursor >= 0 && Cursor < visible.Count) Toggle(visible[Cursor].Key);
                        return true;
                    }

                    if (key.Rune < 32) return false;
                    SetFilter(Filter + Char.ConvertFromUtf32(key.Rune));
                    return true;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 4 || height < 1) return;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            string filter = T("Filter vessels") + ": " + (Filter.Length > 0 ? Filter : "") + (IsFocused ? "_" : "") + "   Space " + T("toggle") + "   Ctrl+A " + (AllVisibleSelected ? T("Clear visible") : T("Select visible"));
            SurfaceText.Draw(surface, 0, 0, filter, IsFocused ? Theme.Accent : Theme.Muted, width);
            List<KeyValuePair<string, string>> visible = VisibleVessels();
            int rows = Math.Max(0, height - 1);
            if (visible.Count == 0)
            {
                if (rows > 0) SurfaceText.Draw(surface, 2, 1, T("No vessels match."), Theme.Muted, width - 2);
                return;
            }

            Cursor = Math.Clamp(Cursor, 0, visible.Count - 1);
            if (Cursor < _Top) _Top = Cursor;
            if (Cursor >= _Top + rows) _Top = Cursor - rows + 1;
            for (int r = 0; r < rows; r++)
            {
                int i = _Top + r;
                if (i >= visible.Count) break;
                bool cursor = i == Cursor && IsFocused;
                CellStyle style = cursor ? Theme.Selection : Theme.Text;
                SurfaceText.FillRow(surface, 0, r + 1, width, style);
                SurfaceText.Draw(surface, 2, r + 1, (Selected.Contains(visible[i].Key) ? "[x] " : "[ ] ") + visible[i].Value, style, width - 2);
            }
        }

        #endregion
    }
}
