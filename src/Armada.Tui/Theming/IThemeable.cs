namespace Armada.Tui.Theming
{
    /// <summary>
    /// A widget or view that restyles itself when the theme changes. Containers forward the call to their children.
    /// Called on the UI loop thread.
    /// </summary>
    public interface IThemeable
    {
        /// <summary>
        /// Apply a palette.
        /// </summary>
        /// <param name="theme">Palette. Never null.</param>
        void ApplyTheme(ArmadaTheme theme);
    }
}
