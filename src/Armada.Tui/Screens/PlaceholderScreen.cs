namespace Armada.Tui.Screens
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Routing;
    using Armada.Tui.Text;
    using TUIKit;

    /// <summary>
    /// Stand-in for screens later waves build: names the route, the TUI screen, and the workstream that delivers it,
    /// so every dashboard route already resolves and can be navigated to (and deep links keep their parameters).
    /// </summary>
    public class PlaceholderScreen : ScreenBase
    {
        #region Public-Members

        /// <summary>
        /// Screen name shown (for hub tabs, the tab's screen).
        /// </summary>
        public string ScreenName { get; }

        /// <summary>
        /// Workstream shown.
        /// </summary>
        public string Workstream { get; }

        /// <summary>
        /// English title shown.
        /// </summary>
        public string DisplayTitle { get; }

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a route.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        /// <param name="screenName">Screen name, or null for the route's.</param>
        /// <param name="workstream">Workstream, or null for the route's.</param>
        /// <param name="title">English title, or null for the route's.</param>
        public PlaceholderScreen(RouteMatch route, TuiContext context, string? screenName = null, string? workstream = null, string? title = null)
            : base(route, context)
        {
            ScreenName = screenName ?? route.Route.ScreenName;
            Workstream = workstream ?? route.Route.Workstream;
            DisplayTitle = title ?? route.Route.Title;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 10 || height < 3) return;
            List<string> lines = new List<string>();
            int y = Math.Max(0, height / 2 - 4);
            Line(surface, ref y, T(DisplayTitle), Theme.Accent, width);
            y++;
            Line(surface, ref y, T("Coming in a later milestone"), Theme.Text, width);
            Line(surface, ref y, T("Route") + ": " + Route.Route.Pattern + "   (" + Route.FullPath + ")", Theme.Muted, width);
            Line(surface, ref y, T("Screen") + ": " + ScreenName + "   " + T("Workstream") + ": " + Workstream, Theme.Muted, width);
            if (Route.Parameters.Count > 0)
            {
                Line(surface, ref y, String.Join("  ", Route.Parameters.Select(p => p.Key + "=" + p.Value)), Theme.Code, width);
            }

            y++;
            Line(surface, ref y, T("Use the sidebar, the Go menu (F10), or the command palette (Ctrl+K) to move around."), Theme.Muted, width);
        }

        #endregion

        #region Private-Methods

        private static void Line(ISurface surface, ref int y, string text, CellStyle style, int width)
        {
            if (y >= surface.Size.Height) return;
            SurfaceText.Draw(surface, Math.Max(0, (width - TextCells.Width(text)) / 2), y, text, style, width);
            y++;
        }

        #endregion
    }
}
