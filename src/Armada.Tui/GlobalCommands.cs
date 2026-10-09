namespace Armada.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Tui.Input;
    using Armada.Tui.Modals;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Shell;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit.Hosting;

    /// <summary>
    /// Registers the session-wide commands (File, Go, View, Ask, Help) with their key map. Screens add their own
    /// commands to the Actions menu. Call once on the UI loop thread.
    /// </summary>
    public static class GlobalCommands
    {
        #region Public-Methods

        /// <summary>
        /// Register every global command.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="shell">Shell.</param>
        /// <param name="app">Application (lets the screen snapshot include open dialogs), or null.</param>
        public static void Register(TuiContext context, ShellView shell, TuiApplication? app = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (shell == null) throw new ArgumentNullException(nameof(shell));
            CommandService c = context.Commands;
            Func<bool> signedIn = () => context.Session.IsSignedIn;

            // File
            c.Register(Cmd("file.switch-profile", "Switch server profile...", CommandMenuEnum.File, () => SwitchProfile(context, shell), signedIn));
            c.Register(Cmd("file.proxy-switch", "Switch Deployment", CommandMenuEnum.File, () => ProxySwitch(context), () => context.Session.Proxy != null));
            c.Register(Cmd("file.proxy-logout", "Proxy Logout", CommandMenuEnum.File, () => ProxyLogout(context), () => context.Session.Proxy != null));
            c.Register(Cmd("file.sign-out", "Sign out", CommandMenuEnum.File, () => _ = context.Session.SignOutAsync(), signedIn));
            c.Register(Cmd("file.quit", "Quit", CommandMenuEnum.File, context.Quit, null, "ctrl+q"));

            // Go: every sidebar destination with the plan's g-letter keys.
            foreach (NavItem item in NavCatalog.AllItems())
            {
                string to = item.To;
                ArmadaCommand go = Cmd("go." + to.Trim('/').Replace('/', '.').Replace("-", "") + (to == "/" ? "home" : ""), item.Label, CommandMenuEnum.Go, () => context.Navigate(to), signedIn,
                    item.GoKey != null ? "g " + item.GoKey : null);
                go.Group = "Go to";
                c.Register(go);
            }

            c.Register(Group(Cmd("go.approvals", "Approvals", CommandMenuEnum.Go, () => context.Navigate("/approvals"), signedIn), "Go to"));
            c.Register(Group(Cmd("go.back", "Back", CommandMenuEnum.Go, () => context.Router.Back(), signedIn, "alt+left"), "Go to"));
            c.Register(Group(Cmd("go.forward", "Forward", CommandMenuEnum.Go, () => context.Router.Forward(), signedIn, "alt+right"), "Go to"));

            // View
            c.Register(Cmd("view.theme.dark", "Theme: Dark", CommandMenuEnum.View, () => SetTheme(context, ThemeModeEnum.Dark)));
            c.Register(Cmd("view.theme.light", "Theme: Light", CommandMenuEnum.View, () => SetTheme(context, ThemeModeEnum.Light)));
            c.Register(Cmd("view.theme.high-contrast", "Theme: High contrast", CommandMenuEnum.View, () => SetTheme(context, ThemeModeEnum.HighContrast)));
            c.Register(Cmd("view.theme.auto", "Theme: Auto", CommandMenuEnum.View, () => SetTheme(context, ThemeModeEnum.Auto)));
            c.Register(Cmd("view.icons.auto", "Icons: Auto", CommandMenuEnum.View, () => SetGlyphs(context, GlyphModeEnum.Auto)));
            c.Register(Cmd("view.icons.unicode", "Icons: Unicode", CommandMenuEnum.View, () => SetGlyphs(context, GlyphModeEnum.Unicode)));
            c.Register(Cmd("view.icons.ascii", "Icons: ASCII", CommandMenuEnum.View, () => SetGlyphs(context, GlyphModeEnum.Ascii)));
            c.Register(Cmd("view.language", "Language...", CommandMenuEnum.View, () => PickLanguage(context)));
            c.Register(Cmd("view.sidebar", "Toggle sidebar", CommandMenuEnum.View, shell.ToggleSidebar, signedIn, "ctrl+b"));
            c.Register(Cmd("view.dock", "Toggle Ask dock", CommandMenuEnum.View, shell.ToggleDock, signedIn, "ctrl+j"));
            c.Register(Cmd("view.next-pane", "Next pane", CommandMenuEnum.View, shell.NextPane, signedIn, "f6"));
            c.Register(Cmd("view.refresh", "Refresh", CommandMenuEnum.View, () => context.Refresh.RefreshNow(), signedIn, "f5"));
            c.Register(Cmd("view.refresh-interval", "Auto-refresh interval...", CommandMenuEnum.View, () => PickInterval(context), signedIn));
            c.Register(Cmd("view.mouse", "Toggle mouse capture (terminal selection)", CommandMenuEnum.View, () => context.App.ToggleMouseCapture(), null, "f12"));

            // Ask
            c.Register(Cmd("ask.new", "New conversation", CommandMenuEnum.Ask, () => { if (context.Ask != null) context.Ask.NewConversation(); else context.Navigate("/ask"); }, signedIn));
            c.Register(Cmd("ask.approvals", "Approvals center", CommandMenuEnum.Ask, () => context.Navigate("/approvals"), signedIn, "ctrl+a"));
            c.Register(Cmd("ask.about-this", "Ask about this", CommandMenuEnum.Ask, () => context.Ask?.AskAbout(context.Router.Current), signedIn, "alt+a"));
            c.Register(Cmd("ask.notifications", "Notifications", CommandMenuEnum.Ask, () => ShowNotifications(context), null, "ctrl+n"));
            c.Register(Cmd("ask.toast-action", "Run the latest toast action", CommandMenuEnum.Ask, () => context.Notifications.RunLatestToastAction(), null, "ctrl+o"));

            // Help
            c.Register(Cmd("help.keys", "Keyboard shortcuts", CommandMenuEnum.Help, () => ShowHelp(context, shell), null, "?", "f1"));
            c.Register(Cmd("help.palette", "Command palette", CommandMenuEnum.Help, () => ShowPalette(context, shell), signedIn, "ctrl+k"));
            c.Register(Cmd("help.about", "About Armada", CommandMenuEnum.Help, () => ShowAbout(context)));
            c.Register(Cmd("help.snapshot", "Save screen snapshot...", CommandMenuEnum.Help, () => SaveSnapshot(context, shell, app)));
            c.Register(Cmd("help.docs", "Documentation (opens a browser)", CommandMenuEnum.Help, () => context.External.OpenUrl("https://github.com/jchristn/Armada/blob/main/docs/TUI.md")));
            c.SetOverrides(context.Prefs.Current.KeyBindings);
        }

        /// <summary>
        /// Open the command palette.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="shell">Shell.</param>
        /// <returns>The modal.</returns>
        public static CommandPaletteModal ShowPalette(TuiContext context, ShellView shell)
        {
            CommandPaletteModal palette = new CommandPaletteModal(context.Commands, context.Navigate, context.Loc, context.Theme.Current, target => RouteVisible(context, target));
            context.Modals.Show(palette, result =>
            {
                if (result is PaletteEntry entry) entry.Action();
            });
            return palette;
        }

        /// <summary>
        /// Open the help overlay.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="shell">Shell.</param>
        /// <returns>The modal.</returns>
        public static HelpOverlayModal ShowHelp(TuiContext context, ShellView shell)
        {
            HelpOverlayModal help = new HelpOverlayModal(context.Commands, shell.Screen?.Title ?? "Armada", context.Loc, context.Theme.Current);
            context.Modals.Show(help);
            return help;
        }

        /// <summary>
        /// Open the notification center.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <returns>The modal.</returns>
        public static NotificationCenterModal ShowNotifications(TuiContext context)
        {
            NotificationCenterModal modal = new NotificationCenterModal(context.Notifications, context.Clock, context.Loc, context.Theme.Current);
            context.Modals.Show(modal, result =>
            {
                if (result is string route && context.Session.IsSignedIn) context.Navigate(route);
            });
            return modal;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Save the screen as plain text (what the terminal shows, without colors, ASCII-transliterated in ASCII icon
        /// mode) to a file the user picks, for bug reports. The text is captured before the path prompt opens.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="shell">Shell.</param>
        /// <param name="app">Application (for open modals), or null.</param>
        public static void SaveSnapshot(TuiContext context, ShellView shell, TuiApplication? app)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (shell == null) throw new ArgumentNullException(nameof(shell));
            int width = shell.LastSize.Width > 0 ? shell.LastSize.Width : 120;
            int height = shell.LastSize.Height > 0 ? shell.LastSize.Height : 40;
            string text = TuiSnapshot.Render(shell, app, width, height);
            string name = "armada-tui-" + context.Clock.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".txt";
            Screens.Kit.PathPrompt.AskSave(context, "Save Screen Snapshot", name, path =>
            {
                try
                {
                    string saved = context.External.SaveText(path, text);
                    Screens.Kit.ScreenOps.Toast(context, NotificationSeverityEnum.Success, "Snapshot saved to {{path}}.", LocalizationArgs.Of("path", saved));
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
                {
                    Screens.Kit.ScreenOps.Toast(context, NotificationSeverityEnum.Error, "Failed to save file: {{message}}", LocalizationArgs.Of("message", ex.Message));
                }
            });
        }

        private static ArmadaCommand Cmd(string id, string title, CommandMenuEnum menu, Action handler, Func<bool>? enabled = null, params string?[] gestures)
        {
            ArmadaCommand command = new ArmadaCommand(id, title, menu, handler, gestures.Where(g => g != null).Select(g => g!).ToArray());
            command.IsEnabled = enabled;
            return command;
        }

        private static ArmadaCommand Group(ArmadaCommand command, string group)
        {
            command.Group = group;
            return command;
        }

        private static bool RouteVisible(TuiContext context, string target)
        {
            RouteMatch match = Router.Resolve(target);
            if (match.Tab == null) return true;
            return (!match.Tab.GlobalAdminOnly || context.Session.IsGlobalAdmin) && (!match.Tab.TenantAdminOnly || context.Session.IsTenantAdmin);
        }

        private static void SetTheme(TuiContext context, ThemeModeEnum mode)
        {
            context.Theme.Apply(mode);
            context.Prefs.Current.Theme = mode;
            context.Prefs.Save();
            context.Notifications.Toast(NotificationSeverityEnum.Info, context.Loc.T("Theme") + ": " + context.Loc.T(context.Theme.Current.Name));
        }

        private static void SetGlyphs(TuiContext context, GlyphModeEnum mode)
        {
            context.Theme.ApplyGlyphs(mode);
            context.Prefs.Current.Glyphs = mode;
            context.Prefs.Save();
            string effective = context.Theme.AsciiGlyphs ? "ASCII" : "Unicode";
            string label = mode == GlyphModeEnum.Auto ? context.Loc.T("Auto") + " (" + effective + ")" : effective;
            context.Notifications.Toast(NotificationSeverityEnum.Info, context.Loc.T("Icons") + ": " + label);
        }

        private static void PickLanguage(TuiContext context)
        {
            List<SelectOption<string>> options = context.Loc.SupportedLocales.Select(l => new SelectOption<string>(l.Code, l.NativeLabel, l.Label)).ToList();
            PickerModal<string> picker = new PickerModal<string>("Language", options, context.Loc, context.Theme.Current);
            picker.List.SelectValue(context.Loc.Locale);
            context.Modals.Show(picker, result =>
            {
                if (!(result is SelectOption<string> chosen)) return;
                string locale = context.Loc.SetLocale(chosen.Value);
                context.Prefs.Current.Locale = locale;
                context.Prefs.Save();
                context.Client.Options.AcceptLanguage = locale;
            });
        }

        private static void PickInterval(TuiContext context)
        {
            List<SelectOption<int>> options = RefreshService.Intervals.Select(s => new SelectOption<int>(s, s == 0 ? context.Loc.T("Off") : s + "s")).ToList();
            PickerModal<int> picker = new PickerModal<int>("Auto-refresh interval", options, context.Loc, context.Theme.Current);
            picker.List.SelectValue(context.Refresh.IntervalSeconds);
            context.Modals.Show(picker, result =>
            {
                if (result is SelectOption<int> chosen) context.Refresh.SetInterval(chosen.Value);
            });
        }

        private static void SwitchProfile(TuiContext context, ShellView shell)
        {
            List<SelectOption<string>> options = context.Prefs.Current.Profiles.Select(p => new SelectOption<string>(p.Name, p.Name, p.Url)).ToList();
            PickerModal<string> picker = new PickerModal<string>("Switch server profile", options, context.Loc, context.Theme.Current);
            context.Modals.Show(picker, result =>
            {
                if (!(result is SelectOption<string> chosen)) return;
                ServerProfile? profile = context.Prefs.FindProfile(chosen.Value);
                if (profile == null || ReferenceEquals(profile, context.Session.Profile)) return;
                context.Session.Expire("Switched server profile.");
                context.Session.SwitchProfile(profile);
                shell.ShowLogin(null);
                _ = Task.Run(() => context.Session.TryResumeAsync(null));
            });
        }

        private static void ProxySwitch(TuiContext context)
        {
            _ = Task.Run(async () =>
            {
                try { await context.Client.ClearProxySessionInstanceAsync().ConfigureAwait(false); }
                catch (Armada.Client.ArmadaApiException) { }
                context.Dispatcher.Post(() => context.Session.Expire("Choose a deployment in Armada.Proxy, then sign in again."));
            });
        }

        private static void ProxyLogout(TuiContext context)
        {
            _ = Task.Run(async () =>
            {
                try { await context.Client.LogoutProxyAsync().ConfigureAwait(false); }
                catch (Armada.Client.ArmadaApiException) { }
                await context.Session.SignOutAsync().ConfigureAwait(false);
            });
        }

        private static void ShowAbout(TuiContext context)
        {
            string text = "Armada TUI v" + Armada.Core.Constants.ProductVersion + "\n\n"
                + context.Loc.T("Server") + ": " + context.Session.Profile.Url + "\n"
                + context.Loc.T("Profile") + ": " + context.Session.Profile.Name + "\n"
                + context.Loc.T("Theme") + ": " + context.Theme.Current.Name + "\n"
                + context.Loc.T("Language") + ": " + context.Loc.Locale + "\n"
                + context.Loc.T("Credential store") + ": " + context.Credentials.Name + "\n\n"
                + "https://github.com/jchristn/Armada";
            context.Modals.Show(new ViewerModal("About Armada", new JsonOrTextViewer(text), context.Loc, context.Theme.Current));
        }

        #endregion
    }
}
