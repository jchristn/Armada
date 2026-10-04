namespace Armada.Tui.Services
{
    using System;
    using TUIKit.Modals;

    /// <summary>
    /// Shows modals and delivers their results on the UI loop thread (TUIKit's <c>ShowAsync</c> continuations run off
    /// the loop, so results are posted back).
    /// </summary>
    public interface IModalHost
    {
        /// <summary>
        /// True while any modal is open (auto-refresh pauses).
        /// </summary>
        bool IsModalOpen { get; }

        /// <summary>
        /// Show a modal.
        /// </summary>
        /// <param name="modal">Modal.</param>
        /// <param name="onClosed">Called on the UI loop thread with the result (null when cancelled), or null.</param>
        void Show(Modal modal, Action<object?>? onClosed = null);
    }
}
