namespace Armada.Tui.Widgets
{
    using TUIKit;
    using TUIKit.Widgets;

    /// <summary>
    /// A region host that can open an overlay over its own regions (a detail drawer) which then holds keyboard focus.
    /// While the overlay is open <see cref="RegionFrames"/> boxes only the overlay, drawn focused, so no box of the
    /// regions underneath cuts through it.
    /// </summary>
    public interface IRegionOverlayHost
    {
        /// <summary>
        /// The open overlay, or null when none is open.
        /// </summary>
        IWidget? RegionOverlay { get; }

        /// <summary>
        /// The overlay's content rectangle in the host's coordinates as of the last render (its box is one cell
        /// larger on every side).
        /// </summary>
        Rect RegionOverlayRect { get; }
    }
}
