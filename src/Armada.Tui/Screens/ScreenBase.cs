namespace Armada.Tui.Screens
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Base for every screen in the main region: carries the route and context, contributes screen-scoped commands
    /// (the Actions menu, palette, and help overlay), status bar hints, and an optional refresh action. Not
    /// thread-safe.
    /// </summary>
    public abstract class ScreenBase : ContainerWidget
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
        /// Status bar hints for the control that has focus on this screen. <paramref name="inner"/> is the answer of
        /// the deepest <see cref="IFocusHintSource"/> on the focus path below the screen (a filter row, a form), or
        /// null. The default: the inner hints plus <see cref="TypingHints"/> when there are inner hints; a generic
        /// "Tab Next field" plus <see cref="TypingHints"/> when a bare text field has focus; otherwise
        /// <see cref="Hints"/>. Screens whose children carry no hints of their own (Ask) override this.
        /// </summary>
        /// <param name="inner">Inner hints, or null.</param>
        /// <param name="textEntry">True when the focused leaf takes typed text.</param>
        /// <returns>Hints. Never null.</returns>
        public virtual FocusHints ResolveHints(FocusHints? inner, bool textEntry)
        {
            if (inner != null)
            {
                FocusHints result = new FocusHints(inner.TextEntry || textEntry);
                result.AddRange(inner.Keys);
                AddTypingHints(result);
                return result;
            }

            if (textEntry) return AddTypingHints(FocusHints.Typing("Tab", "Next field"));
            return FocusHints.Of(Hints);
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

        #region Private-Methods

        private FocusHints AddTypingHints(FocusHints hints)
        {
            // The screen's own chords follow the control's keys; a key the control already describes (Ctrl+S in a form)
            // is not repeated.
            foreach (KeyValuePair<string, string> hint in TypingHints)
            {
                if (!hints.Keys.Any(k => k.Key == hint.Key)) hints.Keys.Add(hint);
            }

            return hints;
        }

        #endregion
    }
}
