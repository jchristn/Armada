namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Builds the sc.exe and reg.exe argument lists used on Windows. Each list is passed to the process one argv
    /// element per entry, so values with spaces need no extra shell quoting.
    /// </summary>
    public static class WindowsCommandBuilder
    {
        #region Public-Members

        /// <summary>
        /// Registry key of per-user Run entries.
        /// </summary>
        public const string RunKey = "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Command line the service control manager runs: the quoted executable followed by the run arguments.
        /// </summary>
        /// <param name="context">Registration context.</param>
        /// <returns>The binPath value.</returns>
        /// <exception cref="ArgumentNullException">context is null.</exception>
        public static string BuildCommandLine(RegistrationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            List<string> parts = new List<string>();
            foreach (string argument in context.BuildProgramArguments()) parts.Add(QuoteArgument(argument));
            return String.Join(" ", parts);
        }

        /// <summary>
        /// sc.exe arguments that create (or, with <paramref name="update"/>, reconfigure) the service: automatic
        /// start, display name, and binPath.
        /// </summary>
        /// <param name="context">Registration context.</param>
        /// <param name="update">True to emit "config" for an existing service instead of "create".</param>
        /// <returns>Argument list for sc.exe.</returns>
        /// <exception cref="ArgumentNullException">context is null.</exception>
        public static List<string> BuildServiceCreate(RegistrationContext context, bool update)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return new List<string>
            {
                update ? "config" : "create",
                context.Name,
                "binPath=", BuildCommandLine(context),
                "start=", "auto",
                "DisplayName=", context.DisplayName
            };
        }

        /// <summary>
        /// sc.exe arguments that set the service description.
        /// </summary>
        /// <param name="context">Registration context.</param>
        /// <returns>Argument list for sc.exe.</returns>
        public static List<string> BuildServiceDescription(RegistrationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return new List<string> { "description", context.Name, String.IsNullOrEmpty(context.Description) ? context.DisplayName : context.Description };
        }

        /// <summary>
        /// sc.exe arguments that restart the service after a crash (5 s, 5 s, then 60 s; counter resets daily).
        /// </summary>
        /// <param name="context">Registration context.</param>
        /// <returns>Argument list for sc.exe.</returns>
        public static List<string> BuildServiceFailureActions(RegistrationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return new List<string> { "failure", context.Name, "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/60000" };
        }

        /// <summary>
        /// reg.exe arguments that write the Run value for the current user.
        /// </summary>
        /// <param name="context">Registration context; <see cref="RegistrationContext.DisplayName"/> is the value name.</param>
        /// <returns>Argument list for reg.exe.</returns>
        public static List<string> BuildRunKeyAdd(RegistrationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return new List<string> { "add", RunKey, "/v", context.DisplayName, "/t", "REG_SZ", "/d", BuildCommandLine(context), "/f" };
        }

        /// <summary>
        /// Quote one argument by the Windows command-line rules (CommandLineToArgvW): wrap in quotes when it contains
        /// whitespace or quotes, doubling backslashes that precede a quote.
        /// </summary>
        /// <param name="argument">Raw argument.</param>
        /// <returns>Quoted argument.</returns>
        public static string QuoteArgument(string argument)
        {
            if (argument == null) argument = String.Empty;
            if (argument.Length > 0 && argument.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0) return argument;

            StringBuilder quoted = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    quoted.Append('\\', backslashes * 2 + 1);
                    quoted.Append('"');
                }
                else
                {
                    quoted.Append('\\', backslashes);
                    quoted.Append(c);
                }
                backslashes = 0;
            }
            quoted.Append('\\', backslashes * 2);
            quoted.Append('"');
            return quoted.ToString();
        }

        #endregion
    }
}
