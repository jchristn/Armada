namespace Armada.Tui.Services
{
    using System;

    /// <summary>
    /// Time source for timeouts, toasts, and polling, replaceable in tests.
    /// </summary>
    public interface IClock
    {
        /// <summary>
        /// Current UTC time.
        /// </summary>
        DateTime UtcNow { get; }
    }
}
