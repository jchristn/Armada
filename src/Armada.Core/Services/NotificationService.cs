namespace Armada.Core.Services
{
    using System.Diagnostics;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Sends desktop notifications for mission events.
    /// Cross-platform: macOS (osascript), Linux (notify-send), Windows (PowerShell toast).
    /// </summary>
    public static class NotificationService
    {
        #region Public-Methods

        /// <summary>
        /// Send a desktop notification.
        /// </summary>
        /// <param name="title">Notification title.</param>
        /// <param name="message">Notification body.</param>
        public static void Send(string title, string message)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    SendMacOs(title, message);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    SendLinux(title, message);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    SendWindows(title, message);
                }
            }
            catch
            {
                // Notifications are best-effort; never crash the app
            }
        }

        /// <summary>
        /// Ring the terminal bell.
        /// </summary>
        public static void Bell()
        {
            Console.Write('\a');
        }

        #endregion

        #region Private-Methods

        private static void SendMacOs(string title, string message)
        {
            string script = $"display notification \"{EscapeAppleScript(message)}\" with title \"{EscapeAppleScript(title)}\"";
            RunProcess("osascript", "-e", script);
        }

        private static void SendLinux(string title, string message)
        {
            RunProcess("notify-send", title, message);
        }

        private static void SendWindows(string title, string message)
        {
            // Use raw XML toast with explicit binding for reliable rendering
            string escapedTitle = EscapeXml(title);
            string escapedMessage = EscapeXml(message);

            string toastXml = string.IsNullOrEmpty(message)
                ? $"<toast><visual><binding template='ToastGeneric'><text>{escapedTitle}</text></binding></visual></toast>"
                : $"<toast><visual><binding template='ToastGeneric'><text>{escapedTitle}</text><text>{escapedMessage}</text></binding></visual></toast>";

            string script =
                "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null; " +
                "$xml = [Windows.Data.Xml.Dom.XmlDocument]::new(); " +
                $"$xml.LoadXml('{toastXml.Replace("'", "''")}'); " +
                "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Armada').Show([Windows.UI.Notifications.ToastNotification]::new($xml))";
            RunProcess("powershell", "-NoProfile", "-Command", script);
        }

        private static string EscapeXml(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private static void RunProcess(string command, params string[] arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string arg in arguments)
                startInfo.ArgumentList.Add(arg);

            using Process? process = Process.Start(startInfo);
            process?.WaitForExit(3000);
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
