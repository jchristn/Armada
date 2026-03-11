namespace Armada.Runtimes
{
    using System.Diagnostics;
    using SyslogLogging;
    using Armada.Runtimes.Interfaces;

    /// <summary>
    /// Base implementation for agent runtimes with common process management.
    /// </summary>
    public abstract class BaseAgentRuntime : IAgentRuntime
    {
        #region Public-Members

        /// <summary>
        /// Runtime display name.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Whether this runtime supports session resume.
        /// </summary>
        public abstract bool SupportsResume { get; }

        /// <summary>
        /// Event raised when the agent writes a line to stdout.
        /// </summary>
        public event Action<int, string>? OnOutputReceived;

        #endregion

        #region Private-Members

        private string _Header = "[BaseAgentRuntime] ";
        private LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        public BaseAgentRuntime(LoggingModule logging)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start an agent process.
        /// </summary>
        /// <param name="workingDirectory">Working directory for the agent.</param>
        /// <param name="prompt">Prompt/instructions for the agent.</param>
        /// <param name="environment">Optional environment variables.</param>
        /// <param name="logFilePath">Optional path to write agent stdout/stderr output.</param>
        /// <param name="token">Cancellation token.</param>
        public virtual async Task<int> StartAsync(
            string workingDirectory,
            string prompt,
            Dictionary<string, string>? environment = null,
            string? logFilePath = null,
            CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(workingDirectory)) throw new ArgumentNullException(nameof(workingDirectory));
            if (String.IsNullOrEmpty(prompt)) throw new ArgumentNullException(nameof(prompt));

            string command = GetCommand();
            List<string> args = BuildArguments(prompt);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = command,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (string arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            if (environment != null)
            {
                foreach (KeyValuePair<string, string> kvp in environment)
                {
                    startInfo.Environment[kvp.Key] = kvp.Value;
                }
            }

            ApplyEnvironment(startInfo);

            // Set up optional log file writer
            StreamWriter? logWriter = null;
            if (!String.IsNullOrEmpty(logFilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
                logWriter = new StreamWriter(logFilePath, append: true) { AutoFlush = true };
                await logWriter.WriteLineAsync("[" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "] Agent starting: " + command + " " + String.Join(" ", args).Replace("\n", " ").Replace("\r", "")).ConfigureAwait(false);
            }

            Process process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (sender, e) =>
            {
                if (!String.IsNullOrEmpty(e.Data))
                {
                    _Logging.Debug(_Header + "[stdout] " + e.Data);
                    logWriter?.WriteLine(e.Data);

                    try { OnOutputReceived?.Invoke(process.Id, e.Data); }
                    catch { }
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!String.IsNullOrEmpty(e.Data))
                {
                    _Logging.Debug(_Header + "[stderr] " + e.Data);
                    logWriter?.WriteLine("[stderr] " + e.Data);
                }
            };

            process.Exited += (sender, e) =>
            {
                int? code = null;
                try { code = ((Process?)sender)?.ExitCode; } catch { }
                logWriter?.WriteLine("[" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "] Agent exited with code " + (code?.ToString() ?? "unknown"));
                logWriter?.Dispose();
            };
            process.EnableRaisingEvents = true;

            bool started = process.Start();
            if (!started)
                throw new InvalidOperationException("Failed to start agent process: " + command);

            // Close stdin immediately so the agent doesn't block waiting for piped input
            process.StandardInput.Close();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _Logging.Info(_Header + "started process " + process.Id + " (" + command + ") in " + workingDirectory);

            return process.Id;
        }

        /// <summary>
        /// Stop an agent process gracefully.
        /// </summary>
        public virtual async Task StopAsync(int processId, CancellationToken token = default)
        {
            try
            {
                Process process = Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    // Try graceful shutdown first by closing stdin
                    try
                    {
                        process.StandardInput.Close();
                    }
                    catch
                    {
                        // stdin may already be closed
                    }

                    // Wait up to 10 seconds for graceful exit
                    bool exited = process.WaitForExit(10000);
                    if (!exited)
                    {
                        _Logging.Warn(_Header + "process " + processId + " did not exit gracefully, killing");
                        process.Kill(entireProcessTree: true);
                    }
                }

                _Logging.Info(_Header + "stopped process " + processId);
            }
            catch (ArgumentException)
            {
                _Logging.Debug(_Header + "process " + processId + " already exited");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error stopping process " + processId + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Check if a process is still running.
        /// </summary>
        public virtual async Task<bool> IsRunningAsync(int processId, CancellationToken token = default)
        {
            try
            {
                Process process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Get the command to execute for this runtime.
        /// </summary>
        protected abstract string GetCommand();

        /// <summary>
        /// Build the argument list for launching the agent with the given prompt.
        /// </summary>
        protected abstract List<string> BuildArguments(string prompt);

        /// <summary>
        /// Apply runtime-specific environment variables to the process start info.
        /// </summary>
        protected virtual void ApplyEnvironment(ProcessStartInfo startInfo)
        {
        }

        #endregion
    }
}
