namespace Armada.Tui.Widgets
{
    using System;
    using Armada.Tui.Services;
    using Armada.Tui.Theming;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Base class for Armada widgets: carries the palette and localizer pushed down the tree, tracks focus, and
    /// provides default (non-consuming) input handling. Not thread-safe; use on the UI loop thread.
    /// </summary>
    public abstract class ArmadaWidget : IWidget, IFocusable, IFocusAware, IMouseAware, IThemeable, IHideable
    {
        #region Public-Members

        /// <summary>
        /// Active palette. Never null.
        /// </summary>
        public ArmadaTheme Theme { get; private set; } = ThemePalettes.Dark();

        /// <summary>
        /// Localizer. Never null (English pass-through by default).
        /// </summary>
        public ITextLocalizer Localizer
        {
            get { return _Localizer; }
            set
            {
                _Localizer = value ?? new LocalizationService();
                OnLocalizerChanged(_Localizer);
            }
        }

        /// <summary>
        /// True while the widget holds keyboard focus.
        /// </summary>
        public bool IsFocused { get; private set; } = false;

        /// <summary>
        /// True when the widget can take focus (focus routers skip widgets that cannot). Default true.
        /// </summary>
        public virtual bool CanFocus { get; set; } = true;

        /// <summary>
        /// Hidden widgets are skipped by containers. Default true.
        /// </summary>
        public bool Visible { get; set; } = true;

        /// <summary>
        /// True while the widget is shown with something to interact with (TUIKit's <see cref="IHideable"/>): focus
        /// traversal skips it otherwise, so Tab never lands on something the user cannot see. Default
        /// <see cref="Visible"/>; containers and widgets that render nothing while empty narrow it.
        /// </summary>
        public virtual bool IsVisible
        {
            get { return Visible; }
        }

        /// <summary>
        /// Text drawn on the top line of this widget's box when it is a focus region of a screen (already translated),
        /// or null for none (see <see cref="RegionFrames"/>). Default null.
        /// </summary>
        public string? BoxTitle { get; set; } = null;

        /// <summary>
        /// Raised after focus enters (true) or leaves (false).
        /// </summary>
        public event EventHandler<bool>? FocusChanged;

        #endregion

        #region Private-Members

        private ITextLocalizer _Localizer = new LocalizationService();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public virtual Size Measure(Size available)
        {
            return available;
        }

        /// <inheritdoc />
        public abstract void Render(ISurface surface);

        /// <inheritdoc />
        public virtual bool HandleKey(KeyEvent key)
        {
            return false;
        }

        /// <inheritdoc />
        public virtual bool HandleMouse(MouseEvent mouse)
        {
            return false;
        }

        /// <inheritdoc />
        public void OnFocusChanged(bool focused)
        {
            if (IsFocused == focused) return;
            IsFocused = focused;
            OnFocusChangedCore(focused);
            EventHandler<bool>? handler = FocusChanged;
            if (handler != null) handler(this, focused);
        }

        /// <inheritdoc />
        public void ApplyTheme(ArmadaTheme theme)
        {
            Theme = theme ?? throw new ArgumentNullException(nameof(theme));
            OnThemeChanged(theme);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Translate English text through the localizer.
        /// </summary>
        /// <param name="text">English text.</param>
        /// <returns>Translated text.</returns>
        protected string T(string text)
        {
            return _Localizer.T(text);
        }

        /// <summary>
        /// Called after focus changes.
        /// </summary>
        /// <param name="focused">New focus state.</param>
        protected virtual void OnFocusChangedCore(bool focused)
        {
        }

        /// <summary>
        /// Called after the localizer changes; containers forward it to children.
        /// </summary>
        /// <param name="localizer">Localizer.</param>
        protected virtual void OnLocalizerChanged(ITextLocalizer localizer)
        {
        }

        /// <summary>
        /// Called after the palette changes; containers forward it to children.
        /// </summary>
        /// <param name="theme">Palette.</param>
        protected virtual void OnThemeChanged(ArmadaTheme theme)
        {
        }

        #endregion
    }
}
