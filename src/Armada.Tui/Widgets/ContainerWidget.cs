namespace Armada.Tui.Widgets
{
    using System;
    using Armada.Tui.Theming;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Base for widgets that host children in a <see cref="FocusScope"/>: keys and mouse go through the scope, focus
    /// state flows into it, and theme changes are forwarded to every child. Not thread-safe.
    /// </summary>
    public abstract class ContainerWidget : ArmadaWidget, IFocusScopeOwner, IPasteTarget
    {
        #region Public-Members

        /// <inheritdoc />
        public FocusScope Scope { get; } = new FocusScope();

        /// <inheritdoc />
        public IFocusable? FocusedChild
        {
            get { return Scope.Focused as IFocusable; }
        }

        /// <summary>
        /// Shown with something to focus: a plain container (a button row, an action bar) whose children are all
        /// hidden or unfocusable renders nothing to interact with, so it is not a Tab stop. Region hosts (screens) and
        /// containers without children, which handle keys themselves, stay stops while <see cref="ArmadaWidget.Visible"/>.
        /// </summary>
        public override bool IsVisible
        {
            get
            {
                if (!base.IsVisible) return false;
                if (Scope.RegionHost || Scope.Children.Count == 0) return true;
                foreach (IWidget child in Scope.Children)
                {
                    if (FocusScope.IsFocusStop(child)) return true;
                }

                return false;
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            return Scope.HandleKey(key);
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            return Scope.HandleMouse(mouse);
        }

        /// <inheritdoc />
        public virtual bool HandlePaste(string text)
        {
            return Scope.FocusedLeaf() is IPasteTarget target && !ReferenceEquals(target, this) && target.HandlePaste(text);
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
            foreach (IWidget child in Scope.Children) ThemeApplicator.Apply(child, theme);
        }

        /// <inheritdoc />
        protected override void OnLocalizerChanged(Armada.Tui.Services.ITextLocalizer localizer)
        {
            foreach (IWidget child in Scope.Children)
            {
                if (child is ArmadaWidget aw) aw.Localizer = localizer;
            }
        }

        /// <summary>
        /// Add a child, giving it this container's palette and localizer.
        /// </summary>
        /// <typeparam name="T">Child type.</typeparam>
        /// <param name="child">Child.</param>
        /// <returns>The child.</returns>
        protected T AddChild<T>(T child) where T : IWidget
        {
            if (child is ArmadaWidget aw) aw.Localizer = Localizer;
            ThemeApplicator.Apply(child, Theme);
            return Scope.Add(child);
        }

        #endregion
    }
}
