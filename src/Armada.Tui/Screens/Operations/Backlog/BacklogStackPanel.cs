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
    /// A vertical stack of child widgets for the backlog item's refinement panels: fixed-height rows and one flexible
    /// row, <c>Tab</c> between focusable children, and an optional key hook that runs before the focused child (used
    /// for <c>Ctrl+S</c> send in the transcript). Not thread-safe.
    /// </summary>
    public class BacklogStackPanel : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// Runs before the focused child; return true to consume the key.
        /// </summary>
        public Func<KeyEvent, bool>? KeyHook { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly List<IWidget> _Rows = new List<IWidget>();
        private readonly List<Func<int>> _Heights = new List<Func<int>>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate. Each row is its own focus region (see <see cref="RegionFrames"/>).
        /// </summary>
        public BacklogStackPanel()
        {
            Scope.RegionHost = true;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a row.
        /// </summary>
        /// <typeparam name="TWidget">Widget type.</typeparam>
        /// <param name="widget">Widget.</param>
        /// <param name="height">Height function; 0 or less makes the row flexible.</param>
        /// <returns>The widget.</returns>
        public TWidget AddRow<TWidget>(TWidget widget, Func<int> height) where TWidget : IWidget
        {
            _Rows.Add(widget);
            _Heights.Add(height ?? (() => 1));
            AddChild(widget);
            return widget;
        }

        /// <summary>
        /// Focus a child.
        /// </summary>
        /// <param name="widget">Child.</param>
        /// <returns>True when focused.</returns>
        public bool FocusRow(IWidget widget)
        {
            return Scope.Focus(widget);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (KeyHook != null && KeyHook(key)) return true;
            return Scope.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            // Each row that is a focus region gets its own box (the panel is a region host): measure the fixed rows and
            // the box lines with the flexible row empty, then give it what is left.
            int flex = -1;
            for (int i = 0; i < _Rows.Count; i++)
            {
                bool visible = !(_Rows[i] is ArmadaWidget aw) || aw.Visible;
                if (visible && _Heights[i]() <= 0)
                {
                    flex = i;
                    break;
                }
            }

            RegionStack measure = new RegionStack(width, height);
            Arrange(measure, width, flex, 0, null);
            Arrange(new RegionStack(width, height), width, flex, Math.Max(1, height - measure.Y), surface);
        }

        #endregion

        #region Private-Methods

        private void Arrange(RegionStack stack, int width, int flex, int flexHeight, ISurface? surface)
        {
            for (int i = 0; i < _Rows.Count; i++)
            {
                bool visible = !(_Rows[i] is ArmadaWidget aw) || aw.Visible;
                int h = !visible ? 0 : i == flex ? flexHeight : Math.Max(0, _Heights[i]());
                if (i != flex && h <= 0) continue;
                if (stack.Y >= stack.Height) break;
                Rect r = stack.Place(_Rows[i], h);
                if (surface != null && r.Height > 0) Scope.RenderChild(surface, _Rows[i], r);
            }
        }

        #endregion
    }
}
