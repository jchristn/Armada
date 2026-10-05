namespace Armada.Tui.Screens.Operations
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
    /// The dashboard's filter row as one line (or a few, when narrow) of labeled fields: text filters, selects, and
    /// toggles laid out left to right and wrapped. <c>/</c> on a list focuses it, <c>Tab</c> moves between fields, and
    /// <c>Esc</c> or <c>Enter</c> in a text field returns to the list (<see cref="Exited"/>). Not thread-safe.
    /// </summary>
    public class OpsFilterBar : ContainerWidget, IFocusHintSource
    {
        #region Public-Members

        /// <summary>
        /// True when the bar can take focus: never while it has no filters (it is not drawn then).
        /// </summary>
        public override bool CanFocus
        {
            get { return _CanFocus && !IsEmpty; }
            set { _CanFocus = value; }
        }

        /// <summary>
        /// English status bar description of where <c>Esc</c> takes focus. Default "Back to the list".
        /// </summary>
        public string ExitLabel { get; set; } = "Back to the list";

        /// <summary>
        /// Raised when the user leaves the bar (Esc, or Enter in a text field).
        /// </summary>
        public event EventHandler? Exited;

        /// <summary>
        /// True when the bar has no fields.
        /// </summary>
        public bool IsEmpty
        {
            get { return _Items.Count == 0; }
        }

        #endregion

        #region Private-Members

        private bool _CanFocus = true;

        private readonly List<OpsFilterItem> _Items = new List<OpsFilterItem>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a labeled field.
        /// </summary>
        /// <typeparam name="TWidget">Widget type.</typeparam>
        /// <param name="label">English label.</param>
        /// <param name="widget">Field widget.</param>
        /// <param name="width">Field width in cells.</param>
        /// <returns>The widget.</returns>
        public TWidget Add<TWidget>(string label, TWidget widget, int width) where TWidget : IWidget
        {
            _Items.Add(new OpsFilterItem(label, widget, Math.Clamp(width, 4, 80)));
            AddChild(widget);
            if (widget is TextInput input)
            {
                input.Submitted += (s, e) => Exited?.Invoke(this, EventArgs.Empty);
            }

            return widget;
        }

        /// <summary>
        /// Rows needed at a width.
        /// </summary>
        /// <param name="width">Width.</param>
        /// <returns>Rows (0 when empty).</returns>
        public int PreferredHeight(int width)
        {
            if (_Items.Count == 0) return 0;
            return Layout(Math.Max(10, width)).Select(r => r.Y).DefaultIfEmpty(0).Max() + 1;
        }

        /// <inheritdoc />
        public FocusHints? GetFocusHints()
        {
            return FilterRowHints.For(Scope.Focused, ExitLabel);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape)
            {
                Exited?.Invoke(this, EventArgs.Empty);
                return true;
            }

            return Scope.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            List<Rect> rects = Layout(width);
            for (int i = 0; i < _Items.Count; i++)
            {
                OpsFilterItem item = _Items[i];
                Rect r = rects[i];
                if (r.Y >= height) break;
                string label = T(item.Label) + ":";
                bool focused = IsFocused && ReferenceEquals(Scope.Focused, item.Widget);
                // The focused field's label is bold and underlined as well as colored, so focus does not rely on color.
                CellStyle labelStyle = focused ? Theme.Accent.WithAttribute(CellAttributes.Bold, true).WithAttribute(CellAttributes.Underline, true) : Theme.Muted;
                int lw = SurfaceText.Draw(surface, r.X, r.Y, label, labelStyle, width - r.X);
                Scope.RenderChild(surface, item.Widget, new Rect(r.X + lw + 1, r.Y, Math.Max(1, Math.Min(item.Width, width - r.X - lw - 1)), 1));
            }
        }

        #endregion

        #region Private-Methods

        private List<Rect> Layout(int width)
        {
            List<Rect> rects = new List<Rect>();
            int x = 0;
            int y = 0;
            foreach (OpsFilterItem item in _Items)
            {
                int need = TextCells.Width(T(item.Label)) + 2 + item.Width;
                if (x > 0 && x + need > width)
                {
                    x = 0;
                    y++;
                }

                rects.Add(new Rect(x, y, Math.Min(need, width), 1));
                x += need + 2;
            }

            return rects;
        }

        #endregion
    }
}
