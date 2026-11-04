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
    /// the active tab lives in the route query so deep links and history work. Role-gated tabs are hidden. A hub tab can
    /// host a nested section strip (for example Settings, CLI Tool Permissions with Requests and Rules under the
    /// <c>section</c> query parameter); a nested strip switches with Left/Right when focused and leaves <c>[</c>/<c>]</c>
    /// to the outer hub.
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

        /// <summary>
        /// The hub whose tabs this screen shows (the route's hub, or the section hub of a nested strip).
        /// </summary>
        public HubDefinition Hub { get; }

        /// <summary>
        /// The selected tab of <see cref="Hub"/>.
        /// </summary>
        public HubTab SelectedTab { get; }

        /// <summary>
        /// True for a nested section strip inside a hub tab.
        /// </summary>
        public bool IsNested { get; }

        /// <inheritdoc />
        public override string Title
        {
            get
            {
                if (IsNested) return SelectedTab.Label;
                // "Missions: Missions" says nothing twice; the hub's own tab is named once.
                if (Route.Tab == null || String.Equals(Route.Tab.Label, Route.Route.Title, StringComparison.Ordinal)) return Route.Route.Title;
                return Route.Route.Title + ": " + Route.Tab.Label;
            }
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
            Hub = route.Route.Hub;
            SelectedTab = route.Tab;
            foreach (HubTab tab in VisibleTabs()) Tabs.Add(tab.Key, tab.Label);
            Tabs.SelectKey(route.Tab.Key);
            Tabs.SelectedChanged += (s, e) =>
            {
                // The switch came from the tab strip: keep focus there on the rebuilt hub so the next Left/Right
                // switches tabs again instead of going to the new tab's content.
                Context.KeepHubTabFocus = Tabs.IsFocused;
                Context.Router.SelectTab(e.NewValue);
            };
            AddChild(Tabs);
            Content = AddChild(contentFactory(route, route.Tab));
            if (Context.KeepHubTabFocus) Scope.Focus(Tabs);
            else Scope.Focus(Content);
            Context.KeepHubTabFocus = false;
        }

        /// <summary>
        /// Instantiate a nested section strip inside a hub tab. The selected section comes from the route query
        /// parameter named by <paramref name="sections"/> (its first visible section when absent or unknown).
        /// </summary>
        /// <param name="route">Route of the enclosing hub.</param>
        /// <param name="context">Context.</param>
        /// <param name="sections">Section hub (query parameter and sections).</param>
        /// <param name="contentFactory">Builds the section content.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="sections"/> or <paramref name="contentFactory"/> is null.</exception>
        public HubScreen(RouteMatch route, TuiContext context, HubDefinition sections, Func<RouteMatch, HubTab, ScreenBase> contentFactory)
            : base(route, context)
        {
            Hub = sections ?? throw new ArgumentNullException(nameof(sections));
            if (contentFactory == null) throw new ArgumentNullException(nameof(contentFactory));
            IsNested = true;
            IReadOnlyList<HubTab> visible = VisibleTabs();
            route.Query.TryGetValue(sections.QueryParam, out string? key);
            HubTab? selected = visible.FirstOrDefault(t => String.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
            SelectedTab = selected ?? (visible.Count > 0 ? visible[0] : sections.Tabs[0]);
            foreach (HubTab tab in visible) Tabs.Add(tab.Key, tab.Label);
            Tabs.SelectKey(SelectedTab.Key);
            Tabs.SelectedChanged += (s, e) =>
            {
                Context.KeepSectionTabFocus = Tabs.IsFocused;
                RouteMatch? current = Context.Router.Current;
                if (current == null) return;
                Dictionary<string, string> query = new Dictionary<string, string>(current.Query, StringComparer.Ordinal);
                query[sections.QueryParam] = e.NewValue;
                Context.Router.Navigate(current.Path + RouteMatch.BuildQuery(query), true);
            };
            AddChild(Tabs);
            Content = AddChild(contentFactory(route, SelectedTab));
            if (Context.KeepSectionTabFocus) Scope.Focus(Tabs);
            else Scope.Focus(Content);
            Context.KeepSectionTabFocus = false;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Tabs the signed-in user may see.
        /// </summary>
        /// <returns>Tabs.</returns>
        public IReadOnlyList<HubTab> VisibleTabs()
        {
            return Hub.Tabs.Where(t => (!t.GlobalAdminOnly || Context.Session.IsGlobalAdmin) && (!t.TenantAdminOnly || Context.Session.IsTenantAdmin)).ToList();
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
        public override void OnActivated()
        {
            Content.OnActivated();
        }

        /// <inheritdoc />
        public override void OnDeactivated()
        {
            Content.OnDeactivated();
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (Scope.HandleKey(key)) return true;
            return !IsNested && Tabs.HandleGlobalKey(key);
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
