namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Widgets;

    /// <summary>
    /// Finds the focus regions of a pane so <see cref="FocusFrame.DrawNested"/> can box each one. A focus region is
    /// every Tab stop placed in a <see cref="FocusScope.RegionHost"/> scope (every screen sets it; hubs, split panels,
    /// and wizards nest further hosts), so a new screen gets boxes for its panes without any code of its own: its
    /// layout only has to leave the cell around each region free (see <see cref="RegionStack"/>), which the focus
    /// sweep test checks on every route. The region on the focus path is the focused one. Stateless and thread-safe.
    /// </summary>
    public static class RegionFrames
    {
        #region Public-Methods

        /// <summary>
        /// The focus regions below a host, in shell coordinates.
        /// </summary>
        /// <param name="host">The pane's widget (the current screen).</param>
        /// <param name="hostRect">Where the host was drawn, in shell coordinates.</param>
        /// <param name="focusInHost">True when keyboard focus is in the host.</param>
        /// <returns>Regions in focus order. Never null.</returns>
        public static List<RegionFrame> Collect(IFocusScopeOwner host, Rect hostRect, bool focusInHost)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            List<RegionFrame> result = new List<RegionFrame>();
            if (!host.Scope.RegionHost) return result;
            List<RegionFrame> overlays = new List<RegionFrame>();
            Walk(host, hostRect.X, hostRect.Y, focusInHost, result, overlays, 0);
            // An open overlay (a drawer) holds focus over everything below it: it is the only box.
            if (overlays.Count > 0) return new List<RegionFrame> { overlays[overlays.Count - 1] };
            return result;
        }

        /// <summary>
        /// The focused region, or null.
        /// </summary>
        /// <param name="regions">Regions.</param>
        /// <returns>Region or null.</returns>
        public static RegionFrame? FocusedOf(IReadOnlyList<RegionFrame> regions)
        {
            if (regions == null) return null;
            foreach (RegionFrame r in regions)
            {
                if (r.Focused) return r;
            }

            return null;
        }

        #endregion

        #region Private-Methods

        private static void Walk(IFocusScopeOwner host, int ox, int oy, bool onFocusPath, List<RegionFrame> result, List<RegionFrame> overlays, int depth)
        {
            if (depth > 16) return;
            if (host is IRegionOverlayHost overlayHost && overlayHost.RegionOverlay != null && !overlayHost.RegionOverlayRect.IsEmpty)
            {
                overlays.Add(new RegionFrame(overlayHost.RegionOverlay, overlayHost.RegionOverlayRect.Offset(ox, oy), onFocusPath));
                return;
            }

            FocusScope scope = host.Scope;
            IWidget? focused = scope.Focused;
            foreach (IWidget child in scope.Children)
            {
                Rect placed = scope.RectOf(child);
                if (placed.IsEmpty) continue;
                Rect abs = placed.Offset(ox, oy);
                bool childOnPath = onFocusPath && ReferenceEquals(child, focused);
                if (child is IFocusScopeOwner owner && owner.Scope.RegionHost && !ReferenceEquals(owner.Scope, scope))
                {
                    int before = result.Count;
                    Walk(owner, abs.X, abs.Y, childOnPath, result, overlays, depth + 1);
                    // A nested host that drew none of its regions (a panel showing "Loading..." in place of its
                    // fields) is boxed as one region, so focus inside it still shows.
                    if (result.Count > before || !FocusScope.IsFocusStop(child)) continue;
                }
                else if (!FocusScope.IsFocusStop(child))
                {
                    continue;
                }

                result.Add(new RegionFrame(child, abs, childOnPath));
            }
        }

        #endregion
    }
}
