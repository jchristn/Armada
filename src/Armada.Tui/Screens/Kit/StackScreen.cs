namespace Armada.Tui.Screens.Kit
{
    using System;
    using System.Collections.Generic;
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
            Dictionary<IWidget, int> sizes = new Dictionary<IWidget, int>();
            int fixedTotal = 0;
            foreach (IWidget part in _Parts)
            {
                if (ReferenceEquals(part, _Fill)) continue;
                int h = IsHidden(part) ? 0 : Math.Max(0, _Heights[part](width));
                sizes[part] = h;
                fixedTotal += h;
            }

            bool banner = !String.IsNullOrEmpty(Banner);
            int fill = Math.Max(1, height - fixedTotal - (banner ? 1 : 0));
            int y = 0;
            bool bannerDrawn = false;
            foreach (IWidget part in _Parts)
            {
                int h = ReferenceEquals(part, _Fill) ? fill : sizes[part];
                if (h > 0 && y < height)
                {
                    Scope.RenderChild(surface, part, new Rect(0, y, width, Math.Min(h, height - y)));
                    y += h;
                }

                if (banner && !bannerDrawn)
                {
                    bannerDrawn = true;
                    if (y < height) SurfaceText.Draw(surface, 0, y, Banner, BannerStyle != null ? BannerStyle(Theme) : Theme.Warning, width);
                    y++;
                }
            }
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

        private static bool IsHidden(IWidget widget)
        {
            return widget is ArmadaWidget aw && !aw.Visible;
        }

        #endregion
    }
}
