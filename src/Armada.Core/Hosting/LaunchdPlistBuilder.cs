namespace Armada.Core.Hosting
{
    using System;
    using System.Security;
    using System.Text;

    /// <summary>
    /// Builds launchd agent property lists. The Admiral agent matches what the .pkg installer writes to
    /// <c>/Library/LaunchAgents</c> (same label, keys, and KeepAlive policy) so the two install paths never disagree.
    /// The Harbor login item is a RunAtLoad agent without KeepAlive, so quitting Harbor from the tray keeps it closed.
    /// </summary>
    public static class LaunchdPlistBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Build the Admiral's launchd agent plist: RunAtLoad, restarted when it exits unsuccessfully, background
        /// process type, limited to the user's GUI (Aqua) session so captains get the user's keychain and logins.
        /// </summary>
        /// <param name="context">Registration context (label, program arguments, working directory).</param>
        /// <returns>Plist XML with LF line endings.</returns>
        /// <exception cref="ArgumentNullException">context is null.</exception>
        public static string BuildServiceAgent(RegistrationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            StringBuilder plist = Header(context);
            plist.Append("    <key>WorkingDirectory</key>\n    <string>").Append(Escape(context.WorkingDirectory)).Append("</string>\n");
            plist.Append("    <key>RunAtLoad</key>\n    <true/>\n");
            plist.Append("    <key>KeepAlive</key>\n    <dict>\n        <key>SuccessfulExit</key>\n        <false/>\n    </dict>\n");
            plist.Append("    <key>ProcessType</key>\n    <string>Background</string>\n");
            plist.Append("    <key>LimitLoadToSessionType</key>\n    <string>Aqua</string>\n");
            plist.Append("</dict>\n</plist>\n");
            return plist.ToString();
        }

        /// <summary>
        /// Build Harbor's login-item agent plist: RunAtLoad once per login, interactive process type, Aqua session only,
        /// no KeepAlive.
        /// </summary>
        /// <param name="context">Registration context (label, program arguments).</param>
        /// <returns>Plist XML with LF line endings.</returns>
        /// <exception cref="ArgumentNullException">context is null.</exception>
        public static string BuildLoginItem(RegistrationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            StringBuilder plist = Header(context);
            plist.Append("    <key>RunAtLoad</key>\n    <true/>\n");
            plist.Append("    <key>ProcessType</key>\n    <string>Interactive</string>\n");
            plist.Append("    <key>LimitLoadToSessionType</key>\n    <string>Aqua</string>\n");
            plist.Append("</dict>\n</plist>\n");
            return plist.ToString();
        }

        /// <summary>
        /// XML-escape a plist string value.
        /// </summary>
        /// <param name="value">Raw value; null is treated as empty.</param>
        /// <returns>Escaped value.</returns>
        public static string Escape(string? value)
        {
            return SecurityElement.Escape(value ?? String.Empty) ?? String.Empty;
        }

        #endregion

        #region Private-Methods

        private static StringBuilder Header(RegistrationContext context)
        {
            StringBuilder plist = new StringBuilder();
            plist.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            plist.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
            plist.Append("<plist version=\"1.0\">\n<dict>\n");
            plist.Append("    <key>Label</key>\n    <string>").Append(Escape(context.Label)).Append("</string>\n");
            plist.Append("    <key>ProgramArguments</key>\n    <array>\n");
            foreach (string argument in context.BuildProgramArguments())
            {
                plist.Append("        <string>").Append(Escape(argument)).Append("</string>\n");
            }
            plist.Append("    </array>\n");
            return plist;
        }

        #endregion
    }
}
