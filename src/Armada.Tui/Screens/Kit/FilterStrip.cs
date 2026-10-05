namespace Armada.Tui.Screens.Kit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// The dashboard's filter row in the terminal: labeled fields laid out left to right, wrapping onto more rows.
    /// <c>Tab</c> moves between fields, <c>Enter</c> in a text field raises <see cref="Applied"/>, and
    /// <see cref="Changed"/> fires when a picker or toggle changes. Not thread-safe.
    /// </summary>
    public class FilterStrip : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// Entries in order. Never null.
        /// </summary>
        public IReadOnlyList<FilterStripEntry> Entries
        {
            get { return _Entries; }
        }

        /// <summary>
        /// Raised when the user presses Enter in a text filter.
        /// </summary>
        public event EventHandler? Applied;

        /// <summary>
        /// Raised when a select, multi-select, tri-state, or toggle filter changes.
        /// </summary>
        public event EventHandler? Changed;

        #endregion

        #region Private-Members

        private readonly List<FilterStripEntry> _Entries = new List<FilterStripEntry>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a labeled field.
        /// </summary>
        /// <typeparam name="T">Field type.</typeparam>
        /// <param name="label">English label.</param>
        /// <param name="field">Field.</param>
        /// <param name="width">Field width in cells.</param>
        /// <returns>The field.</returns>
        public T Add<T>(string label, T field, int width = 16) where T : IWidget
        {
            _Entries.Add(new FilterStripEntry(label, field, width));
            AddChild(field);
            if (field is TextInput input) input.Submitted += (s, e) => RaiseApplied();
            return field;
        }

        /// <summary>
        /// Wire a field's change event to <see cref="Changed"/> (call for pickers and toggles).
        /// </summary>
        public void RaiseChanged()
        {
            EventHandler? handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>
        /// Raise <see cref="Applied"/>.
        /// </summary>
        public void RaiseApplied()
        {
            EventHandler? handler = Applied;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>
        /// Rows needed at a width.
        /// </summary>
        /// <param name="width">Width.</param>
        /// <returns>Rows.</returns>
        public int HeightFor(int width)
        {
            List<Rect> rects = LayoutEntries(width).Where(r => r.Width > 0).ToList();
            return rects.Count == 0 ? 0 : rects.Max(r => r.Y) + 1;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            return Scope.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            List<Rect> rects = LayoutEntries(width);
            for (int i = 0; i < _Entries.Count; i++)
            {
                Rect r = rects[i];
                if (r.Width == 0) continue;
                if (r.Y >= height) break;
                FilterStripEntry entry = _Entries[i];
                bool focused = ReferenceEquals(Scope.Focused, entry.Field) && IsFocused;
                string label = T(entry.Label) + ":";
                int lw = SurfaceText.Draw(surface, r.X, r.Y, label, focused ? Theme.Accent : Theme.Muted, r.Width);
                int fx = r.X + lw + 1;
                int fw = Math.Max(1, Math.Min(entry.Width, r.Right - fx));
                if (fx < width) Scope.RenderChild(surface, entry.Field, new Rect(fx, r.Y, fw, 1));
            }
        }

        #endregion

        #region Private-Methods

        private List<Rect> LayoutEntries(int width)
        {
            List<Rect> rects = new List<Rect>();
            int x = 0;
            int y = 0;
            foreach (FilterStripEntry entry in _Entries)
            {
                if (entry.Field is ArmadaWidget aw && !aw.Visible)
                {
                    rects.Add(new Rect(0, 1000, 0, 0));
                    continue;
                }

                int w = Math.Min(width, TextCells.Width(T(entry.Label)) + 2 + entry.Width);
                if (x > 0 && x + w > width)
                {
                    y++;
                    x = 0;
                }

                rects.Add(new Rect(x, y, w, 1));
                x += w + 2;
            }

            return rects;
        }

        #endregion
    }
}
