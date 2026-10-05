namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Raised when a git or gh process exits with a non-zero exit code. Callers that must distinguish
    /// failures decide from <see cref="ExitCode"/> or from an explicit pre-check, never from the message text.
    /// Derives from <see cref="InvalidOperationException"/> so existing handlers keep working.
    /// </summary>
    public class GitCommandException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// Executable that was launched (for example "git" or "gh").
        /// </summary>
        public string Executable { get; }

        /// <summary>
        /// Arguments passed to the executable.
        /// </summary>
        public IReadOnlyList<string> Arguments { get; }

        /// <summary>
        /// Process exit code.
        /// </summary>
        public int ExitCode { get; }

        /// <summary>
        /// Captured standard output.
        /// </summary>
        public string StandardOutput { get; }

        /// <summary>
        /// Captured standard error.
        /// </summary>
        public string StandardError { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="executable">Executable that was launched.</param>
        /// <param name="arguments">Arguments passed to the executable.</param>
        /// <param name="exitCode">Process exit code.</param>
        /// <param name="standardOutput">Captured standard output.</param>
        /// <param name="standardError">Captured standard error.</param>
        public GitCommandException(string executable, IReadOnlyList<string>? arguments, int exitCode, string? standardOutput, string? standardError)
            : base(BuildMessage(executable, exitCode, standardOutput, standardError))
        {
            Executable = executable ?? String.Empty;
            Arguments = arguments ?? new List<string>();
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? String.Empty;
            StandardError = standardError ?? String.Empty;
        }

        #endregion

        #region Private-Methods

        private static string BuildMessage(string executable, int exitCode, string? standardOutput, string? standardError)
        {
            string detail = OneLine(standardError);
            if (String.IsNullOrEmpty(detail)) detail = OneLine(standardOutput);
            return (executable ?? "process") + " failed (exit " + exitCode + "): " + detail;
        }

        private static string OneLine(string? text)
        {
            // Keep every line, joined, so the message is one log record: git puts the cause ("fatal: ...") after a
            // progress line, and a multi-line message is split across log lines and syslog records.
            string[] lines = (text ?? String.Empty).Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return String.Join(" | ", lines);
        }

        #endregion
    }
}
