namespace Armada.Tui.Modals
{
    using System;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Layout;
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

        /// <summary>
        /// True while this dialog is the topmost modal (it holds keyboard focus), or null when it is not on a stack the
        /// host tracks (treated as topmost). Set by the modal host when the dialog is shown.
        /// </summary>
        public Func<bool>? IsTopmost { get; set; } = null;

        /// <summary>
        /// The rectangle of the dialog's box (border included) as of the most recent render, or empty before it.
        /// </summary>
        public Rect LastBox { get; private set; } = Rect.Empty;

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
            Rect box = BoxFor(surface.Size);
            LastBox = box;
            if (box.Width < 3 || box.Height < 3) return;
            // The dialog that holds focus wears the one focus treatment of the TUI (see FocusFrame): its whole box in
            // the focus style with heavy lines. A dialog under another keeps its plain border.
            if (IsTopmost != null && !IsTopmost()) return;
            FocusFrame.Draw(surface, box, Theme, true);
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

        #region Private-Methods

        /// <summary>
        /// The box <see cref="DialogModal.Render"/> draws on a screen of this size (the same measuring, clamping, and
        /// centering).
        /// </summary>
        private Rect BoxFor(Size screen)
        {
            Padding pad = ContentPadding;
            int chromeWidth = 2 + pad.Horizontal;
            int chromeHeight = 2 + pad.Vertical;
            int availableWidth = Math.Max(1, screen.Width - chromeWidth);
            int contentWidth = Math.Min(Math.Clamp(MeasureContentWidth(availableWidth), MinContentWidth, MaxContentWidth), availableWidth);
            int availableHeight = Math.Max(1, screen.Height - chromeHeight);
            int contentHeight = Math.Min(Math.Clamp(MeasureContentHeight(contentWidth), MinContentHeight, MaxContentHeight), availableHeight);
            int boxWidth = contentWidth + chromeWidth;
            int boxHeight = contentHeight + chromeHeight;
            return new Rect(Math.Max(0, (screen.Width - boxWidth) / 2), Math.Max(0, (screen.Height - boxHeight) / 2), boxWidth, boxHeight);
        }

        #endregion
    }
}
