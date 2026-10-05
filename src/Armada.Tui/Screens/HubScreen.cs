namespace Armada.Tui.Screens
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Routing;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A hub (Missions, Delivery, Vessels, Captains, Configuration, Activity, Settings, Dispatch, Fleet Actions): a tab
    /// strip over the active tab's screen. <c>[</c>/<c>]</c> and <c>Alt+1..9</c> switch tabs from anywhere in the hub;
    /// the active tab lives in the route query so deep links and history work. Role-gated tabs are hidden.
    /// </summary>
    public class HubScreen : ScreenBase
    {
        #region Public-Members

        /// <summary>
        /// Tab strip.
        /// </summary>
        public TabStrip Tabs { get; } = new TabStrip();

        /// <summary>
        /// Active tab content.
        /// </summary>
        public ScreenBase Content { get; }

        /// <inheritdoc />
        public override string Title
        {
            get { return Route.Route.Title + (Route.Tab != null ? ": " + Route.Tab.Label : ""); }
        }

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get { return Content.Hints; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Hub route.</param>
        /// <param name="context">Context.</param>
        /// <param name="contentFactory">Builds the tab content.</param>
        /// <exception cref="ArgumentException">Thrown when the route is not a hub.</exception>
        public HubScreen(RouteMatch route, TuiContext context, Func<RouteMatch, HubTab, ScreenBase> contentFactory)
            : base(route, context)
        {
            if (route.Route.Hub == null || route.Tab == null) throw new ArgumentException("Route is not a hub.", nameof(route));
            foreach (HubTab tab in VisibleTabs()) Tabs.Add(tab.Key, tab.Label);
            Tabs.SelectKey(route.Tab.Key);
            Tabs.SelectedChanged += (s, e) => Context.Router.SelectTab(e.NewValue);
            AddChild(Tabs);
            Content = AddChild(contentFactory(route, route.Tab));
            Scope.Focus(Content);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Tabs the signed-in user may see.
        /// </summary>
        /// <returns>Tabs.</returns>
        public IReadOnlyList<HubTab> VisibleTabs()
        {
            return Route.Route.Hub!.Tabs.Where(t => (!t.GlobalAdminOnly || Context.Session.IsGlobalAdmin) && (!t.TenantAdminOnly || Context.Session.IsTenantAdmin)).ToList();
        }

        /// <inheritdoc />
        public override IEnumerable<Armada.Tui.Input.ArmadaCommand> Commands()
        {
            return Content.Commands();
        }

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return Content.RefreshAction();
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (Scope.HandleKey(key)) return true;
            return Tabs.HandleGlobalKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            Scope.RenderChild(surface, Tabs, new Rect(0, 0, width, 1));
            if (height > 2) Scope.RenderChild(surface, Content, new Rect(0, 2, width, height - 2));
        }

        #endregion
    }
}
