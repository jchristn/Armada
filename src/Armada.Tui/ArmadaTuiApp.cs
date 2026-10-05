namespace Armada.Tui
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Socket;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens;
    using Armada.Tui.Services;
    using Armada.Tui.Services.Credentials;
    using Armada.Tui.Shell;
    using Armada.Tui.Theming;
    using TUIKit.Hosting;
    using TUIKit.Input;
    using TUIKit.Terminal;

    /// <summary>
    /// Composition root of the Armada TUI: builds every service, binds the shell to a single full-screen TUIKit region,
    /// registers the global commands, wires socket events, polling, notifications, and session changes, and runs the
    /// startup sequence (preferences, theme, locale, catalog, session resume). <see cref="RunConsoleAsync"/> hosts it on
    /// the real terminal (Helm's <c>armada tui</c>); tests construct it over a <see cref="HeadlessBackend"/> with a stub
    /// client factory. One instance per process (TUIKit allows one running application).
    /// </summary>
    public class ArmadaTuiApp : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Services.
        /// </summary>
        public TuiContext Context { get; }

        /// <summary>
        /// Root widget.
        /// </summary>
        public ShellView Shell { get; }

        /// <summary>
        /// Screen factory (later waves register real screens here).
        /// </summary>
        public ScreenFactory Screens { get; } = new ScreenFactory();

        /// <summary>
        /// Start options.
        /// </summary>
        public TuiStartOptions Options { get; }

        /// <summary>
        /// The Ask Armada session.
        /// </summary>
        public Armada.Tui.Ask.AskController Ask { get; }

        /// <summary>
        /// Feeds the approvals queue from the inbox and entity-change events.
        /// </summary>
        public Armada.Tui.Approvals.ApprovalSources ApprovalSources { get; }

        /// <summary>
        /// Completes when the asynchronous startup (catalog and session resume) has finished.
        /// </summary>
        public Task StartupTask { get; private set; } = Task.CompletedTask;

        #endregion

        #region Private-Members

        private readonly TuiApplication _App;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Build the TUI over an application and backend.
        /// </summary>
        /// <param name="app">TUIKit application (not yet running).</param>
        /// <param name="backend">Its backend (clipboard, bell, OSC notifications).</param>
        /// <param name="options">Start options.</param>
        /// <param name="clientFactory">Creates a client for a base URL, or null for the default HTTP client.</param>
        /// <param name="credentials">Credential store, or null for <see cref="CredentialStoreFactory.Create"/>.</param>
        /// <param name="clock">Clock, or null for the system clock.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ArmadaTuiApp(TuiApplication app, ITerminalBackend backend, TuiStartOptions options, Func<string, ArmadaClient>? clientFactory = null, ICredentialStore? credentials = null, IClock? clock = null)
        {
            _App = app ?? throw new ArgumentNullException(nameof(app));
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            IClock clk = clock ?? new SystemClock();
            IUiDispatcher dispatcher = new TuiApplicationDispatcher(app);
            PreferencesService prefs = new PreferencesService(options.PreferencesPath);
            prefs.Load();
            ThemeService theme = new ThemeService();
            LocalizationService loc = new LocalizationService();
            ITerminalOutput terminal = new BackendTerminalOutput(backend);
            ICredentialStore creds = credentials ?? CredentialStoreFactory.Create();
            ServerProfile profile = ResolveProfile(prefs, options);
            Func<string, ArmadaClient> factory = clientFactory ?? (url => new ArmadaClient(new ArmadaClientOptions(url) { UserAgent = "Armada.Tui/" + Armada.Core.Constants.ProductVersion }));
            SessionService session = new SessionService(profile, factory, creds, prefs, dispatcher);
            ModalService modals = new ModalService(app, dispatcher, theme);
            NotificationService notifications = new NotificationService(clk, loc, terminal, new OsNotifier(terminal), System.IO.Path.Combine(System.IO.Path.GetDirectoryName(prefs.FilePath) ?? ".", "tui-notifications.json"));
            notifications.BellEnabled = prefs.Current.TerminalBell;
            notifications.OsMode = prefs.Current.OsNotifications;
            RefreshService refresh = new RefreshService(prefs, dispatcher, clk, () => app.Modals.IsActive);
            Context = new TuiContext(
                app, dispatcher, clk, prefs, theme, loc, new CommandService(), new Router(), session, new EventPump(dispatcher), refresh,
                notifications, new ApprovalService(),
                new ClipboardService(terminal, modals, notifications, loc, () => theme.Current),
                new ExternalService(app), modals, new StatusPoller(() => session.Client, dispatcher), terminal, creds);

            theme.Apply(prefs.Current.Theme);
            string locale = loc.SetLocale(prefs.Current.Locale ?? CultureInfo.CurrentUICulture.Name);
            session.Client.Options.AcceptLanguage = locale;

            Ask = new Armada.Tui.Ask.AskController(Context);
            Context.AttachAsk(Ask);
            ApprovalSources = new Armada.Tui.Approvals.ApprovalSources(Context);
            Screens.Register("AskScreen", (m, c) => new Armada.Tui.Screens.Ask.AskScreen(m, c));
            Screens.Register("ApprovalsScreen", (m, c) => new Armada.Tui.Approvals.ApprovalsScreen(m, c));
            Armada.Tui.Screens.DeliveryConfigScreens.Register(Screens);
            Armada.Tui.Screens.Operations.OperationsScreens.Register(Screens);
            Armada.Tui.Screens.Build.BuildScreens.Register(Screens);
            Shell = new ShellView(Context, Screens);
            GlobalCommands.Register(Context, Shell);
            Wire();
            ActivitySystemScreens.Register(Screens);
            Armada.Tui.Screens.Admin.SetupWizardAutoOpen.Attach(Context, !String.IsNullOrEmpty(Options.StartRoute));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Host the TUI on the real terminal until the user quits.
        /// </summary>
        /// <param name="options">Start options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exit code (0).</returns>
        public static async Task<int> RunConsoleAsync(TuiStartOptions options, CancellationToken token = default)
        {
            using (ConsoleBackend backend = new ConsoleBackend())
            using (TuiApplication app = new TuiApplication(backend))
            using (ArmadaTuiApp tui = new ArmadaTuiApp(app, backend, options))
            {
                IDisposable? telemetryHost = StartTelemetry(tui.Context.Prefs.Current.Telemetry, options);
                try
                {
                    tui.Start();
                    await app.RunAsync(token).ConfigureAwait(false);
                    app.Stop();
                    return 0;
                }
                finally
                {
                    telemetryHost?.Dispose();
                }
            }
        }

        /// <summary>
        /// Apply the <c>tui.json</c> telemetry settings: switch the TUI and TUIKit instruments on or off and, when
        /// enabled, start the exporter from <see cref="TuiStartOptions.TelemetryHostFactory"/>. Never throws.
        /// </summary>
        /// <param name="settings">Telemetry settings, or null for defaults (off).</param>
        /// <param name="options">Start options.</param>
        /// <returns>The exporter to dispose at exit, or null.</returns>
        public static IDisposable? StartTelemetry(TuiTelemetrySettings? settings, TuiStartOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            bool enabled = settings != null && settings.Enabled;
            TuiTelemetry.Configure(enabled);
            if (!enabled || options.TelemetryHostFactory == null) return null;
            try
            {
                return options.TelemetryHostFactory(settings!.ToTelemetrySettings());
            }
            catch (Exception)
            {
                // Telemetry never blocks the TUI.
                return null;
            }
        }

        /// <summary>
        /// Begin the asynchronous startup: load the i18n catalog, then resume a stored or environment session. The
        /// login view shows until a session is established.
        /// </summary>
        public void Start()
        {
            string? envToken = Options.Token ?? Environment.GetEnvironmentVariable(TuiPaths.TokenEnvironmentVariable);
            if (Options.Live) Context.Refresh.Start();
            TuiTelemetry.RecordSession();
            StartupTask = Task.Run(async () =>
            {
                if (await Context.Loc.LoadAsync(Context.Client).ConfigureAwait(false))
                {
                    Context.Dispatcher.Post(() =>
                    {
                        Context.Loc.SetLocale(Context.Prefs.Current.Locale ?? Context.Loc.Locale);
                        Shell.Login.RefreshPickers();
                    });
                }

                await Context.Session.TryResumeAsync(envToken).ConfigureAwait(false);
            });
        }

        /// <summary>
        /// Stop background work.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern.
        /// </summary>
        /// <param name="disposing">True from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                Context.Status.Dispose();
                Context.Refresh.Dispose();
                Context.Events.Dispose();
                Context.Session.Client.Dispose();
            }

            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private static ServerProfile ResolveProfile(PreferencesService prefs, TuiStartOptions options)
        {
            string? url = options.ServerUrl ?? Environment.GetEnvironmentVariable(TuiPaths.ServerUrlEnvironmentVariable);
            if (!String.IsNullOrWhiteSpace(options.ProfileName))
            {
                ServerProfile? named = prefs.FindProfile(options.ProfileName);
                if (named != null && String.IsNullOrWhiteSpace(url)) return Activate(prefs, named);
                return Save(prefs, prefs.UpsertProfile(options.ProfileName!, url ?? named?.Url ?? options.DefaultServerUrl));
            }

            if (!String.IsNullOrWhiteSpace(url))
            {
                string normalized = url!.Trim().TrimEnd('/');
                foreach (ServerProfile p in prefs.Current.Profiles)
                {
                    if (String.Equals(p.Url, normalized, StringComparison.OrdinalIgnoreCase)) return Activate(prefs, p);
                }

                string name = Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri) ? uri.Authority : "default";
                return Save(prefs, prefs.UpsertProfile(name, normalized));
            }

            ServerProfile? active = prefs.FindProfile(prefs.Current.ActiveProfile);
            if (active != null) return FollowLocalAdmiral(prefs, active, options.DefaultServerUrl);
            if (prefs.Current.Profiles.Count > 0) return FollowLocalAdmiral(prefs, Activate(prefs, prefs.Current.Profiles[0]), options.DefaultServerUrl);
            ServerProfile created = prefs.UpsertProfile("default", options.DefaultServerUrl);
            created.FollowsLocalAdmiral = true;
            return Save(prefs, created);
        }

        /// <summary>
        /// Keep the auto-created local profile on the local Admiral's current port, so a port change in settings.json
        /// (or a profile created while it held other values) does not leave the TUI pointing at a stale URL.
        /// </summary>
        private static ServerProfile FollowLocalAdmiral(PreferencesService prefs, ServerProfile profile, string localUrl)
        {
            bool follows = profile.FollowsLocalAdmiral ?? String.Equals(profile.Name, "default", StringComparison.OrdinalIgnoreCase);
            if (!follows) return profile;
            if (!LocalAdmiralDefaults.IsLoopback(profile.Url) || !LocalAdmiralDefaults.IsLoopback(localUrl)) return profile;
            string normalized = localUrl.Trim().TrimEnd('/');
            if (String.Equals(profile.Url, normalized, StringComparison.OrdinalIgnoreCase) && profile.FollowsLocalAdmiral == true) return profile;
            profile.Url = normalized;
            profile.FollowsLocalAdmiral = true;
            return Save(prefs, profile);
        }

        private static ServerProfile Activate(PreferencesService prefs, ServerProfile profile)
        {
            prefs.Current.ActiveProfile = profile.Name;
            return profile;
        }

        private static ServerProfile Save(PreferencesService prefs, ServerProfile profile)
        {
            prefs.Save();
            return profile;
        }

        private void Wire()
        {
            _App.AddRegion("shell", r => r.FillWidth().FillHeight().WithPadding(0));
            _App.Bind("shell", Shell);
            _App.CtrlCPolicy = CtrlCPolicy.Custom;
            _App.AutoRenderNotifications = false;
            _App.Theme = Context.Theme.TuiKitTheme;
            _App.PasteReceived += text => Shell.HandlePaste(text);
            _App.TerminalFocusChanged += focused =>
            {
                Context.Notifications.TerminalFocused = focused;
                Ask.OnTerminalFocusChanged(focused);
            };
            Context.Theme.Changed += (s, t) =>
            {
                _App.Theme = Context.Theme.TuiKitTheme;
                Shell.ApplyTheme(t);
            };
            Context.Loc.Changed += (s, e) => Shell.Login.RefreshPickers();

            Context.Events.Subscribe("*", message =>
            {
                Context.Notifications.HandleSocketMessage(message);
                Context.Status.NudgeInbox();
            });
            Context.Notifications.RouteOpener = route =>
            {
                if (Context.Session.IsSignedIn) Context.Navigate(route);
            };
            Context.Approvals.Arrived += (s, item) => Context.Notifications.Attention(Context.Loc.T("Approval needed") + ": " + item.Title);

            Context.Session.SignedIn += (s, e) =>
            {
                if (Options.Live)
                {
                    ArmadaSocketOptions socket = new ArmadaSocketOptions(Context.Session.Profile.Url, () => Context.Session.Token);
                    Context.Events.Start(socket);
                    Context.Status.Start();
                }

                Context.Router.Reset();
                Shell.Login.Notice = null;
                Context.Router.Navigate(Options.StartRoute ?? Context.Prefs.Current.LastRoute ?? "/ask");
                Shell.FocusPane("main");
            };
            Context.Session.SignedOut += (s, reason) =>
            {
                Context.Events.Stop();
                Context.Status.Stop();
                Context.Approvals.Clear();
                Context.Router.Reset();
                Shell.ShowLogin(reason);
            };
        }

        #endregion
    }
}
