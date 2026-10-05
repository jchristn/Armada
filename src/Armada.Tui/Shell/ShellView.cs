namespace Armada.Tui.Shell
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Layout;
    using TUIKit.Widgets;

    /// <summary>
    /// The root widget (W1.2): header, menu bar, sidebar, main region, Ask dock, status bar, and toasts, laid out by
    /// <see cref="ShellLayout"/> on every frame (so resizes reflow at the breakpoints without a resize event). While
    /// signed out it shows the <see cref="LoginView"/>. The sidebar, main screen, and dock each sit in a reserved
    /// one-cell border, drawn in the theme's focus color around the pane that holds keyboard focus (or along the
    /// stretch of the main border beside the focused sub-region; see <see cref="FocusFrame"/>). It is bound to one
    /// full-screen TUIKit region, so it owns focus routing (sidebar, main, dock via <see cref="FocusScope"/>), key
    /// precedence (menu, focused pane, back on Backspace, then command bindings), and mouse hit-testing. Swaps the main screen when the router navigates. Not thread-safe.
    /// </summary>
    public class ShellView : ArmadaWidget, IFocusScopeOwner, IPasteTarget
    {
        #region Public-Members

        /// <inheritdoc />
        public FocusScope Scope { get; } = new FocusScope();

        /// <summary>
        /// Header.
        /// </summary>
        public HeaderBar Header { get; }

        /// <summary>
        /// Menu bar.
        /// </summary>
        public MenuBarView Menu { get; }

        /// <summary>
        /// Sidebar.
        /// </summary>
        public SidebarView Sidebar { get; }

        /// <summary>
        /// Status bar.
        /// </summary>
        public StatusBarView StatusBar { get; }

        /// <summary>
        /// Ask dock.
        /// </summary>
        public AskDockView Dock { get; }

        /// <summary>
        /// Login view (while signed out).
        /// </summary>
        public LoginView Login { get; private set; }

        /// <summary>
        /// Current main screen, or null.
        /// </summary>
        public ScreenBase? Screen { get; private set; } = null;

        /// <summary>
        /// Screen factory.
        /// </summary>
        public ScreenFactory Screens { get; }

        /// <summary>
        /// Layout used for the last frame.
        /// </summary>
        public ShellLayout? LastLayout { get; private set; } = null;

        /// <summary>
        /// True in narrow terminals when the user showed the sidebar with Ctrl+B.
        /// </summary>
        public bool NarrowSidebarOpen { get; private set; } = false;

        /// <summary>
        /// Decides when the run loop composes a frame (off by default; <see cref="ArmadaTuiApp.RunConsoleAsync"/>
        /// enables it). The shell invalidates it when the theme changes.
        /// </summary>
        public FrameGovernor Frames { get; } = new FrameGovernor();

        /// <summary>
        /// Raised when the breakpoint changes (resize).
        /// </summary>
        public event EventHandler<LayoutModeEnum>? LayoutModeChanged;

        #endregion

        #region Private-Members

        private readonly TuiContext _Context;
        private LayoutModeEnum _LastMode = LayoutModeEnum.Wide;
        private int _SurfaceWidth = 80;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="screens">Screen factory.</param>
        public ShellView(TuiContext context, ScreenFactory screens)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            Screens = screens ?? throw new ArgumentNullException(nameof(screens));
            Localizer = context.Loc;
            Header = new HeaderBar(context);
            Menu = new MenuBarView(context.Commands);
            Sidebar = new SidebarView(context);
            StatusBar = new StatusBarView(context);
            Dock = new AskDockView(context);
            Login = new LoginView(context);
            foreach (ArmadaWidget w in new ArmadaWidget[] { Header, Menu, Sidebar, StatusBar, Dock, Login }) w.Localizer = context.Loc;
            Scope.Wrap = true;
            context.Router.Navigated += (s, match) => ShowRoute(match);
            ApplyTheme(context.Theme.Current);
            RebuildScope();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show the login view (signed out), optionally with a notice.
        /// </summary>
        /// <param name="notice">English notice, or null.</param>
        public void ShowLogin(string? notice)
        {
            if (Screen != null) Screen.OnDeactivated();
            Screen = null;
            _Context.Commands.SetScreenCommands(null, null);
            _Context.Refresh.Attach(null, null);
            Login = new LoginView(_Context);
            Login.Notice = notice;
            Login.ApplyTheme(Theme);
            RebuildScope();
        }

        /// <summary>
        /// Focus a pane: sidebar, main, or dock.
        /// </summary>
        /// <param name="pane">Pane name.</param>
        /// <returns>True when focused.</returns>
        public bool FocusPane(string pane)
        {
            IWidget? target = pane == "sidebar" ? Sidebar : pane == "dock" ? Dock : (IWidget?)Screen;
            return target != null && Scope.Focus(target);
        }

        /// <summary>
        /// Move focus to the next pane (F6).
        /// </summary>
        public void NextPane()
        {
            Scope.Move(true);
        }

        /// <summary>
        /// Toggle the sidebar (Ctrl+B): in narrow terminals it opens the sidebar over the layout; otherwise it toggles
        /// the persisted preference.
        /// </summary>
        public void ToggleSidebar()
        {
            if (LastLayout != null && LastLayout.Mode == LayoutModeEnum.Narrow)
            {
                NarrowSidebarOpen = !NarrowSidebarOpen;
            }
            else
            {
                _Context.Prefs.Current.SidebarVisible = !_Context.Prefs.Current.SidebarVisible;
                _Context.Prefs.Save();
            }

            RebuildScope();
        }

        /// <summary>
        /// Toggle the Ask dock (Ctrl+J).
        /// </summary>
        public void ToggleDock()
        {
            _Context.Prefs.Current.AskDockVisible = !_Context.Prefs.Current.AskDockVisible;
            _Context.Prefs.Save();
            RebuildScope();
        }

        /// <summary>
        /// The focused leaf widget (diagnostics and tests).
        /// </summary>
        /// <returns>Widget or null.</returns>
        public IWidget? FocusedLeaf()
        {
            return Scope.FocusedLeaf();
        }

        /// <inheritdoc />
        public bool HandlePaste(string text)
        {
            IWidget? leaf = Scope.FocusedLeaf();
            if (leaf is IPasteTarget target && !ReferenceEquals(target, this)) return target.HandlePaste(text);
            IWidget? focused = Scope.Focused;
            return focused is IPasteTarget container && container.HandlePaste(text);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (_Context.Session.IsSignedIn && Menu.IsOpen) return Menu.HandleKey(key);
            if (_Context.Session.IsSignedIn && key.Code == KeyCode.F10 && key.Modifiers == KeyModifiers.None)
            {
                Menu.Open(0);
                return true;
            }

            if (_Context.Session.IsSignedIn && _Context.Commands.TryCompletePending(key, _Context.Clock.UtcNow)) return true;
            if (Scope.HandleKey(key)) return true;
            if (_Context.Session.IsSignedIn && key.Code == KeyCode.Backspace && key.Modifiers == KeyModifiers.None)
            {
                return _Context.Router.Back() || true;
            }

            return _Context.Commands.TryHandleKey(key, _Context.Clock.UtcNow);
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            ShellLayout? layout = LastLayout;
            if (layout == null || !_Context.Session.IsSignedIn) return Scope.HandleMouse(mouse);
            if (mouse.Y == layout.MenuBar.Y || Menu.IsOpen)
            {
                MouseEvent local = new MouseEvent(mouse.Kind, mouse.Button, mouse.X, mouse.Y - layout.MenuBar.Y, mouse.Modifiers, mouse.ClickCount);
                if (Menu.HandleMouse(local)) return true;
            }

            if (mouse.Y < layout.MenuBar.Y && mouse.Kind == MouseEventKind.Press)
            {
                int bellX = _SurfaceWidth - 12;
                if (mouse.X >= bellX) _Context.Commands.Execute("ask.notifications");
                return true;
            }

            return Scope.HandleMouse(mouse);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            Compose(surface);
        }

        /// <summary>
        /// Draw the terminal-too-small screen: what is needed, what there is, and how to continue.
        /// </summary>
        /// <param name="surface">Surface.</param>
        /// <param name="minimum">Minimum size.</param>
        /// <param name="size">Current size.</param>
        /// <param name="style">Text style.</param>
        /// <param name="loc">Localizer.</param>
        public static void RenderTooSmall(ISurface surface, Size minimum, Size size, CellStyle style, Services.ITextLocalizer loc)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (loc == null) throw new ArgumentNullException(nameof(loc));
            List<string> lines = new List<string>
            {
                loc.T("Terminal too small"),
                loc.T("Need {{need}}, have {{have}}.", Services.LocalizationArgs.Of("need", minimum.Width + "x" + minimum.Height, "have", size.Width + "x" + size.Height)),
                loc.T("Enlarge the window or reduce the font size."),
                "Ctrl+Q " + loc.T("Quit")
            };
            int top = Math.Max(0, (size.Height - lines.Count) / 2);
            for (int i = 0; i < lines.Count && top + i < size.Height; i++)
            {
                string line = TextCells.Truncate(lines[i], Math.Max(1, size.Width));
                int x = Math.Max(0, (size.Width - TextCells.Width(line)) / 2);
                SurfaceText.Draw(surface, x, top + i, line, i == 0 ? style.WithAttribute(CellAttributes.Bold, true) : style, size.Width - x);
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void OnFocusChangedCore(bool focused)
        {
            Scope.SetActive(focused);
        }

        /// <inheritdoc />
        protected override void OnThemeChanged(ArmadaTheme theme)
        {
            foreach (ArmadaWidget w in new ArmadaWidget[] { Header, Menu, Sidebar, StatusBar, Dock, Login }) w.ApplyTheme(theme);
            Screen?.ApplyTheme(theme);
            Frames.Invalidate();
        }

        #endregion

        #region Private-Methods

        private void Compose(ISurface surface)
        {
            Size size = surface.Size;
            _SurfaceWidth = size.Width;
            SurfaceText.FillRect(surface, new Rect(0, 0, size.Width, size.Height), Theme.Text);
            if (!_Context.Session.IsSignedIn)
            {
                LastLayout = null;
                if (size.Width < ShellLayout.MinimumSize.Width || size.Height < ShellLayout.MinimumSize.Height)
                {
                    RenderTooSmall(surface, ShellLayout.MinimumSize, size, Theme.Text, _Context.Loc);
                    return;
                }

                Scope.RenderChild(surface, Login, new Rect(0, 0, size.Width, size.Height - 1));
                StatusBar.Hints = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("Tab", "Next field"),
                    new KeyValuePair<string, string>("Enter", "Continue")
                };
                StatusBar.Hints.Add(new KeyValuePair<string, string>("F2", "Switch login mode"));
                StatusBar.Hints.Add(new KeyValuePair<string, string>("Ctrl+Q", "Quit"));
                StatusBar.Render(new SurfaceView(surface, new Rect(0, size.Height - 1, size.Width, 1)));
                ToastLayer.Render(surface, 1, _Context.Notifications.ActiveToasts(), Theme, _Context.Loc);
                return;
            }

            ShellLayout layout = new ShellLayout(size, Header.Rows, _Context.Prefs.Current.SidebarVisible, _Context.Prefs.Current.AskDockVisible, Dock.PreferredHeight, NarrowSidebarOpen);
            bool sidebarWasShown = LastLayout != null && !LastLayout.Sidebar.IsEmpty;
            LastLayout = layout;
            if (layout.Mode != _LastMode)
            {
                _LastMode = layout.Mode;
                LayoutModeChanged?.Invoke(this, layout.Mode);
            }

            if (layout.Mode == LayoutModeEnum.TooSmall)
            {
                RenderTooSmall(surface, ShellLayout.MinimumSize, size, Theme.Text, _Context.Loc);
                return;
            }

            if (sidebarWasShown != !layout.Sidebar.IsEmpty) RebuildScope();
            Header.Render(new SurfaceView(surface, layout.Header));
            if (!layout.Sidebar.IsEmpty)
            {
                Sidebar.Compact = layout.CompactSidebar;
                Scope.RenderChild(surface, Sidebar, layout.SidebarInner);
                FocusFrame.Draw(surface, layout.Sidebar, Theme, PaneHasFocus(Sidebar), Rect.Empty);
            }

            if (Screen != null)
            {
                Scope.RenderChild(surface, Screen, layout.MainInner);
                bool mainFocused = PaneHasFocus(Screen);
                FocusFrame.Draw(surface, layout.Main, Theme, mainFocused, mainFocused ? FocusedSubRegion(layout.MainInner) : Rect.Empty);
            }

            if (!layout.Dock.IsEmpty)
            {
                Scope.RenderChild(surface, Dock, layout.DockInner);
                FocusFrame.Draw(surface, layout.Dock, Theme, PaneHasFocus(Dock), Rect.Empty);
            }

            StatusBar.Hints = BuildHints();
            StatusBar.Render(new SurfaceView(surface, layout.StatusBar));
            ToastLayer.Render(surface, layout.MenuBar.Bottom, _Context.Notifications.ActiveToasts(), Theme, _Context.Loc);
            Rect menuArea = new Rect(0, layout.MenuBar.Y, size.Width, size.Height - layout.MenuBar.Y - 1);
            Menu.Render(new SurfaceView(surface, menuArea));
        }

        private bool PaneHasFocus(IWidget pane)
        {
            return Scope.IsActive && ReferenceEquals(Scope.Focused, pane);
        }

        /// <summary>
        /// The focused sub-region of the main screen in main-content coordinates, or empty when the screen itself is the
        /// focus target or the sub-region covers the whole screen.
        /// </summary>
        private Rect FocusedSubRegion(Rect mainInner)
        {
            if (Screen == null || !Screen.Scope.RegionHost) return Rect.Empty;
            Rect region = Screen.Scope.FocusedRegion();
            if (region.IsEmpty) return Rect.Empty;
            if (region.X <= 0 && region.Y <= 0 && region.Right >= mainInner.Width && region.Bottom >= mainInner.Height) return Rect.Empty;
            return region;
        }

        private void ShowRoute(RouteMatch match)
        {
            if (!_Context.Session.IsSignedIn) return;
            Screen?.OnDeactivated();
            ScreenBase screen = Screens.Create(match, _Context);
            screen.ApplyTheme(Theme);
            Screen = screen;
            _Context.Commands.SetScreenCommands(screen.ScreenKey, screen.Commands());
            _Context.Refresh.Attach(screen.ScreenKey, screen.RefreshAction(), screen.DefaultRefreshSeconds());
            Sidebar.SyncToRoute();
            _Context.Prefs.Current.LastRoute = match.FullPath;
            _Context.Prefs.Save();
            RebuildScope();
            Scope.Focus(screen);
            screen.OnActivated();
        }

        private void RebuildScope()
        {
            IWidget? previous = Scope.Focused;
            bool active = Scope.IsActive;
            if (active) Scope.SetActive(false);
            Scope.Clear();
            if (!_Context.Session.IsSignedIn)
            {
                Scope.Add(Login);
            }
            else
            {
                bool sidebarShown = LastLayout == null ? _Context.Prefs.Current.SidebarVisible : !LastLayout.Sidebar.IsEmpty;
                if (sidebarShown || NarrowSidebarOpen) Scope.Add(Sidebar);
                if (Screen != null) Scope.Add(Screen);
                if (_Context.Prefs.Current.AskDockVisible) Scope.Add(Dock);
                if (previous != null && !Scope.Focus(previous) && Screen != null) Scope.Focus(Screen);
            }

            if (active) Scope.SetActive(true);
        }

        private List<KeyValuePair<string, string>> BuildHints()
        {
            List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
            if (ReferenceEquals(Scope.Focused, Sidebar))
            {
                hints.Add(new KeyValuePair<string, string>("Enter", "Open"));
                hints.Add(new KeyValuePair<string, string>("Left/Right", "Collapse/expand"));
            }
            else if (Screen != null)
            {
                hints.AddRange(Screen.Hints);
            }

            hints.Add(new KeyValuePair<string, string>("?", "Help"));
            hints.Add(new KeyValuePair<string, string>("Ctrl+K", "Palette"));
            hints.Add(new KeyValuePair<string, string>("F10", "Menu"));
            hints.Add(new KeyValuePair<string, string>("Tab", "Next pane"));
            hints.Add(new KeyValuePair<string, string>("F5", "Refresh"));
            return hints;
        }

        #endregion
    }
}
