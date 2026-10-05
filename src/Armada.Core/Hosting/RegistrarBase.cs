namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Shared plumbing for <see cref="ServiceRegistrar"/> and <see cref="StartupRegistrar"/>: printing, running or
    /// describing commands, and writing definition files only when their content changed. Not thread safe; use one
    /// instance per command-line invocation.
    /// </summary>
    public abstract class RegistrarBase
    {
        #region Public-Members

        /// <summary>
        /// Registration context.
        /// </summary>
        public RegistrationContext Context { get; }

        #endregion

        #region Private-Members

        private readonly IRegistrationCommandRunner _Runner;
        private readonly TextWriter _Output;
        private string _Prefix = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Registration context.</param>
        /// <param name="runner">Command runner.</param>
        /// <param name="output">Where progress lines are written (the console for the real flags).</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        protected RegistrarBase(RegistrationContext context, IRegistrationCommandRunner runner, TextWriter output)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            _Runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _Output = output ?? throw new ArgumentNullException(nameof(output));
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Set the prefix printed before every line, for example "install-service".
        /// </summary>
        /// <param name="flagName">Flag name without dashes.</param>
        protected void SetPrefix(string flagName)
        {
            _Prefix = "[" + flagName + "] ";
        }

        /// <summary>
        /// Print one progress line.
        /// </summary>
        /// <param name="message">Message.</param>
        protected void Print(string message)
        {
            _Output.WriteLine(_Prefix + message);
        }

        /// <summary>
        /// Print a block (definition file contents) indented under the prefix.
        /// </summary>
        /// <param name="content">Block text.</param>
        protected void PrintBlock(string content)
        {
            string[] lines = (content ?? String.Empty).Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            foreach (string line in lines) _Output.WriteLine("    " + line);
        }

        /// <summary>
        /// Run a command, or in dry-run mode print it without running.
        /// </summary>
        /// <param name="fileName">Executable.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="windowsQuoting">Format the printed command with Windows quoting rules.</param>
        /// <returns>The result; a successful empty result in dry-run mode.</returns>
        protected CommandResult Execute(string fileName, List<string> arguments, bool windowsQuoting)
        {
            string display = FormatCommand(fileName, arguments, windowsQuoting);
            if (Context.DryRun)
            {
                Print("would run: " + display);
                return new CommandResult();
            }

            CommandResult result = _Runner.Run(fileName, arguments);
            Print("ran: " + display + (result.Succeeded ? String.Empty : " (exit " + result.ExitCode + ")"));
            return result;
        }

        /// <summary>
        /// Run a read-only probe (for example "sc.exe query" or "launchctl print"). Probes change nothing, so they
        /// also run in dry-run mode, which lets a dry run describe exactly what a real run would do.
        /// </summary>
        /// <param name="fileName">Executable.</param>
        /// <param name="arguments">Arguments.</param>
        /// <returns>The probe result.</returns>
        protected CommandResult Probe(string fileName, List<string> arguments)
        {
            return _Runner.Run(fileName, arguments);
        }

        /// <summary>
        /// Write a definition file when its content differs from what is on disk. In dry-run mode, print it instead.
        /// </summary>
        /// <param name="path">Target path.</param>
        /// <param name="content">Desired content.</param>
        /// <param name="changed">True when the file was (or would be) created or rewritten.</param>
        /// <returns>True on success; false when the write failed (the reason is printed).</returns>
        protected bool WriteDefinition(string path, string content, out bool changed)
        {
            string? existing = null;
            try
            {
                if (File.Exists(path)) existing = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                existing = null;
            }

            changed = !String.Equals(existing, content, StringComparison.Ordinal);

            if (Context.DryRun)
            {
                Print((changed ? "would write " : "already current, would leave ") + path + ":");
                PrintBlock(content);
                return true;
            }

            if (!changed)
            {
                Print("definition unchanged: " + path);
                return true;
            }

            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, content, new UTF8Encoding(false));
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                }
                Print((existing == null ? "wrote " : "updated ") + path);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Print("ERROR: could not write " + path + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Delete a definition file. In dry-run mode, print what would be removed.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>True on success.</returns>
        protected bool DeleteDefinition(string path)
        {
            if (Context.DryRun)
            {
                Print("would remove " + path);
                return true;
            }

            try
            {
                File.Delete(path);
                Print("removed " + path);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Print("ERROR: could not remove " + path + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Format a command for display.
        /// </summary>
        /// <param name="fileName">Executable.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="windowsQuoting">Use Windows quoting rules instead of POSIX shell quoting.</param>
        /// <returns>Display string.</returns>
        protected static string FormatCommand(string fileName, List<string> arguments, bool windowsQuoting)
        {
            StringBuilder sb = new StringBuilder(fileName);
            foreach (string argument in arguments)
            {
                sb.Append(' ');
                sb.Append(windowsQuoting ? WindowsCommandBuilder.QuoteArgument(argument) : ShellQuote(argument));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Print an error for a failed command and return <see cref="RegistrationExitCode.Failed"/>.
        /// </summary>
        /// <param name="what">What failed.</param>
        /// <param name="result">Command result.</param>
        /// <returns><see cref="RegistrationExitCode.Failed"/>.</returns>
        protected int Fail(string what, CommandResult result)
        {
            Print("ERROR: " + what + ": " + FirstLine(result));
            return RegistrationExitCode.Failed;
        }

        /// <summary>
        /// First line of a command's error output (or standard output when stderr is empty).
        /// </summary>
        /// <param name="result">Command result.</param>
        /// <returns>One line of text.</returns>
        protected static string FirstLine(CommandResult result)
        {
            string text = !String.IsNullOrWhiteSpace(result.StandardError) ? result.StandardError : result.StandardOutput;
            text = text.Trim();
            int newline = text.IndexOf('\n');
            if (newline >= 0) text = text.Substring(0, newline).Trim();
            return String.IsNullOrEmpty(text) ? "exit code " + result.ExitCode : text;
        }

        private static string ShellQuote(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(" \t\n\"'\\$`;&|<>()*?#~".ToCharArray()) < 0) return argument;
            return "'" + argument.Replace("'", "'\\''") + "'";
        }

        #endregion
    }
}
