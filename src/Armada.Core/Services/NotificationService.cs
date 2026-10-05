namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Sends desktop notifications for mission events.
    /// Cross-platform: macOS (osascript), Linux (notify-send), Windows (PowerShell toast). The command is run through an
    /// <see cref="INotificationCommandRunner"/>, so tests can verify it without raising real notifications.
    /// </summary>
    public class NotificationService
    {
        #region Private-Members

        private readonly INotificationCommandRunner _Runner;
        private readonly DesktopPlatformEnum _Platform;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for the current platform, starting real processes.
        /// </summary>
        public NotificationService()
            : this(new ProcessNotificationCommandRunner(), CurrentPlatform())
        {
        }

        /// <summary>
        /// Instantiate with a command runner and platform.
        /// </summary>
        /// <param name="runner">Command runner.</param>
        /// <param name="platform">Platform whose notification command to use.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="runner"/> is null.</exception>
        public NotificationService(INotificationCommandRunner runner, DesktopPlatformEnum platform)
        {
            _Runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _Platform = platform;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Send a desktop notification. Best effort: a failure to start the command is ignored.
        /// </summary>
        /// <param name="title">Notification title (null is treated as empty).</param>
        /// <param name="message">Notification body (null is treated as empty).</param>
        public void Send(string? title, string? message)
        {
            string safeTitle = title ?? String.Empty;
            string safeMessage = message ?? String.Empty;
            try
            {
                if (_Platform == DesktopPlatformEnum.MacOs) SendMacOs(safeTitle, safeMessage);
                else if (_Platform == DesktopPlatformEnum.Linux) _Runner.Run("notify-send", new List<string> { safeTitle, safeMessage });
                else if (_Platform == DesktopPlatformEnum.Windows) SendWindows(safeTitle, safeMessage);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is System.IO.IOException)
            {
                // Notifications are best effort: a missing or failing notifier never affects the caller.
            }
        }

        /// <summary>
        /// Ring the terminal bell.
        /// </summary>
        public static void Bell()
        {
            Console.Write('\a');
        }

        /// <summary>
        /// The platform this process runs on.
        /// </summary>
        /// <returns>Platform.</returns>
        public static DesktopPlatformEnum CurrentPlatform()
        {
            if (OperatingSystem.IsMacOS()) return DesktopPlatformEnum.MacOs;
            if (OperatingSystem.IsLinux()) return DesktopPlatformEnum.Linux;
            if (OperatingSystem.IsWindows()) return DesktopPlatformEnum.Windows;
            return DesktopPlatformEnum.Other;
        }

        #endregion

        #region Private-Methods

        private void SendMacOs(string title, string message)
        {
            string script = "display notification \"" + EscapeAppleScript(message) + "\" with title \"" + EscapeAppleScript(title) + "\"";
            _Runner.Run("osascript", new List<string> { "-e", script });
        }

        private void SendWindows(string title, string message)
        {
            // Use raw XML toast with explicit binding for reliable rendering
            string escapedTitle = EscapeXml(title);
            string escapedMessage = EscapeXml(message);

            string toastXml = String.IsNullOrEmpty(message)
                ? "<toast><visual><binding template='ToastGeneric'><text>" + escapedTitle + "</text></binding></visual></toast>"
                : "<toast><visual><binding template='ToastGeneric'><text>" + escapedTitle + "</text><text>" + escapedMessage + "</text></binding></visual></toast>";

            string script =
                "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null; " +
                "$xml = [Windows.Data.Xml.Dom.XmlDocument]::new(); " +
                "$xml.LoadXml('" + toastXml.Replace("'", "''") + "'); " +
                "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Armada').Show([Windows.UI.Notifications.ToastNotification]::new($xml))";
            _Runner.Run("powershell", new List<string> { "-NoProfile", "-Command", script });
        }

        private static string EscapeXml(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        /// <summary>
        /// Escape double quotes for AppleScript string literals.
        /// </summary>
        private static string EscapeAppleScript(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        #endregion
    }
}
