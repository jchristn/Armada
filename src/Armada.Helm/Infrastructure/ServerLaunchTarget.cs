namespace Armada.Helm.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;

    /// <summary>
    /// How <c>armada server start</c> launches the Admiral: the program to run, the arguments to pass,
    /// and where the server was found. Resolved in order: a native server executable next to the CLI
    /// (from-source installs), <c>Armada.Server.dll</c> next to the CLI assembly run through the dotnet
    /// host (the NuGet global tool), and <c>armada-server</c> on the PATH (the Deb/Rpm, .pkg, and .msi
    /// server packages).
    /// </summary>
    public class ServerLaunchTarget
    {
        #region Public-Members

        /// <summary>
        /// Program to start (the server executable, or the dotnet host for a framework-dependent server).
        /// </summary>
        public string FileName
        {
            get => _FileName;
            set => _FileName = !String.IsNullOrEmpty(value) ? value : throw new ArgumentNullException(nameof(FileName));
        }

        /// <summary>
        /// Arguments passed to <see cref="FileName"/>.
        /// </summary>
        public List<string> Arguments
        {
            get => _Arguments;
            set => _Arguments = value ?? new List<string>();
        }

        /// <summary>
        /// The server binary that will run (the executable or the server assembly), used for messages and
        /// for locating dashboard sources relative to a development build.
        /// </summary>
        public string ServerPath
        {
            get => _ServerPath;
            set => _ServerPath = !String.IsNullOrEmpty(value) ? value : throw new ArgumentNullException(nameof(ServerPath));
        }

        #endregion

        #region Private-Members

        private string _FileName = "Armada.Server";
        private List<string> _Arguments = new List<string>();
        private string _ServerPath = "Armada.Server";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ServerLaunchTarget()
        {
        }

        /// <summary>
        /// A target that runs a native executable directly.
        /// </summary>
        /// <param name="executable">Absolute path to the server executable.</param>
        /// <returns>Launch target.</returns>
        public static ServerLaunchTarget ForExecutable(string executable)
        {
            if (String.IsNullOrEmpty(executable)) throw new ArgumentNullException(nameof(executable));
            return new ServerLaunchTarget { FileName = executable, ServerPath = executable };
        }

        /// <summary>
        /// A target that runs a framework-dependent server assembly through the dotnet host.
        /// </summary>
        /// <param name="assemblyPath">Absolute path to <c>Armada.Server.dll</c>.</param>
        /// <returns>Launch target.</returns>
        public static ServerLaunchTarget ForAssembly(string assemblyPath)
        {
            if (String.IsNullOrEmpty(assemblyPath)) throw new ArgumentNullException(nameof(assemblyPath));
            ServerLaunchTarget target = new ServerLaunchTarget { FileName = ResolveDotnetHost(), ServerPath = assemblyPath };
            target.Arguments.Add(assemblyPath);
            return target;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find a packaged server: a native executable next to the running CLI, then
        /// <c>Armada.Server.dll</c> next to the CLI assembly, then <c>armada-server</c> on the PATH.
        /// Returns null when none exists (the caller may then fall back to a source build).
        /// </summary>
        /// <param name="processDirectory">Directory of the running CLI process, or null.</param>
        /// <param name="assemblyDirectory">Directory holding the CLI assemblies (AppContext.BaseDirectory), or null.</param>
        /// <param name="pathVariable">Value of the PATH environment variable, or null.</param>
        /// <param name="isWindows">Whether to use Windows executable names.</param>
        /// <returns>Launch target, or null.</returns>
        public static ServerLaunchTarget? FindInstalled(string? processDirectory, string? assemblyDirectory, string? pathVariable, bool isWindows)
        {
            string exeName = isWindows ? "Armada.Server.exe" : "Armada.Server";

            // 1. A native executable next to the CLI process (from-source installs publish it there). A tool
            //    package may also carry an apphost, but it is built for whichever OS packed the tool, so the
            //    assembly below is preferred whenever the CLI itself runs on the shared framework.
            if (!String.IsNullOrEmpty(processDirectory))
            {
                string candidate = Path.Combine(processDirectory, exeName);
                string assemblyBeside = Path.Combine(processDirectory, "Armada.Server.dll");
                if (File.Exists(candidate) && !File.Exists(assemblyBeside)) return ForExecutable(candidate);
            }

            // 2. Armada.Server.dll next to the CLI assemblies: the NuGet global tool keeps them in its store
            //    directory, not next to the shim on the PATH, and runs them on the shared framework.
            if (!String.IsNullOrEmpty(assemblyDirectory))
            {
                string assembly = Path.Combine(assemblyDirectory, "Armada.Server.dll");
                if (File.Exists(assembly)) return ForAssembly(assembly);

                string candidate = Path.Combine(assemblyDirectory, exeName);
                if (File.Exists(candidate)) return ForExecutable(candidate);
            }

            // 3. armada-server on the PATH: the server installed by the Deb/Rpm, .pkg, or .msi package.
            if (!String.IsNullOrEmpty(pathVariable))
            {
                string packagedName = isWindows ? "armada-server.exe" : "armada-server";
                foreach (string entry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    string candidate;
                    try
                    {
                        candidate = Path.Combine(entry.Trim().Trim('"'), packagedName);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    if (File.Exists(candidate)) return ForExecutable(candidate);
                }
            }

            return null;
        }

        /// <summary>
        /// Arguments joined into one command line, quoted where needed (for launches that cannot use an
        /// argument list).
        /// </summary>
        /// <returns>Command-line string.</returns>
        public string JoinArguments()
        {
            List<string> parts = new List<string>();
            foreach (string argument in _Arguments)
            {
                if (argument.Length > 0 && argument.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0) parts.Add(argument);
                else parts.Add("\"" + argument.Replace("\"", "\\\"") + "\"");
            }

            return String.Join(" ", parts);
        }

        #endregion

        #region Private-Methods

        private static string ResolveDotnetHost()
        {
            string hostName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";

            // The SDK sets DOTNET_HOST_PATH for tools it launches; DOTNET_ROOT is set by installers and CI.
            string? hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!String.IsNullOrEmpty(hostPath) && File.Exists(hostPath)) return hostPath;

            string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!String.IsNullOrEmpty(dotnetRoot) && File.Exists(Path.Combine(dotnetRoot, hostName))) return Path.Combine(dotnetRoot, hostName);

            // The running shared framework lives in <root>/shared/Microsoft.NETCore.App/<version>/; the host is <root>/dotnet.
            try
            {
                string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
                DirectoryInfo? root = new DirectoryInfo(runtimeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Parent?.Parent?.Parent;
                if (root != null && File.Exists(Path.Combine(root.FullName, hostName))) return Path.Combine(root.FullName, hostName);
            }
            catch (Exception)
            {
                // Fall through to the PATH lookup.
            }

            return hostName;
        }

        #endregion
    }
}
