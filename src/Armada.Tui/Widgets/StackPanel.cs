namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// A vertical stack for detail panels that combine widgets (a summary over a chart, a form over a grid): each
    /// child has a fixed height or fills a share of the remaining space, optional titles are drawn above children, and
    /// <c>Tab</c> moves focus between focusable children. Hidden children take no space. Not thread-safe.
    /// </summary>
    public class StackPanel : ContainerWidget
    {
        #region Private-Members

        private readonly List<StackItem> _Items = new List<StackItem>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a child.
        /// </summary>
        /// <typeparam name="TWidget">Child type.</typeparam>
        /// <param name="child">Child.</param>
        /// <param name="height">Fixed height, or null to fill.</param>
        /// <param name="title">English title drawn above the child, or null.</param>
        /// <param name="weight">Share of the remaining space for fill children (1..10).</param>
        /// <returns>The child.</returns>
        public TWidget Add<TWidget>(TWidget child, int? height = null, string? title = null, int weight = 1) where TWidget : IWidget
        {
            _Items.Add(new StackItem(child, height, title, Math.Clamp(weight, 1, 10)));
            AddChild(child);
            return child;
        }

        /// <summary>
        /// Change a child's fixed height.
        /// </summary>
        /// <param name="child">Child.</param>
        /// <param name="height">Height, or null to fill.</param>
        public void SetHeight(IWidget child, int? height)
        {
            StackItem? item = _Items.FirstOrDefault(i => ReferenceEquals(i.Child, child));
            if (item != null) item.Height = height;
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
            List<StackItem> visible = _Items.Where(i => !(i.Child is ArmadaWidget aw) || aw.Visible).ToList();
            int fixedTotal = visible.Where(i => i.Height.HasValue).Sum(i => i.Height!.Value + (i.Title != null ? 1 : 0));
            int fillTitles = visible.Where(i => !i.Height.HasValue && i.Title != null).Count();
            int weights = visible.Where(i => !i.Height.HasValue).Sum(i => i.Weight);
            int remaining = Math.Max(0, height - fixedTotal - fillTitles);
            int y = 0;
            int fillUsed = 0;
            int fillSeen = 0;
            int fillCount = visible.Count(i => !i.Height.HasValue);
            foreach (StackItem item in visible)
            {
                if (y >= height) break;
                if (item.Title != null)
                {
                    SurfaceText.Draw(surface, 0, y, T(item.Title), Theme.Accent, width);
                    y++;
                }

                int h;
                if (item.Height.HasValue) h = item.Height.Value;
                else
                {
                    fillSeen++;
                    h = fillSeen == fillCount ? remaining - fillUsed : (weights > 0 ? remaining * item.Weight / weights : 0);
                    fillUsed += h;
                }

                h = Math.Max(0, Math.Min(h, height - y));
                if (h > 0) Scope.RenderChild(surface, item.Child, new Rect(0, y, width, h));
                y += h;
            }
        }

        #endregion
    }
}
