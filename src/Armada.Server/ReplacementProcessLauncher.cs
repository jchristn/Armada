namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using SyslogLogging;
    using Armada.Core.Hosting;
    using ArmadaConstants = Armada.Core.Constants;

    /// <summary>
    /// Launches a replacement Admiral process as a detached child that outlives the current process, tagged
    /// with the current process id via <see cref="ArmadaConstants.RestartWaitPidEnvVar"/> so the replacement
    /// waits for this instance to exit (freeing the listening port) before it binds. Shared by the
    /// server/restart baton and the self-rebuild cutover so both use identical launch semantics. See
    /// docs/SERVER_REBUILD.md. The replacement runs on the same host as this instance: a native server executable
    /// directly, and a framework-dependent server as <c>dotnet &lt;Armada.Server.dll&gt; &lt;original args&gt;</c>
    /// (started with <c>dotnet Armada.Server.dll</c>, <see cref="Environment.ProcessPath"/> is the dotnet host, and
    /// relaunching it bare used to start a <c>dotnet</c> that printed its usage and exited). The command line is
    /// built by pure methods (<see cref="BuildForRunningProcess"/>, <see cref="BuildForExecutable"/>) so it is
    /// testable without starting processes.
    /// </summary>
    public static class ReplacementProcessLauncher
    {
        #region Public-Members

        /// <summary>
        /// File name of the framework-dependent server assembly in a build or slot directory.
        /// </summary>
        public const string ServerAssemblyFileName = "Armada.Server.dll";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Launch a replacement Admiral from the supplied executable, or from its directory's <c>Armada.Server.dll</c>
        /// via the dotnet host when the apphost executable is absent, with this process's arguments (see
        /// <see cref="FilterArguments"/>). The child is handed this process's id so it waits for this instance to exit
        /// before binding.
        /// </summary>
        /// <param name="executablePath">Absolute path to the replacement server executable (typically a slot's
        /// <c>Armada.Server.exe</c>). Required.</param>
        /// <param name="logging">Logging module for diagnostics. Required.</param>
        /// <param name="header">Log header prefix.</param>
        /// <returns>True when the replacement process was started; false otherwise.</returns>
        public static bool Launch(string executablePath, LoggingModule logging, string header)
        {
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            if (String.IsNullOrWhiteSpace(executablePath))
            {
                logging.Warn(header + "no replacement executable path supplied; restart aborted");
                return false;
            }

            ReplacementCommand? command = BuildForServerExecutable(executablePath);
            if (command == null)
            {
                logging.Warn(header + "replacement executable not found at " + executablePath + " and no " + ServerAssemblyFileName + " fallback in " + DirectoryOf(executablePath) + "; restart aborted");
                return false;
            }

            return Launch(command, logging, header);
        }

        /// <summary>
        /// Launch a replacement Admiral with a prepared command line. The child is handed this process's id so it
        /// waits for this instance to exit before binding.
        /// </summary>
        /// <param name="command">Command line. Required.</param>
        /// <param name="logging">Logging module for diagnostics. Required.</param>
        /// <param name="header">Log header prefix.</param>
        /// <returns>True when the replacement process was started; false otherwise.</returns>
        public static bool Launch(ReplacementCommand command, LoggingModule logging, string header)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            ProcessStartInfo startInfo = BuildStartInfo(command);
            startInfo.Environment[ArmadaConstants.RestartWaitPidEnvVar] = Environment.ProcessId.ToString();

            try
            {
                Process? replacement = Process.Start(startInfo);
                if (replacement == null)
                {
                    logging.Warn(header + "Process.Start returned null; restart aborted");
                    return false;
                }

                logging.Info(header + "launched replacement Admiral process " + replacement.Id + ": " + command);
                return true;
            }
            catch (Exception e)
            {
                logging.Warn(header + "failed to launch replacement process (" + command + "): " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// The replacement command for this running process: the arguments it was started with, on the host it runs
        /// on (<see cref="Environment.ProcessPath"/>; for the dotnet host, the entry assembly is passed first).
        /// </summary>
        /// <returns>The command, or null when the server assembly of a dotnet-hosted process cannot be found.</returns>
        public static ReplacementCommand? BuildForCurrentProcess()
        {
            return BuildForRunningProcess(Environment.ProcessPath, EntryAssemblyPath(), Environment.GetCommandLineArgs().Skip(1));
        }

        /// <summary>
        /// The replacement command for a server executable (a slot's apphost, or its <see cref="ServerAssemblyFileName"/>
        /// through the dotnet host), with this process's arguments.
        /// </summary>
        /// <param name="executablePath">Server executable path.</param>
        /// <returns>The command, or null when neither the executable nor the server assembly exists.</returns>
        public static ReplacementCommand? BuildForServerExecutable(string executablePath)
        {
            return BuildForExecutable(executablePath, CurrentArguments(), Environment.ProcessPath, File.Exists);
        }

        /// <summary>
        /// Pure: the replacement command for a running process. A native executable is relaunched directly; the dotnet
        /// host (<c>dotnet</c> or <c>dotnet.exe</c>) is relaunched as <c>&lt;host&gt; &lt;entry assembly&gt; &lt;args&gt;</c>
        /// so it runs the same server instead of a bare <c>dotnet</c>.
        /// </summary>
        /// <param name="processPath">Path of the running program (<see cref="Environment.ProcessPath"/>).</param>
        /// <param name="entryAssemblyPath">Path of the server assembly the host runs (needed for the dotnet host).</param>
        /// <param name="originalArguments">Arguments the process was started with, without the program; filtered by
        /// <see cref="FilterArguments"/>. Passed on only when the entry assembly is the Admiral server
        /// (<see cref="ServerAssemblyFileName"/>): an Admiral embedded in another program (the CLI) is not relaunched
        /// with that program's command line, which could repeat the command.</param>
        /// <returns>The command, or null when <paramref name="processPath"/> is empty or the dotnet host has no
        /// entry assembly.</returns>
        public static ReplacementCommand? BuildForRunningProcess(string? processPath, string? entryAssemblyPath, IEnumerable<string>? originalArguments)
        {
            if (String.IsNullOrWhiteSpace(processPath)) return null;
            List<string> arguments = IsServerAssembly(entryAssemblyPath) ? FilterArguments(originalArguments) : new List<string>();

            if (IsDotnetHost(processPath))
            {
                if (String.IsNullOrWhiteSpace(entryAssemblyPath)) return null;
                ReplacementCommand hosted = new ReplacementCommand();
                hosted.FileName = processPath!;
                hosted.IsDotnetHosted = true;
                hosted.Arguments.Add(entryAssemblyPath!);
                hosted.Arguments.AddRange(arguments);
                hosted.WorkingDirectory = DirectoryOf(entryAssemblyPath!);
                return hosted;
            }

            ReplacementCommand native = new ReplacementCommand();
            native.FileName = processPath!;
            native.Arguments.AddRange(arguments);
            native.WorkingDirectory = DirectoryOf(processPath!);
            return native;
        }

        /// <summary>
        /// Pure: the replacement command for a server executable (a slot's apphost). Runs it directly when it exists,
        /// else runs its directory's <see cref="ServerAssemblyFileName"/> through the dotnet host: the running host when
        /// this process is dotnet-hosted, else <c>dotnet</c> from the PATH.
        /// </summary>
        /// <param name="executablePath">Server executable path.</param>
        /// <param name="arguments">Arguments to pass; filtered by <see cref="FilterArguments"/>.</param>
        /// <param name="currentProcessPath">Path of the running program (<see cref="Environment.ProcessPath"/>).</param>
        /// <param name="fileExists">File existence check.</param>
        /// <returns>The command, or null when neither the executable nor the server assembly exists.</returns>
        public static ReplacementCommand? BuildForExecutable(string executablePath, IEnumerable<string>? arguments, string? currentProcessPath, Func<string, bool> fileExists)
        {
            if (String.IsNullOrWhiteSpace(executablePath)) return null;
            if (fileExists == null) throw new ArgumentNullException(nameof(fileExists));
            List<string> filtered = FilterArguments(arguments);
            string directory = DirectoryOf(executablePath);

            if (fileExists(executablePath))
            {
                ReplacementCommand native = new ReplacementCommand();
                native.FileName = executablePath;
                native.Arguments.AddRange(filtered);
                native.WorkingDirectory = directory;
                return native;
            }

            string assemblyPath = CombineLike(executablePath, directory, ServerAssemblyFileName);
            if (!fileExists(assemblyPath)) return null;

            ReplacementCommand hosted = new ReplacementCommand();
            hosted.FileName = IsDotnetHost(currentProcessPath) ? currentProcessPath! : "dotnet";
            hosted.IsDotnetHosted = true;
            hosted.Arguments.Add(assemblyPath);
            hosted.Arguments.AddRange(filtered);
            hosted.WorkingDirectory = directory;
            return hosted;
        }

        /// <summary>
        /// Pure: true when the path names the dotnet host (<c>dotnet</c> or <c>dotnet.exe</c>, any case, with either
        /// path separator).
        /// </summary>
        /// <param name="processPath">Program path.</param>
        /// <returns>True for the dotnet host.</returns>
        public static bool IsDotnetHost(string? processPath)
        {
            if (String.IsNullOrWhiteSpace(processPath)) return false;
            string name = FileNameOf(processPath!);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            return String.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Pure: the arguments a replacement keeps. <see cref="RegistrationCommandLine.RunServiceFlag"/> is dropped:
        /// the replacement is a detached child, not a process the service manager started (on Windows it would fail
        /// to connect to the service control manager).
        /// </summary>
        /// <param name="arguments">Original arguments, without the program.</param>
        /// <returns>The arguments to pass.</returns>
        public static List<string> FilterArguments(IEnumerable<string>? arguments)
        {
            if (arguments == null) return new List<string>();
            return arguments
                .Where(a => a != null && !String.Equals(a, RegistrationCommandLine.RunServiceFlag, StringComparison.Ordinal))
                .ToList();
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<string> CurrentArguments()
        {
            // GetCommandLineArgs()[0] is the program (the server assembly for both the apphost and the dotnet host);
            // the rest are the arguments the server was started with. Only the Admiral server's own arguments carry over.
            if (!IsServerAssembly(EntryAssemblyPath())) return new List<string>();
            return Environment.GetCommandLineArgs().Skip(1);
        }

        private static string? EntryAssemblyPath()
        {
            string? entryAssembly = Assembly.GetEntryAssembly()?.Location;
            if (String.IsNullOrEmpty(entryAssembly))
            {
                string[] commandLine = Environment.GetCommandLineArgs();
                if (commandLine.Length > 0) entryAssembly = commandLine[0];
            }

            return entryAssembly;
        }

        private static bool IsServerAssembly(string? assemblyPath)
        {
            if (String.IsNullOrWhiteSpace(assemblyPath)) return false;
            return String.Equals(FileNameOf(assemblyPath!), ServerAssemblyFileName, StringComparison.OrdinalIgnoreCase);
        }

        private static ProcessStartInfo BuildStartInfo(ReplacementCommand command)
        {
            // UseShellExecute MUST be false: the caller sets a predecessor-PID environment variable on the
            // start info (so the replacement waits for this instance to exit before binding the port), and
            // .NET throws InvalidOperationException from Process.Start when environment variables are combined
            // with UseShellExecute = true. On Windows a UseShellExecute=false child is still independent of
            // this process's lifetime (it is not placed in a kill-on-close job object), so it survives the
            // handover cleanly. CreateNoWindow keeps the detached Admiral headless on every platform.
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string argument in command.Arguments) startInfo.ArgumentList.Add(argument);
            startInfo.WorkingDirectory = !String.IsNullOrEmpty(command.WorkingDirectory) ? command.WorkingDirectory : Environment.CurrentDirectory;
            return startInfo;
        }

        private static int LastSeparator(string path)
        {
            return Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        }

        private static string FileNameOf(string path)
        {
            int index = LastSeparator(path);
            return index >= 0 ? path.Substring(index + 1) : path;
        }

        private static string DirectoryOf(string path)
        {
            // Separator-agnostic so a Windows path is handled the same on every host (and in tests).
            int index = LastSeparator(path);
            if (index < 0) return String.Empty;
            if (index == 0) return path.Substring(0, 1);
            return path.Substring(0, index);
        }

        private static string CombineLike(string original, string directory, string fileName)
        {
            if (String.IsNullOrEmpty(directory)) return fileName;
            int index = LastSeparator(original);
            char separator = index >= 0 ? original[index] : Path.DirectorySeparatorChar;
            return directory.EndsWith(separator.ToString(), StringComparison.Ordinal) ? directory + fileName : directory + separator + fileName;
        }

        #endregion
    }
}
