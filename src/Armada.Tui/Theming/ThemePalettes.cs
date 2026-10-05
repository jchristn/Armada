namespace Armada.Tui.Theming
{
    using System;
    using TUIKit;

    /// <summary>
    /// Built-in palettes. Every status color is paired with a text label by the widgets, so no state is conveyed by
    /// color alone. Thread-safe (each call builds a new instance).
    /// </summary>
    public static class ThemePalettes
    {
        #region Public-Methods

        /// <summary>
        /// Build the palette for a concrete mode (Auto is resolved by <see cref="ThemeService"/> first).
        /// </summary>
        /// <param name="mode">Mode.</param>
        /// <returns>A new palette.</returns>
        public static ArmadaTheme Build(ThemeModeEnum mode)
        {
            switch (mode)
            {
                case ThemeModeEnum.Light:
                    return Light();
                case ThemeModeEnum.HighContrast:
                    return HighContrast();
                default:
                    return Dark();
            }
        }

        /// <summary>
        /// The dark palette.
        /// </summary>
        /// <returns>A new palette.</returns>
        public static ArmadaTheme Dark()
        {
            Color bg = Color.FromRgb(0x1B, 0x1D, 0x23);
            Color fg = Color.FromRgb(0xD8, 0xDB, 0xE0);
            Color panel = Color.FromRgb(0x14, 0x16, 0x1B);
            Color raised = Color.FromRgb(0x26, 0x29, 0x31);
            Color accent = Color.FromRgb(0x4F, 0xA3, 0xE0);
            Color muted = Color.FromRgb(0x8A, 0x8F, 0x98);
            Color select = Color.FromRgb(0x2B, 0x4C, 0x70);
            ArmadaTheme t = Base("Dark", ThemeModeEnum.Dark, fg, bg, panel, raised, accent, muted, select,
                Color.FromRgb(0x3C, 0x40, 0x48),
                Color.FromRgb(0x4E, 0xC9, 0x8A), Color.FromRgb(0xE5, 0xB8, 0x4C), Color.FromRgb(0xF0, 0x65, 0x6A), Color.FromRgb(0x6C, 0xB6, 0xFF));
            return t;
        }

        /// <summary>
        /// The light palette.
        /// </summary>
        /// <returns>A new palette.</returns>
        public static ArmadaTheme Light()
        {
            Color bg = Color.FromRgb(0xFF, 0xFF, 0xFF);
            Color fg = Color.FromRgb(0x1F, 0x23, 0x28);
            Color panel = Color.FromRgb(0xF3, 0xF5, 0xF8);
            Color raised = Color.FromRgb(0xE6, 0xEA, 0xEF);
            Color accent = Color.FromRgb(0x09, 0x69, 0xDA);
            Color muted = Color.FromRgb(0x5F, 0x67, 0x70);
            Color select = Color.FromRgb(0xCC, 0xE4, 0xFF);
            ArmadaTheme t = Base("Light", ThemeModeEnum.Light, fg, bg, panel, raised, accent, muted, select,
                Color.FromRgb(0xC4, 0xCC, 0xD4),
                Color.FromRgb(0x1A, 0x7F, 0x37), Color.FromRgb(0x9A, 0x67, 0x00), Color.FromRgb(0xCF, 0x22, 0x2E), Color.FromRgb(0x09, 0x69, 0xDA));
            return t;
        }

        /// <summary>
        /// The high-contrast palette (black and white with bright accents, ASCII borders, bold emphasis, and reverse video
        /// or underline for every selected or focused state, so it also reads on a terminal without color).
        /// </summary>
        /// <returns>A new palette.</returns>
        public static ArmadaTheme HighContrast()
        {
            Color bg = Color.FromRgb(0, 0, 0);
            Color fg = Color.FromRgb(0xFF, 0xFF, 0xFF);
            Color yellow = Color.FromRgb(0xFF, 0xFF, 0x00);
            ArmadaTheme t = Base("High contrast", ThemeModeEnum.HighContrast, fg, bg, bg, bg, yellow, fg, fg,
                fg,
                Color.FromRgb(0x00, 0xFF, 0x00), yellow, Color.FromRgb(0xFF, 0x55, 0x55), Color.FromRgb(0x00, 0xFF, 0xFF));
            t.AsciiBorders = true;
            // Reverse video rather than swapped colors, so selection still shows when the terminal has no color
            // (NO_COLOR, monochrome); with color the result is the same black on white.
            CellStyle inverse = new CellStyle(fg, bg, CellAttributes.Bold | CellAttributes.Reverse);
            t.Selection = inverse;
            t.SelectionInactive = new CellStyle(fg, bg, CellAttributes.Underline | CellAttributes.Bold);
            t.GridCursor = inverse;
            t.SidebarSelected = new CellStyle(yellow, bg, CellAttributes.Bold | CellAttributes.Underline);
            t.SidebarFocused = inverse;
            t.MenuActive = inverse;
            t.ButtonFocused = inverse;
            t.InputFocused = new CellStyle(fg, bg, CellAttributes.Underline | CellAttributes.Bold);
            t.Input = new CellStyle(fg, bg, CellAttributes.Underline);
            t.TabActive = inverse;
            t.Muted = new CellStyle(fg, bg);
            return t;
        }

        #endregion

        #region Private-Methods

        private static ArmadaTheme Base(
            string name,
            ThemeModeEnum mode,
            Color fg,
            Color bg,
            Color panel,
            Color raised,
            Color accent,
            Color muted,
            Color select,
            Color border,
            Color success,
            Color warning,
            Color error,
            Color info)
        {
            ArmadaTheme t = new ArmadaTheme();
            t.Name = name;
            t.Mode = mode;
            t.Text = new CellStyle(fg, bg);
            t.Muted = new CellStyle(muted, bg);
            t.Accent = new CellStyle(accent, bg, CellAttributes.Bold);
            t.Border = new CellStyle(border, bg);
            t.Success = new CellStyle(success, bg, CellAttributes.Bold);
            t.Warning = new CellStyle(warning, bg, CellAttributes.Bold);
            t.Error = new CellStyle(error, bg, CellAttributes.Bold);
            t.Info = new CellStyle(info, bg);
            t.Selection = new CellStyle(mode == ThemeModeEnum.Light ? fg : Color.FromRgb(0xFF, 0xFF, 0xFF), select, CellAttributes.Bold);
            t.SelectionInactive = new CellStyle(fg, raised);
            t.Disabled = new CellStyle(muted, bg, CellAttributes.Dim);
            t.Header = new CellStyle(fg, panel);
            t.HeaderAccent = new CellStyle(accent, panel, CellAttributes.Bold);
            t.MenuBar = new CellStyle(fg, raised);
            t.MenuActive = new CellStyle(Color.FromRgb(0xFF, 0xFF, 0xFF), accent, CellAttributes.Bold);
            t.MenuDropdown = new CellStyle(fg, raised);
            t.Sidebar = new CellStyle(fg, panel);
            t.SidebarSection = new CellStyle(muted, panel, CellAttributes.Bold);
            t.SidebarSelected = new CellStyle(accent, panel, CellAttributes.Bold);
            t.SidebarFocused = new CellStyle(mode == ThemeModeEnum.Light ? fg : Color.FromRgb(0xFF, 0xFF, 0xFF), select, CellAttributes.Bold);
            t.StatusBar = new CellStyle(fg, panel);
            t.StatusKey = new CellStyle(accent, panel, CellAttributes.Bold);
            t.Input = new CellStyle(fg, raised);
            t.InputFocused = new CellStyle(fg, select);
            t.Button = new CellStyle(fg, raised);
            t.ButtonFocused = new CellStyle(Color.FromRgb(0xFF, 0xFF, 0xFF), accent, CellAttributes.Bold);
            t.GridHeader = new CellStyle(muted, bg, CellAttributes.Bold | CellAttributes.Underline);
            t.GridRow = new CellStyle(fg, bg);
            t.GridRowAlt = new CellStyle(fg, panel);
            t.GridCursor = new CellStyle(mode == ThemeModeEnum.Light ? fg : Color.FromRgb(0xFF, 0xFF, 0xFF), select, CellAttributes.Bold);
            t.GridMarked = new CellStyle(accent, bg, CellAttributes.Bold);
            t.TabActive = new CellStyle(accent, bg, CellAttributes.Bold | CellAttributes.Underline);
            t.TabInactive = new CellStyle(muted, bg);
            t.Dialog = new CellStyle(fg, raised);
            t.DialogBorder = new CellStyle(accent, raised);
            t.ToastInfo = new CellStyle(fg, raised);
            t.ToastSuccess = new CellStyle(success, raised, CellAttributes.Bold);
            t.ToastWarning = new CellStyle(warning, raised, CellAttributes.Bold);
            t.ToastError = new CellStyle(error, raised, CellAttributes.Bold);
            t.Code = new CellStyle(info, bg);
            t.Link = new CellStyle(accent, bg, CellAttributes.Underline);
            return t;
        }

        #endregion
    }
}
