namespace Armada.Server
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using ArmadaConstants = Armada.Core.Constants;
    using SyslogLogging;

    /// <summary>
    /// Launches a replacement Admiral process as a detached child that outlives the current process, tagged
    /// with the current process id via <see cref="ArmadaConstants.RestartWaitPidEnvVar"/> so the replacement
    /// waits for this instance to exit (freeing the listening port) before it binds. Shared by the
    /// server/restart baton and the self-rebuild cutover so both use identical launch semantics. See
    /// docs/SERVER_REBUILD.md.
    /// </summary>
    public static class ReplacementProcessLauncher
    {
        #region Public-Methods

        /// <summary>
        /// Launch a replacement Admiral from the supplied executable, or from a slot's <c>Armada.Server.dll</c>
        /// via <c>dotnet</c> when the apphost executable is absent. The child is handed this process's id so it
        /// waits for this instance to exit before binding.
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

            string workingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory;

            // Prefer the published apphost executable; fall back to "dotnet <slot>/Armada.Server.dll" when the
            // apphost is missing (e.g. a framework-dependent publish without an apphost on this platform).
            ProcessStartInfo startInfo;
            if (File.Exists(executablePath))
            {
                startInfo = BuildStartInfo(executablePath, null);
            }
            else
            {
                string dllPath = Path.Combine(workingDirectory, "Armada.Server.dll");
                if (!File.Exists(dllPath))
                {
                    logging.Warn(header + "replacement executable not found at " + executablePath + " and no Armada.Server.dll fallback in " + workingDirectory + "; restart aborted");
                    return false;
                }
                startInfo = BuildStartInfo("dotnet", dllPath);
            }

            startInfo.WorkingDirectory = workingDirectory;
            startInfo.Environment[ArmadaConstants.RestartWaitPidEnvVar] = Environment.ProcessId.ToString();

            try
            {
                Process? replacement = Process.Start(startInfo);
                if (replacement == null)
                {
                    logging.Warn(header + "Process.Start returned null; restart aborted");
                    return false;
                }

                logging.Debug(header + "launched replacement Admiral process " + replacement.Id + " from " + executablePath);
                return true;
            }
            catch (Exception e)
            {
                logging.Warn(header + "failed to launch replacement process: " + e.Message);
                return false;
            }
        }

        #endregion

        #region Private-Methods

        private static ProcessStartInfo BuildStartInfo(string fileName, string? firstArgument)
        {
            ProcessStartInfo startInfo;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized
                };
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }

            if (!String.IsNullOrEmpty(firstArgument)) startInfo.ArgumentList.Add(firstArgument);
            return startInfo;
        }

        #endregion
    }
}
