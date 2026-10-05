namespace Armada.Tui.Screens.Kit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Routing;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// A screen laid out as a vertical stack: fixed-height parts (header, banner, KPIs, chart, filters) above one part
    /// that fills the rest (usually a grid). A one-line banner under the first part shows errors and notices.
    /// <c>Tab</c> moves between focusable parts. Not thread-safe.
    /// </summary>
    public abstract class StackScreen : ScreenBase
    {
        #region Public-Members

        /// <summary>
        /// Banner text (already translated) under the header, or empty.
        /// </summary>
        public string Banner { get; set; } = "";

        /// <summary>
        /// Banner style, or null for the warning style.
        /// </summary>
        public Func<ArmadaTheme, CellStyle>? BannerStyle { get; set; } = null;

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        #endregion

        #region Private-Members

        private const int MinimumFill = 4;
        private readonly HashSet<IWidget> _Dropped = new HashSet<IWidget>(ReferenceEqualityComparer.Instance);

        private readonly List<IWidget> _Parts = new List<IWidget>();
        private readonly Dictionary<IWidget, Func<int, int>> _Heights = new Dictionary<IWidget, Func<int, int>>();
        private IWidget? _Fill = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        protected StackScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (Scope.HandleKey(key)) return true;
            return false;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 4 || height < 2) return;
            BeforeRender(width, height);
            bool banner = !String.IsNullOrEmpty(Banner);
            // Each focus region gets a box line above and below it (see RegionStack); measure the fixed parts and the
            // lines first, then give the fill part what is left.
            // When the fill part (the grid) would get fewer than MinimumFill rows, plain content parts (charts, KPI
            // rows) are left out, tallest first, so the focus regions stay on screen (80x24).
            _Dropped.Clear();
            int fill = Measure(banner, width, height);
            while (_Fill != null && fill < MinimumFill)
            {
                IWidget? drop = _Parts
                    .Where(p => !ReferenceEquals(p, _Fill) && !_Dropped.Contains(p) && !RegionStack.IsRegion(p) && !IsHidden(p) && _Heights[p](width) > 0)
                    .OrderByDescending(p => _Heights[p](width))
                    .FirstOrDefault();
                if (drop == null) break;
                _Dropped.Add(drop);
                fill = Measure(banner, width, height);
            }

            Arrange(new RegionStack(width, height), banner, width, fill, surface);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Add a part with a height computed from the width.
        /// </summary>
        /// <typeparam name="T">Widget type.</typeparam>
        /// <param name="widget">Widget.</param>
        /// <param name="height">Height for a width.</param>
        /// <returns>The widget.</returns>
        protected T AddFixed<T>(T widget, Func<int, int> height) where T : IWidget
        {
            _Parts.Add(widget);
            _Heights[widget] = height ?? (w => 1);
            return AddChild(widget);
        }

        /// <summary>
        /// Add the part that fills the remaining height (one per screen).
        /// </summary>
        /// <typeparam name="T">Widget type.</typeparam>
        /// <param name="widget">Widget.</param>
        /// <returns>The widget.</returns>
        protected T AddFill<T>(T widget) where T : IWidget
        {
            _Parts.Add(widget);
            _Fill = widget;
            return AddChild(widget);
        }

        /// <summary>
        /// Called before layout on every frame (update button visibility, labels, status text).
        /// </summary>
        /// <param name="width">Width.</param>
        /// <param name="height">Height.</param>
        protected virtual void BeforeRender(int width, int height)
        {
        }

        #endregion

        #region Private-Methods

        private int Measure(bool banner, int width, int height)
        {
            RegionStack measure = new RegionStack(width, height);
            Arrange(measure, banner, width, 1, null);
            return Math.Max(1, 1 + height - measure.Y);
        }

        private void Arrange(RegionStack stack, bool banner, int width, int fill, ISurface? surface)
        {
            bool bannerDrawn = false;
            foreach (IWidget part in _Parts)
            {
                int h = ReferenceEquals(part, _Fill) ? fill : (IsHidden(part) || _Dropped.Contains(part) ? 0 : Math.Max(0, _Heights[part](width)));
                if (h > 0 && stack.Y < stack.Height)
                {
                    Rect r = stack.Place(part, h);
                    if (surface != null && !r.IsEmpty) Scope.RenderChild(surface, part, r);
                }

                if (banner && !bannerDrawn)
                {
                    bannerDrawn = true;
                    int y = stack.Content(1);
                    if (surface != null && y < stack.Height) SurfaceText.Draw(surface, 0, y, Banner, BannerStyle != null ? BannerStyle(Theme) : Theme.Warning, width);
                }
            }
        }

        private static bool IsHidden(IWidget widget)
        {
            return widget is ArmadaWidget aw && !aw.Visible;
        }

        #endregion
    }
}
