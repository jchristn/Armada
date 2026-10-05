namespace Armada.Tui.Screens
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Widgets;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Base for every screen in the main region: carries the route and context, contributes screen-scoped commands
    /// (the Actions menu, palette, and help overlay), status bar hints, and an optional refresh action. Not
    /// thread-safe.
    /// </summary>
    public abstract class ScreenBase : ContainerWidget, IKeyHintSource
    {
        #region Public-Members

        /// <summary>
        /// Route this screen shows.
        /// </summary>
        public RouteMatch Route { get; }

        /// <summary>
        /// Services.
        /// </summary>
        public TuiContext Context { get; }

        /// <summary>
        /// Stable key for per-screen preferences (refresh interval) and command scope.
        /// </summary>
        public virtual string ScreenKey
        {
            get { return Route.Route.ScreenName.Length > 0 ? Route.Route.ScreenName : Route.Route.Pattern; }
        }

        /// <summary>
        /// English title.
        /// </summary>
        public virtual string Title
        {
            get { return Route.Route.Title; }
        }

        /// <summary>
        /// Status bar key hints (English labels), key label first.
        /// </summary>
        public virtual IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get { return new List<KeyValuePair<string, string>>(); }
        }

        /// <summary>
        /// Status bar hints (English labels), key label first, that still work while a text field on this screen has
        /// focus (chords such as <c>Ctrl+S</c>; never single printable keys, which would type). Default none.
        /// </summary>
        public virtual IReadOnlyList<KeyValuePair<string, string>> TypingHints
        {
            get { return new List<KeyValuePair<string, string>>(); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        protected ScreenBase(RouteMatch route, TuiContext context)
        {
            Route = route ?? throw new ArgumentNullException(nameof(route));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Scope.RegionHost = true;
            Localizer = context.Loc;
            ApplyTheme(context.Theme.Current);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Screen-scoped commands (row, bulk, and screen actions).
        /// </summary>
        /// <returns>Commands.</returns>
        public virtual IEnumerable<ArmadaCommand> Commands()
        {
            return new List<ArmadaCommand>();
        }

        /// <summary>
        /// Refresh action (F5 and auto-refresh), or null when the screen has nothing to refresh.
        /// </summary>
        /// <returns>Action or null.</returns>
        public virtual Action? RefreshAction()
        {
            return null;
        }

        /// <summary>
        /// Default auto-refresh interval in seconds until the user picks one (0 is off). Default 15.
        /// </summary>
        /// <returns>Seconds.</returns>
        public virtual int DefaultRefreshSeconds()
        {
            return Armada.Tui.Services.RefreshService.DefaultInterval;
        }

        /// <summary>
        /// This screen's status bar hints for the control that has focus on it (TUIKit's <see cref="IKeyHintSource"/>;
        /// the shell resolves them with <see cref="KeyHintResolver"/>, after the hints of the control itself). A screen
        /// that hosts another screen (a hub's content) leaves the hints to the inner one.
        /// </summary>
        /// <returns>Hints, or null while an inner screen has focus.</returns>
        public IReadOnlyList<KeyHint>? GetKeyHints()
        {
            FocusPath path = FocusPath.Build(null, this);
            for (int i = 1; i < path.Nodes.Count; i++)
            {
                if (path.Nodes[i] is ScreenBase) return null;
            }

            return ComposeHints(KeyHints.Deeper(this), KeyHints.Typing(path));
        }

        /// <summary>
        /// The screen's part of the status bar hints. <paramref name="inner"/> is the answer of the deepest
        /// <see cref="IKeyHintSource"/> below the screen (a filter row, a form), or null; the status bar shows it first
        /// and drops any key of the screen's that it already describes. The default: <see cref="TypingHints"/> when there
        /// are inner hints; a generic "Tab Next field" plus <see cref="TypingHints"/> when a bare text field has focus;
        /// otherwise <see cref="Hints"/>. Screens whose children carry no hints of their own (Ask) override this.
        /// </summary>
        /// <param name="inner">Inner hints, or null.</param>
        /// <param name="typing">True when the focused leaf takes typed text.</param>
        /// <returns>Hints. Never null.</returns>
        public virtual IReadOnlyList<KeyHint> ComposeHints(IReadOnlyList<KeyHint>? inner, bool typing)
        {
            if (inner != null) return KeyHints.Of(TypingHints);
            if (typing)
            {
                List<KeyHint> hints = new List<KeyHint> { new KeyHint("Tab", "Next field") };
                hints.AddRange(KeyHints.Of(TypingHints));
                return hints;
            }

            return KeyHints.Of(Hints);
        }

        /// <summary>
        /// Called after the screen becomes current.
        /// </summary>
        public virtual void OnActivated()
        {
        }

        /// <summary>
        /// Called before the screen is replaced.
        /// </summary>
        public virtual void OnDeactivated()
        {
        }

        #endregion
    }
}
