namespace Armada.Tui.Modals
{
    using System;
    using Armada.Tui.Services;
    using Armada.Tui.Theming;
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
    }
}
