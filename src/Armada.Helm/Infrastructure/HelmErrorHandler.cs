namespace Armada.Helm.Infrastructure
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text.RegularExpressions;
    using Spectre.Console;
    using Spectre.Console.Cli;

    /// <summary>
    /// Renders an exception that escaped a CLI command. Targeting errors (<see cref="AdmiralTargetException"/>) and
    /// 401/403 responses get a one-line explanation on stderr; anything else is rendered the way Spectre.Console.Cli
    /// renders it by default.
    /// </summary>
    public static class HelmErrorHandler
    {
        #region Public-Members

        /// <summary>
        /// Exit code for a targeting error (refused local-only command, bad URL or profile, unreachable Admiral,
        /// rejected credential).
        /// </summary>
        public const int TargetErrorExitCode = 2;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Handle an exception.
        /// </summary>
        /// <param name="exception">Exception.</param>
        /// <param name="resolver">Type resolver (unused).</param>
        /// <returns>Process exit code.</returns>
        public static int Handle(Exception exception, ITypeResolver? resolver)
        {
            return Handle(exception, Console.Error);
        }

        /// <summary>
        /// Handle an exception, writing targeting errors to <paramref name="error"/>.
        /// </summary>
        /// <param name="exception">Exception.</param>
        /// <param name="error">Error writer.</param>
        /// <returns>Process exit code.</returns>
        public static int Handle(Exception exception, TextWriter error)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            if (error == null) throw new ArgumentNullException(nameof(error));

            Exception inner = Unwrap(exception);
            if (inner is AdmiralTargetException target)
            {
                error.WriteLine("Error: " + target.Message);
                return TargetErrorExitCode;
            }

            if (inner is HttpRequestException http && (http.StatusCode == HttpStatusCode.Unauthorized || http.StatusCode == HttpStatusCode.Forbidden))
            {
                string hint = http.StatusCode == HttpStatusCode.Unauthorized
                    ? "The Admiral requires a valid credential: pass --token <bearer>, set " + AdmiralTargetResolver.TokenEnvironmentVariable + ", or store one with 'armada profile add'."
                    : "The credential is valid but its user may not do this.";
                error.WriteLine("Error: " + http.Message + Environment.NewLine + hint);
                return TargetErrorExitCode;
            }

            string? conversion = DescribeConversionFailure(exception.Message);
            if (conversion != null)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] " + Markup.Escape(conversion));
                return -1;
            }

            if (exception is CommandAppException app && app.Pretty != null)
            {
                AnsiConsole.Write(app.Pretty);
                return -1;
            }

            AnsiConsole.MarkupLine("[red]Error:[/] " + Markup.Escape(exception.Message));
            return -1;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Spectre reports a bad option value as "Failed to convert 'abc' to Nullable`1." which names a CLR type,
        /// not what the user should type. Rewrite it in plain words; return null for any other message.
        /// </summary>
        private static string? DescribeConversionFailure(string? message)
        {
            if (String.IsNullOrEmpty(message)) return null;
            Match match = Regex.Match(message, "^Failed to convert '(?<value>.*)' to (?<type>[^ ]+)\\.$", RegexOptions.Singleline);
            if (!match.Success) return null;

            string value = match.Groups["value"].Value;
            string type = match.Groups["type"].Value;
            string expected;
            if (type.StartsWith("Int", StringComparison.Ordinal) || type.StartsWith("UInt", StringComparison.Ordinal) || type.StartsWith("Nullable", StringComparison.Ordinal))
                expected = "a number";
            else if (type == "Double" || type == "Single" || type == "Decimal")
                expected = "a number";
            else if (type == "Boolean")
                expected = "true or false";
            else if (type == "DateTime" || type == "DateTimeOffset")
                expected = "a date such as 2026-01-31";
            else
                expected = "a valid " + type + " value";

            return "Invalid value '" + value + "': expected " + expected + ". Run the command with --help to see each option's format.";
        }

        private static Exception Unwrap(Exception exception)
        {
            Exception current = exception;
            while ((current is CommandRuntimeException || current is AggregateException) && current.InnerException != null) current = current.InnerException;
            return current;
        }

        #endregion
    }
}
