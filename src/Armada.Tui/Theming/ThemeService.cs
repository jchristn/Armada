namespace Armada.Tui.Theming
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Theming;

    /// <summary>
    /// Owns the active palette (Dark, Light, HighContrast, or Auto from the terminal background) and pushes it into
    /// every registered widget and into the TUIKit application theme (region backgrounds and built-in modals).
    /// TUIKit widgets do not read the application theme, so styles are pushed explicitly (see
    /// <see cref="ThemeApplicator"/>). Call on the UI loop thread.
    /// </summary>
    public class ThemeService
    {
        #region Public-Members

        /// <summary>
        /// Selected mode (may be Auto).
        /// </summary>
        public ThemeModeEnum Mode { get; private set; } = ThemeModeEnum.Dark;

        /// <summary>
        /// Concrete mode after resolving Auto.
        /// </summary>
        public ThemeModeEnum EffectiveMode
        {
            get { return Current.Mode; }
        }

        /// <summary>
        /// Active palette. Never null.
        /// </summary>
        public ArmadaTheme Current { get; private set; } = ThemePalettes.Dark();

        /// <summary>
        /// The TUIKit theme derived from <see cref="Current"/>. Never null.
        /// </summary>
        public Theme TuiKitTheme { get; private set; } = Theme.Dark;

        /// <summary>
        /// Raised after the palette changes, with the new palette.
        /// </summary>
        public event EventHandler<ArmadaTheme>? Changed;

        /// <summary>
        /// Reads an environment variable; replaceable for tests. Defaults to <see cref="Environment.GetEnvironmentVariable(string)"/>.
        /// </summary>
        public Func<string, string?> EnvironmentReader { get; set; } = Environment.GetEnvironmentVariable;

        #endregion

        #region Private-Members

        private readonly List<WeakReference<IThemeable>> _Targets = new List<WeakReference<IThemeable>>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with the dark palette.
        /// </summary>
        public ThemeService()
        {
            TuiKitTheme = BuildTuiKitTheme(Current);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve Auto to Dark or Light from <c>COLORFGBG</c> ("fg;bg"; background 7 or 15 means light). Unknown is Dark.
        /// </summary>
        /// <param name="mode">Mode.</param>
        /// <returns>A concrete mode.</returns>
        public ThemeModeEnum Resolve(ThemeModeEnum mode)
        {
            if (mode != ThemeModeEnum.Auto) return mode;
            string? colorFgBg = EnvironmentReader("COLORFGBG");
            if (String.IsNullOrWhiteSpace(colorFgBg)) return ThemeModeEnum.Dark;
            string[] parts = colorFgBg!.Split(';');
            string last = parts[parts.Length - 1].Trim();
            if (Int32.TryParse(last, out int bg) && (bg == 7 || bg == 15)) return ThemeModeEnum.Light;
            return ThemeModeEnum.Dark;
        }

        /// <summary>
        /// Select a mode, rebuild the palette, and push it everywhere.
        /// </summary>
        /// <param name="mode">Mode.</param>
        public void Apply(ThemeModeEnum mode)
        {
            Mode = mode;
            Current = ThemePalettes.Build(Resolve(mode));
            TuiKitTheme = BuildTuiKitTheme(Current);
            PushAll();
            EventHandler<ArmadaTheme>? handler = Changed;
            if (handler != null) handler(this, Current);
        }

        /// <summary>
        /// Register a long-lived themeable object; it receives the current palette now and on every change. Held
        /// weakly, so registration never keeps a widget alive.
        /// </summary>
        /// <param name="target">Target.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is null.</exception>
        public void Register(IThemeable target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            _Targets.Add(new WeakReference<IThemeable>(target));
            target.ApplyTheme(Current);
        }

        /// <summary>
        /// Build a TUIKit theme from a palette so region backgrounds, borders, and built-in modals match.
        /// </summary>
        /// <param name="palette">Palette.</param>
        /// <returns>The TUIKit theme.</returns>
        public static Theme BuildTuiKitTheme(ArmadaTheme palette)
        {
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            Theme theme = new Theme(
                "Armada " + palette.Name,
                palette.Text,
                palette.Accent,
                palette.Border,
                palette.Muted,
                palette.AsciiBorders,
                palette.Success,
                palette.Warning,
                palette.Error,
                palette.Info,
                palette.Selection,
                palette.Disabled);
            theme.SetStyle(Theme.SidebarRole, palette.Sidebar);
            theme.SetStyle(Theme.StatusBarRole, palette.StatusBar);
            return theme;
        }

        #endregion

        #region Private-Methods

        private void PushAll()
        {
            for (int i = _Targets.Count - 1; i >= 0; i--)
            {
                if (_Targets[i].TryGetTarget(out IThemeable? target)) target.ApplyTheme(Current);
                else _Targets.RemoveAt(i);
            }
        }

        #endregion
    }
}
