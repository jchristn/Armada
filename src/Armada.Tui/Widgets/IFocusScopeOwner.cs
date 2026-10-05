namespace Armada.Tui.Widgets
{
    using TUIKit.Widgets;

    /// <summary>
    /// A widget that owns a nested <see cref="FocusScope"/> (forms, hubs, split panes, the shell itself), so parent
    /// scopes can enter it from either end and find the focused leaf. Each owner is a TUIKit
    /// <see cref="IFocusPathNode"/> (its <see cref="IFocusPathNode.FocusedChild"/> is the scope's focused child), so
    /// TUIKit's <see cref="FocusPath"/> and <c>TuiApplication.CurrentFocusPath</c> follow focus through Armada's tree.
    /// </summary>
    public interface IFocusScopeOwner : IFocusPathNode
    {
        /// <summary>
        /// The nested scope.
        /// </summary>
        FocusScope Scope { get; }
    }
}
