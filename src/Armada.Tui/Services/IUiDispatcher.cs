namespace Armada.Tui.Services
{
    using System;

    /// <summary>
    /// Marshals work onto the UI loop thread. TUIKit widgets are not thread-safe, so every UI mutation that starts on
    /// a background thread (HTTP continuations, socket events, timers) goes through <see cref="Post"/>.
    /// Implementations are thread-safe.
    /// </summary>
    public interface IUiDispatcher
    {
        /// <summary>
        /// Queue an action to run on the UI loop thread.
        /// </summary>
        /// <param name="action">Action.</param>
        void Post(Action action);
    }
}
