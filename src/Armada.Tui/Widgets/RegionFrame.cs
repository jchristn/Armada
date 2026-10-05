namespace Armada.Tui.Widgets
{
    using TUIKit;
    using TUIKit.Widgets;

    /// <summary>
    /// One focus region of a screen as drawn in the last frame (see <see cref="RegionFrames"/>): the widget, where its
    /// content went, the box around it, and whether it holds keyboard focus. Immutable.
    /// </summary>
    public sealed class RegionFrame
    {
        #region Public-Members

        /// <summary>
        /// The region's widget (a Tab stop of the screen or of a nested region host).
        /// </summary>
        public IWidget Widget { get; }

        /// <summary>
        /// Where the widget was drawn, in shell (terminal) coordinates.
        /// </summary>
        public Rect Region { get; }

        /// <summary>
        /// The box around the region (one cell larger on every side), in shell coordinates.
        /// </summary>
        public Rect Box { get; }

        /// <summary>
        /// True when keyboard focus is in this region.
        /// </summary>
        public bool Focused { get; }

        /// <summary>
        /// Text drawn on the box's top line, or null (see <see cref="ArmadaWidget.BoxTitle"/>).
        /// </summary>
        public string? Title
        {
            get { return Widget is ArmadaWidget aw && !System.String.IsNullOrEmpty(aw.BoxTitle) ? aw.BoxTitle : null; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="widget">Widget.</param>
        /// <param name="region">Region in shell coordinates.</param>
        /// <param name="focused">True when focus is in the region.</param>
        public RegionFrame(IWidget widget, Rect region, bool focused)
        {
            Widget = widget ?? throw new System.ArgumentNullException(nameof(widget));
            Region = region;
            Box = FocusFrame.Outer(region);
            Focused = focused;
        }

        #endregion
    }
}
