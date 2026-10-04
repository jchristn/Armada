namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Builds an XDG autostart desktop entry (freedesktop.org Desktop Entry Specification 1.5) for Harbor on Linux.
    /// </summary>
    public static class XdgAutostartBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Build the desktop entry.
        /// </summary>
        /// <param name="context">Registration context (display name, description, program arguments).</param>
        /// <returns>Desktop entry contents with LF line endings.</returns>
        /// <exception cref="ArgumentNullException">context is null.</exception>
        public static string Build(RegistrationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            List<string> exec = new List<string>();
            foreach (string argument in context.BuildProgramArguments()) exec.Add(QuoteExecArgument(argument));

            StringBuilder entry = new StringBuilder();
            entry.Append("[Desktop Entry]\n");
            entry.Append("Type=Application\n");
            entry.Append("Version=1.5\n");
            entry.Append("Name=").Append(EscapeString(context.DisplayName)).Append('\n');
            if (!String.IsNullOrEmpty(context.Description)) entry.Append("Comment=").Append(EscapeString(context.Description)).Append('\n');
            entry.Append("Exec=").Append(EscapeString(String.Join(" ", exec))).Append('\n');
            entry.Append("Terminal=false\n");
            entry.Append("X-GNOME-Autostart-enabled=true\n");
            return entry.ToString();
        }

        /// <summary>
        /// Quote one Exec argument per the spec: arguments with reserved characters are wrapped in double quotes, and
        /// inside quotes the characters ", `, $, and \ are backslash-escaped; '%' is doubled because it starts a field code.
        /// </summary>
        /// <param name="argument">Raw argument.</param>
        /// <returns>Quoted argument (before string-level escaping).</returns>
        public static string QuoteExecArgument(string argument)
        {
            if (argument == null) argument = String.Empty;
            string value = argument.Replace("%", "%%");
            bool needsQuotes = value.Length == 0;
            foreach (char c in value)
            {
                if (" \t\n\"'\\><~|&;$*?#()`".IndexOf(c) >= 0)
                {
                    needsQuotes = true;
                    break;
                }
            }
            if (!needsQuotes) return value;

            StringBuilder quoted = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '"' || c == '`' || c == '$' || c == '\\') quoted.Append('\\');
                quoted.Append(c);
            }
            quoted.Append('"');
            return quoted.ToString();
        }

        #endregion

        #region Private-Methods

        private static string EscapeString(string value)
        {
            // String-level escapes of the spec: a literal backslash is written as \\, control characters as \n \t \r.
            return (value ?? String.Empty).Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r");
        }

        #endregion
    }
}
