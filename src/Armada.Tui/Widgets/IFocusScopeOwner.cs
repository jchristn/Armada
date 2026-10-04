namespace Armada.Tui.Widgets
{
    /// <summary>
    /// A widget that owns a nested <see cref="FocusScope"/> (forms, hubs, split panes, the shell itself), so parent
    /// scopes can enter it from either end and find the focused leaf.
    /// </summary>
    public interface IFocusScopeOwner
    {
        /// <summary>
        /// The nested scope.
        /// </summary>
        FocusScope Scope { get; }
    }
}
