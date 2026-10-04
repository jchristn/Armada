namespace Armada.Tui.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Tui.Services.Credentials;

    /// <summary>
    /// <see cref="IOsNotifier"/> using OSC 9 or OSC 777 escapes (through the terminal) or the platform notifier
    /// (<c>osascript</c> on macOS, <c>notify-send</c> on Linux). Failures are ignored: notifications are best effort.
    /// Thread-safe.
    /// </summary>
    public class OsNotifier : IOsNotifier
    {
        #region Private-Members

        private readonly ITerminalOutput _Terminal;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="terminal">Terminal output.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="terminal"/> is null.</exception>
        public OsNotifier(ITerminalOutput terminal)
        {
            _Terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The OSC sequence for a mode, or null for modes that do not use the terminal.
        /// </summary>
        /// <param name="mode">Mode.</param>
        /// <param name="title">Title.</param>
        /// <param name="message">Message.</param>
        /// <returns>Escape sequence, or null.</returns>
        public static string? Sequence(OsNotificationModeEnum mode, string title, string message)
        {
            string t = Clean(title);
            string m = Clean(message);
            if (mode == OsNotificationModeEnum.Osc9) return "\u001b]9;" + t + ": " + m + "\u0007";
            if (mode == OsNotificationModeEnum.Osc777) return "\u001b]777;notify;" + t + ";" + m + "\u0007";
            return null;
        }

        /// <inheritdoc />
        public void Notify(OsNotificationModeEnum mode, string title, string message)
        {
            if (mode == OsNotificationModeEnum.Off) return;
            string? seq = Sequence(mode, title, message);
            if (seq != null)
            {
                _Terminal.Write(seq);
                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                string script = "display notification \"" + Escape(message) + "\" with title \"" + Escape(title) + "\"";
                _ = Task.Run(() => ProcessRunner.RunAsync("osascript", new List<string> { "-e", script }, null, 5000));
            }
            else if (OperatingSystem.IsLinux() && ProcessRunner.Exists("notify-send"))
            {
                _ = Task.Run(() => ProcessRunner.RunAsync("notify-send", new List<string> { title ?? "", message ?? "" }, null, 5000));
            }
        }

        #endregion

        #region Private-Methods

        private static string Clean(string? text)
        {
            return (text ?? "").Replace("\u001b", "").Replace("\u0007", "").Replace(";", ",").Replace("\n", " ");
        }

        private static string Escape(string? text)
        {
            return (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        #endregion
    }
}
