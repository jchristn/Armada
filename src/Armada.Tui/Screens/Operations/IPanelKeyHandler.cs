namespace Armada.Tui.Screens.Operations
{
    using TUIKit.Input;
    using TUIKit;

    /// <summary>
    /// A detail-screen panel whose single-key shortcuts take precedence over the screen's action keys while the panel
    /// has focus (the Failure tab's <c>r</c> runs recovery, not Restart). The panel lists those keys in its own status
    /// bar hints so they stay discoverable from the current focus.
    /// </summary>
    public interface IPanelKeyHandler
    {
        /// <summary>
        /// Handle a key before the screen's actions.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>True when the panel handled the key.</returns>
        bool HandlePanelKey(KeyEvent key);
    }
}
