namespace Armada.Tui.Screens.Kit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A <see cref="DetailView"/> whose rows can carry a route: <c>Enter</c> on a linked row navigates (the
    /// dashboard's links to captains, missions, vessels, and voyages). Also reports the rows it needs at a width so
    /// detail pages can size it. Not thread-safe.
    /// </summary>
    public class RecordDetailView : DetailView
    {
        #region Public-Members

        /// <summary>
        /// Raised with the route of a linked row when <c>Enter</c> is pressed on it.
        /// </summary>
        public event EventHandler<string>? LinkActivated;

        #endregion

        #region Private-Members

        private readonly List<string?> _Links = new List<string?>();
        private readonly List<KeyValuePair<string, string?>> _Measure = new List<KeyValuePair<string, string?>>();
        private int _Sections = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Remove every row.
        /// </summary>
        public new void Clear()
        {
            base.Clear();
            _Links.Clear();
            _Measure.Clear();
            _Sections = 0;
        }

        /// <summary>
        /// Add a section heading.
        /// </summary>
        /// <param name="title">English title.</param>
        public new void AddSection(string title)
        {
            base.AddSection(title);
            _Sections++;
        }

        /// <summary>
        /// Add a row, optionally linked to a route.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="value">Value.</param>
        /// <param name="link">Route, or null.</param>
        /// <param name="style">Value style, or null (links default to the link style).</param>
        public void AddRow(string label, string? value, string? link = null, Func<ArmadaTheme, CellStyle>? style = null)
        {
            Add(label, value, style ?? (link != null ? (Func<ArmadaTheme, CellStyle>)(t => t.Link) : null));
            _Links.Add(link);
            _Measure.Add(new KeyValuePair<string, string?>(label, value));
        }

        /// <summary>
        /// Rows needed to show everything at a width.
        /// </summary>
        /// <param name="width">Width.</param>
        /// <returns>Rows.</returns>
        public int HeightFor(int width)
        {
            int labelWidth = Math.Min(Math.Max(10, _Measure.Select(m => TextCells.Width(T(m.Key))).DefaultIfEmpty(8).Max() + 2), Math.Max(8, width / 3));
            int valueWidth = Math.Max(1, width - labelWidth);
            int rows = _Sections * 2;
            foreach (KeyValuePair<string, string?> m in _Measure)
            {
                rows += TextCells.Wrap(String.IsNullOrEmpty(m.Value) ? "-" : m.Value, valueWidth).Count;
            }

            return rows;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Enter && key.Modifiers == KeyModifiers.None)
            {
                int index = Cursor;
                if (index >= 0 && index < _Links.Count && _Links[index] != null)
                {
                    EventHandler<string>? handler = LinkActivated;
                    if (handler != null) handler(this, _Links[index]!);
                    return true;
                }

                return false;
            }

            return base.HandleKey(key);
        }

        #endregion
    }
}
