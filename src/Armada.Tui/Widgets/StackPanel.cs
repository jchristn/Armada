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

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate. The children are separate focus regions, each in its own box (see <see cref="RegionFrames"/>).
        /// </summary>
        public StackPanel()
        {
            Scope.RegionHost = true;
        }

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
            // Each child that is a focus region gets its own box (the panel is a region host), so measure the fixed
            // rows, titles, and box lines first, then share what is left among the fill children by weight.
            RegionStack measure = new RegionStack(width, height);
            Arrange(measure, visible, width, new Dictionary<StackItem, int>(), null);
            int remaining = Math.Max(0, height - measure.Y);
            int weights = visible.Where(i => !i.Height.HasValue).Sum(i => i.Weight);
            int fillCount = visible.Count(i => !i.Height.HasValue);
            Dictionary<StackItem, int> fills = new Dictionary<StackItem, int>();
            int fillUsed = 0;
            int fillSeen = 0;
            foreach (StackItem item in visible.Where(i => !i.Height.HasValue))
            {
                fillSeen++;
                int h = fillSeen == fillCount ? remaining - fillUsed : (weights > 0 ? remaining * item.Weight / weights : 0);
                fills[item] = h;
                fillUsed += h;
            }

            Arrange(new RegionStack(width, height), visible, width, fills, surface);
        }

        #endregion

        #region Private-Methods

        private void Arrange(RegionStack stack, List<StackItem> visible, int width, Dictionary<StackItem, int> fills, ISurface? surface)
        {
            foreach (StackItem item in visible)
            {
                if (stack.Y >= stack.Height) break;
                if (item.Title != null)
                {
                    int ty = stack.Content(1);
                    if (surface != null && ty < stack.Height) SurfaceText.Draw(surface, 0, ty, T(item.Title), Theme.Accent, width);
                }

                int h = item.Height.HasValue ? item.Height.Value : (fills.TryGetValue(item, out int f) ? f : 0);
                Rect r = stack.Place(item.Child, Math.Max(0, h));
                if (surface != null && r.Height > 0) Scope.RenderChild(surface, item.Child, r);
            }
        }

        #endregion
    }
}
