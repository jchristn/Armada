namespace Armada.Tui
{
    using System;
    using Armada.Client;
    using Armada.Tui.Input;
    using Armada.Tui.Modals;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Services.Credentials;
    using Armada.Tui.Theming;
    using TUIKit.Hosting;

    /// <summary>
    /// The services every screen and shell part uses, created once by <see cref="ArmadaTuiApp"/>. Members are set
    /// during composition and not replaced afterwards (the session's client is replaced through
    /// <see cref="SessionService.SwitchProfile"/>). Use on the UI loop thread unless a member says otherwise.
    /// </summary>
    public class TuiContext
    {
        #region Public-Members

        /// <summary>
        /// TUIKit application.
        /// </summary>
        public TuiApplication App { get; }

        /// <summary>
        /// UI dispatcher (thread-safe).
        /// </summary>
        public IUiDispatcher Dispatcher { get; }

        /// <summary>
        /// Clock.
        /// </summary>
        public IClock Clock { get; }

        /// <summary>
        /// Preferences.
        /// </summary>
        public PreferencesService Prefs { get; }

        /// <summary>
        /// Theme.
        /// </summary>
        public ThemeService Theme { get; }

        /// <summary>
        /// Localization.
        /// </summary>
        public LocalizationService Loc { get; }

        /// <summary>
        /// Command registry.
        /// </summary>
        public CommandService Commands { get; }

        /// <summary>
        /// Router.
        /// </summary>
        public Router Router { get; }

        /// <summary>
        /// Session.
        /// </summary>
        public SessionService Session { get; }

        /// <summary>
        /// Socket event pump.
        /// </summary>
        public EventPump Events { get; }

        /// <summary>
        /// Auto-refresh.
        /// </summary>
        public RefreshService Refresh { get; }

        /// <summary>
        /// Notifications and toasts.
        /// </summary>
        public NotificationService Notifications { get; }

        /// <summary>
        /// Approvals queue.
        /// </summary>
        public ApprovalService Approvals { get; }

        /// <summary>
        /// Clipboard.
        /// </summary>
        public ClipboardService Clipboard { get; }

        /// <summary>
        /// External programs, URLs, and files.
        /// </summary>
        public ExternalService External { get; }

        /// <summary>
        /// Modal host.
        /// </summary>
        public IModalHost Modals { get; }

        /// <summary>
        /// Header status polling.
        /// </summary>
        public StatusPoller Status { get; }

        /// <summary>
        /// Terminal output.
        /// </summary>
        public ITerminalOutput Terminal { get; }

        /// <summary>
        /// Credential store.
        /// </summary>
        public ICredentialStore Credentials { get; }

        /// <summary>
        /// The active client (shortcut for <c>Session.Client</c>).
        /// </summary>
        public ArmadaClient Client
        {
            get { return Session.Client; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="app">Application.</param>
        /// <param name="dispatcher">Dispatcher.</param>
        /// <param name="clock">Clock.</param>
        /// <param name="prefs">Preferences.</param>
        /// <param name="theme">Theme.</param>
        /// <param name="loc">Localization.</param>
        /// <param name="commands">Commands.</param>
        /// <param name="router">Router.</param>
        /// <param name="session">Session.</param>
        /// <param name="events">Event pump.</param>
        /// <param name="refresh">Refresh.</param>
        /// <param name="notifications">Notifications.</param>
        /// <param name="approvals">Approvals.</param>
        /// <param name="clipboard">Clipboard.</param>
        /// <param name="external">External.</param>
        /// <param name="modals">Modals.</param>
        /// <param name="status">Status poller.</param>
        /// <param name="terminal">Terminal output.</param>
        /// <param name="credentials">Credentials.</param>
        public TuiContext(
            TuiApplication app,
            IUiDispatcher dispatcher,
            IClock clock,
            PreferencesService prefs,
            ThemeService theme,
            LocalizationService loc,
            CommandService commands,
            Router router,
            SessionService session,
            EventPump events,
            RefreshService refresh,
            NotificationService notifications,
            ApprovalService approvals,
            ClipboardService clipboard,
            ExternalService external,
            IModalHost modals,
            StatusPoller status,
            ITerminalOutput terminal,
            ICredentialStore credentials)
        {
            App = app ?? throw new ArgumentNullException(nameof(app));
            Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Prefs = prefs ?? throw new ArgumentNullException(nameof(prefs));
            Theme = theme ?? throw new ArgumentNullException(nameof(theme));
            Loc = loc ?? throw new ArgumentNullException(nameof(loc));
            Commands = commands ?? throw new ArgumentNullException(nameof(commands));
            Router = router ?? throw new ArgumentNullException(nameof(router));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Events = events ?? throw new ArgumentNullException(nameof(events));
            Refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
            Notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            Approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
            Clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
            External = external ?? throw new ArgumentNullException(nameof(external));
            Modals = modals ?? throw new ArgumentNullException(nameof(modals));
            Status = status ?? throw new ArgumentNullException(nameof(status));
            Terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
            Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Navigate to a path.
        /// </summary>
        /// <param name="path">Path and query.</param>
        public void Navigate(string path)
        {
            Router.Navigate(path);
        }

        /// <summary>
        /// Show an API error dialog (with Copy details).
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="ex">Error.</param>
        /// <param name="retry">Retry action, or null.</param>
        public void ShowError(string title, ArmadaApiException ex, Action? retry = null)
        {
            ErrorDialog dialog = ErrorDialog.From(title, ex, retry != null, Loc, Theme.Current);
            Modals.Show(dialog, result =>
            {
                if (result as string == "retry") retry?.Invoke();
                else if (result as string == "copy") Clipboard.Copy(dialog.Details(), "Error details");
            });
        }

        /// <summary>
        /// Ask for confirmation; runs <paramref name="onConfirm"/> on the UI loop when confirmed.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="message">Message (translated).</param>
        /// <param name="onConfirm">Action.</param>
        /// <param name="confirmLabel">English confirm label.</param>
        /// <param name="requiredText">Word to type (typed-delete variant), or null.</param>
        /// <returns>The dialog.</returns>
        public ConfirmDialog Confirm(string title, string message, Action onConfirm, string confirmLabel = "Confirm", string? requiredText = null)
        {
            ConfirmDialog dialog = new ConfirmDialog(title, message, confirmLabel, "Cancel", requiredText, Loc, Theme.Current);
            dialog.Destructive = requiredText != null;
            Modals.Show(dialog, result =>
            {
                if (result is bool ok && ok) onConfirm();
            });
            return dialog;
        }

        #endregion
    }
}
