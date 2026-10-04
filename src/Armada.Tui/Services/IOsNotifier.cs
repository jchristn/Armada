namespace Armada.Tui.Services
{
    /// <summary>
    /// Raises an operating-system notification.
    /// </summary>
    public interface IOsNotifier
    {
        /// <summary>
        /// Notify.
        /// </summary>
        /// <param name="mode">Mechanism.</param>
        /// <param name="title">Title.</param>
        /// <param name="message">Message.</param>
        void Notify(OsNotificationModeEnum mode, string title, string message);
    }
}
