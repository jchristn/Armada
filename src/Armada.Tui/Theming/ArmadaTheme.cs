namespace Armada.Tui.Theming
{
    using System;
    using TUIKit;

    /// <summary>
    /// The Armada TUI palette: one <see cref="CellStyle"/> per semantic role. <see cref="ThemeService"/> builds one per
    /// mode and pushes it into every widget (TUIKit widgets do not read the application theme themselves). Mutable
    /// only while being built; treat instances returned by <see cref="ThemeService"/> as read-only.
    /// </summary>
    public class ArmadaTheme
    {
        #region Public-Members

        /// <summary>
        /// Display name, for example Dark.
        /// </summary>
        public string Name { get; set; } = "Dark";

        /// <summary>
        /// Mode this palette implements.
        /// </summary>
        public ThemeModeEnum Mode { get; set; } = ThemeModeEnum.Dark;

        /// <summary>
        /// True to draw borders with ASCII characters (high contrast).
        /// </summary>
        public bool AsciiBorders { get; set; } = false;

        /// <summary>
        /// Body text on the main background.
        /// </summary>
        public CellStyle Text { get; set; } = CellStyle.Default;

        /// <summary>
        /// Secondary text.
        /// </summary>
        public CellStyle Muted { get; set; } = CellStyle.Default;

        /// <summary>
        /// Accent text (links, active titles).
        /// </summary>
        public CellStyle Accent { get; set; } = CellStyle.Default;

        /// <summary>
        /// Borders and dividers.
        /// </summary>
        public CellStyle Border { get; set; } = CellStyle.Default;

        /// <summary>
        /// Success state (always paired with a text label).
        /// </summary>
        public CellStyle Success { get; set; } = CellStyle.Default;

        /// <summary>
        /// Warning state.
        /// </summary>
        public CellStyle Warning { get; set; } = CellStyle.Default;

        /// <summary>
        /// Error state.
        /// </summary>
        public CellStyle Error { get; set; } = CellStyle.Default;

        /// <summary>
        /// Informational state.
        /// </summary>
        public CellStyle Info { get; set; } = CellStyle.Default;

        /// <summary>
        /// Selected row or item in a focused widget.
        /// </summary>
        public CellStyle Selection { get; set; } = CellStyle.Default;

        /// <summary>
        /// Selected row or item in an unfocused widget.
        /// </summary>
        public CellStyle SelectionInactive { get; set; } = CellStyle.Default;

        /// <summary>
        /// Disabled controls.
        /// </summary>
        public CellStyle Disabled { get; set; } = CellStyle.Default;

        /// <summary>
        /// Header bar background and text.
        /// </summary>
        public CellStyle Header { get; set; } = CellStyle.Default;

        /// <summary>
        /// Product name and highlights in the header.
        /// </summary>
        public CellStyle HeaderAccent { get; set; } = CellStyle.Default;

        /// <summary>
        /// Menu bar background and titles.
        /// </summary>
        public CellStyle MenuBar { get; set; } = CellStyle.Default;

        /// <summary>
        /// Active menu title or highlighted menu item.
        /// </summary>
        public CellStyle MenuActive { get; set; } = CellStyle.Default;

        /// <summary>
        /// Drop-down menu background.
        /// </summary>
        public CellStyle MenuDropdown { get; set; } = CellStyle.Default;

        /// <summary>
        /// Sidebar background and items.
        /// </summary>
        public CellStyle Sidebar { get; set; } = CellStyle.Default;

        /// <summary>
        /// Sidebar section headings.
        /// </summary>
        public CellStyle SidebarSection { get; set; } = CellStyle.Default;

        /// <summary>
        /// Sidebar item for the current route.
        /// </summary>
        public CellStyle SidebarSelected { get; set; } = CellStyle.Default;

        /// <summary>
        /// Sidebar cursor while the sidebar has focus.
        /// </summary>
        public CellStyle SidebarFocused { get; set; } = CellStyle.Default;

        /// <summary>
        /// Status bar background.
        /// </summary>
        public CellStyle StatusBar { get; set; } = CellStyle.Default;

        /// <summary>
        /// Key names in the status bar and hints.
        /// </summary>
        public CellStyle StatusKey { get; set; } = CellStyle.Default;

        /// <summary>
        /// Text input field.
        /// </summary>
        public CellStyle Input { get; set; } = CellStyle.Default;

        /// <summary>
        /// Focused text input field.
        /// </summary>
        public CellStyle InputFocused { get; set; } = CellStyle.Default;

        /// <summary>
        /// Button.
        /// </summary>
        public CellStyle Button { get; set; } = CellStyle.Default;

        /// <summary>
        /// Focused button.
        /// </summary>
        public CellStyle ButtonFocused { get; set; } = CellStyle.Default;

        /// <summary>
        /// Grid header row.
        /// </summary>
        public CellStyle GridHeader { get; set; } = CellStyle.Default;

        /// <summary>
        /// Grid row.
        /// </summary>
        public CellStyle GridRow { get; set; } = CellStyle.Default;

        /// <summary>
        /// Alternate grid row.
        /// </summary>
        public CellStyle GridRowAlt { get; set; } = CellStyle.Default;

        /// <summary>
        /// Grid cursor row while focused.
        /// </summary>
        public CellStyle GridCursor { get; set; } = CellStyle.Default;

        /// <summary>
        /// Grid rows marked for a bulk action.
        /// </summary>
        public CellStyle GridMarked { get; set; } = CellStyle.Default;

        /// <summary>
        /// Active tab in a tab strip.
        /// </summary>
        public CellStyle TabActive { get; set; } = CellStyle.Default;

        /// <summary>
        /// Inactive tab in a tab strip.
        /// </summary>
        public CellStyle TabInactive { get; set; } = CellStyle.Default;

        /// <summary>
        /// Dialog body.
        /// </summary>
        public CellStyle Dialog { get; set; } = CellStyle.Default;

        /// <summary>
        /// Dialog border.
        /// </summary>
        public CellStyle DialogBorder { get; set; } = CellStyle.Default;

        /// <summary>
        /// Informational toast.
        /// </summary>
        public CellStyle ToastInfo { get; set; } = CellStyle.Default;

        /// <summary>
        /// Success toast.
        /// </summary>
        public CellStyle ToastSuccess { get; set; } = CellStyle.Default;

        /// <summary>
        /// Warning toast.
        /// </summary>
        public CellStyle ToastWarning { get; set; } = CellStyle.Default;

        /// <summary>
        /// Error toast.
        /// </summary>
        public CellStyle ToastError { get; set; } = CellStyle.Default;

        /// <summary>
        /// Code and identifiers.
        /// </summary>
        public CellStyle Code { get; set; } = CellStyle.Default;

        /// <summary>
        /// Links.
        /// </summary>
        public CellStyle Link { get; set; } = CellStyle.Default;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with default styles.
        /// </summary>
        public ArmadaTheme()
        {
        }

        #endregion
    }
}
