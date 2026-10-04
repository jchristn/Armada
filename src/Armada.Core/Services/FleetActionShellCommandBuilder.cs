namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Builds the <see cref="HostCommandRequest"/> that runs a fleet action command through the platform shell.
    /// <para>
    /// On the Admiral host the command text is fed to the shell on standard input, never as an argument string, so
    /// no quoting is involved and nothing is written to disk: <c>/bin/sh -s</c> on Linux and macOS, and
    /// <c>powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -</c> on Windows. PowerShell reads
    /// standard input line by line, so a multi-line block statement needs a blank line after it, and the process exit
    /// code reflects only whether the last statement succeeded; end the command with <c>exit $LASTEXITCODE</c> to
    /// propagate a native tool's exit code.
    /// </para>
    /// <para>
    /// The Harbor link does not carry standard input, so a command routed to a Harbor is passed as a single
    /// argument instead (<c>/bin/sh -c</c>, or <c>powershell ... -Command</c> on a Windows Harbor). The argument
    /// list is handed to the process without shell interpretation, so this is still free of quoting problems on
    /// Linux and macOS Harbors.
    /// </para>
    /// Thread-safe: the class holds no mutable state.
    /// </summary>
    public static class FleetActionShellCommandBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Build a request that runs <paramref name="commandText"/> on the Admiral host.
        /// </summary>
        /// <param name="commandText">Rendered command text.</param>
        /// <param name="workingDirectory">Working directory.</param>
        /// <param name="timeoutMs">Timeout in milliseconds; 0 disables the timeout.</param>
        /// <returns>Host command request.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the command or working directory is null.</exception>
        public static HostCommandRequest BuildLocal(string commandText, string workingDirectory, int timeoutMs)
        {
            if (commandText == null) throw new ArgumentNullException(nameof(commandText));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            if (OperatingSystem.IsWindows())
            {
                return new HostCommandRequest
                {
                    Executable = "powershell.exe",
                    WorkingDirectory = workingDirectory,
                    Arguments = new List<string> { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", "-" },
                    StandardInput = commandText + Environment.NewLine,
                    TimeoutMs = timeoutMs
                };
            }

            return new HostCommandRequest
            {
                Executable = "/bin/sh",
                WorkingDirectory = workingDirectory,
                Arguments = new List<string> { "-s" },
                StandardInput = commandText + "\n",
                TimeoutMs = timeoutMs
            };
        }

        /// <summary>
        /// Build a request that runs <paramref name="commandText"/> on a Harbor host.
        /// </summary>
        /// <param name="commandText">Rendered command text.</param>
        /// <param name="workingDirectory">Working directory on the Harbor host.</param>
        /// <param name="timeoutMs">Timeout in milliseconds; 0 disables the timeout.</param>
        /// <param name="harborOsPlatform">The Harbor's reported OS description, or null when unknown (treated as Unix).</param>
        /// <returns>Host command request.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the command or working directory is null.</exception>
        public static HostCommandRequest BuildRemote(string commandText, string workingDirectory, int timeoutMs, string? harborOsPlatform)
        {
            if (commandText == null) throw new ArgumentNullException(nameof(commandText));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            if (IsWindowsPlatform(harborOsPlatform))
            {
                return new HostCommandRequest
                {
                    Executable = "powershell.exe",
                    WorkingDirectory = workingDirectory,
                    Arguments = new List<string> { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", commandText },
                    TimeoutMs = timeoutMs
                };
            }

            return new HostCommandRequest
            {
                Executable = "/bin/sh",
                WorkingDirectory = workingDirectory,
                Arguments = new List<string> { "-c", commandText },
                TimeoutMs = timeoutMs
            };
        }

        /// <summary>
        /// Whether an OS description names Windows.
        /// </summary>
        /// <param name="osPlatform">OS description, for example from RuntimeInformation.OSDescription.</param>
        /// <returns>True for Windows.</returns>
        public static bool IsWindowsPlatform(string? osPlatform)
        {
            if (String.IsNullOrWhiteSpace(osPlatform)) return false;
            return osPlatform.IndexOf("windows", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion
    }
}
