namespace Armada.Tui.Modals
{
    using System;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Modals;

    /// <summary>
    /// Base for Armada dialogs: a TUIKit <see cref="DialogModal"/> (centered box, title, footer hint) that carries the
    /// Armada palette and localizer. Not thread-safe.
    /// </summary>
    public abstract class ArmadaDialog : DialogModal, IThemeable
    {
        #region Public-Members

        /// <summary>
        /// Palette. Never null.
        /// </summary>
        public ArmadaTheme Theme { get; private set; } = ThemePalettes.Dark();

        /// <summary>
        /// Localizer. Never null.
        /// </summary>
        public ITextLocalizer Localizer { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="localizer">Localizer, or null for English.</param>
        /// <param name="theme">Palette, or null for dark.</param>
        protected ArmadaDialog(string title, ITextLocalizer? localizer, ArmadaTheme? theme)
        {
            Localizer = localizer ?? new LocalizationService();
            Title = " " + Localizer.T(title ?? "") + " ";
            ApplyTheme(theme ?? ThemePalettes.Dark());
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            base.Render(surface);
            Rect box = FrameBounds;
            if (box.Width < 3 || box.Height < 3) return;
            // The dialog that holds focus (TUIKit's Modal.IsTopmost) wears the one focus treatment of the TUI (see
            // FocusFrame): TUIKit draws its whole box in the focus style with heavy lines (FocusedBorder,
            // FocusedBorderStyle, and FocusedTitleStyle, set from the palette). A dialog under another keeps its plain
            // border. The title and footer are drawn again with Armada's truncation ("..."), which TUIKit's centered
            // title (cut without a marker) and footer ("\u2026") do not share.
            if (!IsTopmost) return;
            if (!String.IsNullOrEmpty(Title) && box.Width > 4)
            {
                string label = TextCells.Truncate(" " + Title + " ", box.Width - 2);
                int start = box.X + 1 + Math.Max(0, (box.Width - 2 - TextCells.Width(label)) / 2);
                surface.DrawText(start, box.Y, label, Theme.FocusBorder);
            }

            if (!String.IsNullOrEmpty(FooterHint) && box.Width > 4)
            {
                string hint = TextCells.Truncate(FooterHint, box.Width - 4);
                int start = box.X + 1 + Math.Max(0, (box.Width - 2 - TextCells.Width(hint)) / 2);
                surface.DrawText(start, box.Bottom - 1, hint, BorderStyleColor.WithAttribute(CellAttributes.Dim, true));
            }
        }

        /// <inheritdoc />
        public void ApplyTheme(ArmadaTheme theme)
        {
            Theme = theme ?? throw new ArgumentNullException(nameof(theme));
            BackgroundStyle = theme.Dialog;
            BorderStyleColor = theme.DialogBorder;
            FocusedBorder = FocusFrame.FocusedBorderFor(theme);
            FocusedBorderStyle = theme.FocusBorder;
            FocusedTitleStyle = theme.FocusBorder;
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Translate English text.
        /// </summary>
        /// <param name="text">English text.</param>
        /// <returns>Translated text.</returns>
        protected string T(string text)
        {
            return Localizer.T(text);
        }

        /// <summary>
        /// Dialog text style.
        /// </summary>
        /// <returns>Style.</returns>
        protected CellStyle Body()
        {
            return Theme.Dialog;
        }

        /// <summary>
        /// Muted text over the dialog background.
        /// </summary>
        /// <returns>Style.</returns>
        protected CellStyle Dim()
        {
            return Theme.Dialog.WithForeground(Theme.Muted.Foreground);
        }

        /// <summary>
        /// A palette style recolored onto the dialog background.
        /// </summary>
        /// <param name="style">Style.</param>
        /// <returns>Style on the dialog background.</returns>
        protected CellStyle On(CellStyle style)
        {
            return style.WithBackground(Theme.Dialog.Background);
        }

        #endregion
    }
}
