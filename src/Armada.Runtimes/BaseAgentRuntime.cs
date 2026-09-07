namespace Armada.Runtimes
{
    using System.Diagnostics;
    using System.Text;
    using Armada.Core.Models;
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
        /// Whether this runtime can participate in planning sessions.
        /// The default transcript-relaunch planning flow works for all built-in runtimes.
        /// </summary>
        public virtual bool SupportsPlanningSessions => true;

        /// <summary>
        /// Event raised when the agent writes a line to either stdout or stderr.
        /// </summary>
        public event Action<int, string>? OnOutputReceived;

        /// <summary>
        /// Event raised only when the agent writes a line to stdout (never stderr). Interactive consumers
        /// (chat, planning) subscribe here so CLI stderr banners are excluded from the captured reply.
        /// </summary>
        public event Action<int, string>? OnStdoutReceived;

        /// <summary>
        /// Event raised immediately after the agent process starts and a PID is available.
        /// </summary>
        public event Action<int>? OnProcessStarted;

        /// <summary>
        /// Event raised when the agent process exits.
        /// Parameters: processId, exitCode (null if unavailable).
        /// </summary>
        public event Action<int, int?>? OnProcessExited;

        /// <summary>
        /// Milliseconds to wait for an agent process to exit gracefully (after closing stdin) before it
        /// is force-killed during <see cref="StopAsync"/>. Defaults to 10000 (10s) for production. Test
        /// harnesses lower this so recalling agents does not block on the full graceful window. Clamped to
        /// a non-negative value on set.
        /// </summary>
        public static int GracefulStopTimeoutMs
        {
            get => _GracefulStopTimeoutMs;
            set => _GracefulStopTimeoutMs = value < 0 ? 0 : value;
        }

        #endregion

        #region Private-Members

        private string _Header = "[BaseAgentRuntime] ";
        private LoggingModule _Logging;
        private static int _GracefulStopTimeoutMs = 10000;

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
        /// <param name="finalMessageFilePath">Optional path to write the agent's final response artifact.</param>
        /// <param name="model">Optional model override.</param>
        /// <param name="captain">Optional captain metadata used by runtimes that need persisted runtime-specific options.</param>
        /// <param name="isolateLaunch">When true, launch the captain in a scoped agent configuration that
        /// contains only the Armada MCP server, blocking inheritance of the host user's global settings.</param>
        /// <param name="mcpPort">The Admiral MCP port, used to build the scoped Armada MCP config when isolating.</param>
        /// <param name="showThinking">When true, ask the runtime to surface the model's reasoning for this run
        /// (honored by runtimes with a thinking channel, e.g. Mux).</param>
        /// <param name="token">Cancellation token.</param>
        public virtual async Task<int> StartAsync(
            string workingDirectory,
            string prompt,
            Dictionary<string, string>? environment = null,
            string? logFilePath = null,
            string? finalMessageFilePath = null,
            string? model = null,
            Captain? captain = null,
            bool isolateLaunch = false,
            int mcpPort = 0,
            bool showThinking = false,
            CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(workingDirectory)) throw new ArgumentNullException(nameof(workingDirectory));
            if (String.IsNullOrEmpty(prompt)) throw new ArgumentNullException(nameof(prompt));

            ShowThinking = showThinking;
            string command = GetCommand();
            List<string> args = BuildArguments(workingDirectory, prompt, model, finalMessageFilePath, captain);

            // Plan captain launch isolation (opt-in). An empty plan leaves the launch unchanged, so when
            // isolation is disabled the behavior below is identical to a non-isolated launch.
            Armada.Core.Services.CaptainLaunchIsolationPlan isolationPlan = new Armada.Core.Services.CaptainLaunchIsolationPlan();
            if (isolateLaunch && mcpPort > 0)
            {
                string scopedConfigDirectory = Path.Combine(Path.GetTempPath(), "armada", "isolation", (captain?.Id ?? Guid.NewGuid().ToString("N")));
                isolationPlan = Armada.Core.Services.CaptainLaunchIsolationPlanner.Plan(RuntimeType, mcpPort, scopedConfigDirectory);
                if (!isolationPlan.IsEmpty)
                {
                    foreach (Armada.Core.Services.IsolationConfigFile file in isolationPlan.FilesToWrite)
                    {
                        string absolutePath = Path.Combine(scopedConfigDirectory, file.RelativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
                        File.WriteAllText(absolutePath, file.Contents);
                    }
                    foreach (string extraArg in isolationPlan.ExtraArguments)
                    {
                        args.Add(extraArg);
                    }
                    _Logging.Debug(_Header + "isolated launch for " + RuntimeType + " using scoped config " + scopedConfigDirectory);
                }
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = command,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            // Cross-runtime hardening: captains frequently shell out to `dotnet`, and MSBuild node reuse
            // leaves orphaned build-server processes behind after a mission; disable it at launch. Opt out
            // of dotnet CLI telemetry so it does not add noise to captured captain output. Set before the
            // caller-supplied environment and ApplyEnvironment so a runtime can still override if needed.
            startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

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

            ApplyEnvironment(startInfo, captain);

            // Apply isolation environment overrides last so a scoped HOME/CODEX_HOME/MUX_CONFIG_DIR wins
            // over any inherited or runtime-default value.
            foreach (KeyValuePair<string, string> kvp in isolationPlan.EnvironmentOverrides)
            {
                startInfo.Environment[kvp.Key] = kvp.Value;
            }

            // Set up optional log file writer
            StreamWriter? logWriter = null;
            if (!String.IsNullOrEmpty(logFilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
                logWriter = new StreamWriter(logFilePath, append: true) { AutoFlush = true };
                string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                string argsJoined = String.Join(" ", args);
                // Write command on first line, then prompt content preserving newlines
                string firstFlag = "";
                string promptContent = argsJoined;
                int promptStart = argsJoined.IndexOf("Mission:");
                if (promptStart > 0)
                {
                    firstFlag = argsJoined.Substring(0, promptStart).Trim();
                    promptContent = argsJoined.Substring(promptStart);
                }
                await logWriter.WriteLineAsync("[" + timestamp + "] Agent starting: " + command + " " + firstFlag).ConfigureAwait(false);
                await logWriter.WriteLineAsync(promptContent).ConfigureAwait(false);
                if (UsePromptStdin)
                {
                    // The prompt is delivered on stdin rather than as a CLI argument, so it is not part of
                    // the argument list above; log it explicitly so mission logs still capture the prompt.
                    await logWriter.WriteLineAsync(prompt).ConfigureAwait(false);
                }
                await logWriter.WriteLineAsync("").ConfigureAwait(false);
            }

            Process process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (sender, e) =>
            {
                if (!String.IsNullOrEmpty(e.Data))
                {
                    _Logging.Debug(_Header + "[stdout] " + e.Data);
                    try { logWriter?.WriteLine(e.Data); }
                    catch (ObjectDisposedException) { }

                    try { OnOutputReceived?.Invoke(process.Id, e.Data); }
                    catch { }

                    // stdout-only consumers (chat, planning) rely on this to exclude stderr banners.
                    try { OnStdoutReceived?.Invoke(process.Id, e.Data); }
                    catch { }
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!String.IsNullOrEmpty(e.Data))
                {
                    _Logging.Debug(_Header + "[stderr] " + e.Data);
                    try { logWriter?.WriteLine("[stderr] " + e.Data); }
                    catch (ObjectDisposedException) { }

                    // Treat stderr as runtime output for heartbeat/progress/output capture.
                    // Some agent CLIs emit useful diagnostics or status lines on stderr.
                    try { OnOutputReceived?.Invoke(process.Id, e.Data); }
                    catch { }
                }
            };

            process.Exited += (sender, e) =>
            {
                int? code = null;
                int processId = 0;
                try { processId = process.Id; } catch { }
                try { code = ((Process?)sender)?.ExitCode; } catch { }
                try { logWriter?.WriteLine("[" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "] Agent exited with code " + (code?.ToString() ?? "unknown")); }
                catch (ObjectDisposedException) { }
                logWriter?.Dispose();

                // Notify subscribers that the process has exited BEFORE disposing.
                // Disposing first invalidates the PID, which can cause the health check
                // to race with the exit handler and trigger spurious recovery.
                try { OnProcessExited?.Invoke(processId, code); }
                catch (Exception ex) { _Logging.Warn(_Header + "error in OnProcessExited handler for process " + processId + ": " + ex.ToString()); }

                // Dispose the Process object to release the working directory handle.
                // On Windows, undisposed Process objects hold handles on the WorkingDirectory
                // which prevents dock worktree directories from being deleted.
                try { process.Dispose(); }
                catch { }
            };
            process.EnableRaisingEvents = true;

            bool started = process.Start();
            if (!started)
                throw new InvalidOperationException("Failed to start agent process: " + command);

            try { OnProcessStarted?.Invoke(process.Id); }
            catch (Exception ex) { _Logging.Warn(_Header + "error in OnProcessStarted handler for process " + process.Id + ": " + ex.ToString()); }

            if (UsePromptStdin)
            {
                await process.StandardInput.WriteAsync(prompt).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }

            // Close stdin after writing any prompt content so the agent doesn't block
            // waiting for piped input.
            process.StandardInput.Close();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _Logging.Info(_Header + "started process " + process.Id + " (" + command + ") in " + workingDirectory);

            return process.Id;
        }

        /// <summary>
        /// Stop an agent process gracefully.
        /// </summary>
        public virtual Task StopAsync(int processId, CancellationToken token = default)
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

                    // Wait for graceful exit up to the configured window before force-killing.
                    bool exited = process.WaitForExit(_GracefulStopTimeoutMs);
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
                _Logging.Warn(_Header + "error stopping process " + processId + ": " + ex.ToString());
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// The runtime this adapter drives. Used to plan launch isolation (scoped config / strict MCP).
        /// </summary>
        protected abstract Armada.Core.Enums.AgentRuntimeEnum RuntimeType { get; }

        /// <summary>
        /// Whether the current launch requested the model's reasoning ("thinking") be surfaced. Set at the
        /// start of <see cref="StartAsync"/> and read by runtimes that support a thinking channel.
        /// </summary>
        protected bool ShowThinking { get; private set; }

        /// <summary>
        /// Build runtime-specific command-line arguments.
        /// </summary>
        protected abstract List<string> BuildArguments(
            string workingDirectory,
            string prompt,
            string? model,
            string? finalMessageFilePath,
            Captain? captain);

        /// <summary>
        /// Check if a process is still running.
        /// </summary>
        public virtual Task<bool> IsRunningAsync(int processId, CancellationToken token = default)
        {
            try
            {
                Process process = Process.GetProcessById(processId);
                return Task.FromResult(!process.HasExited);
            }
            catch (ArgumentException)
            {
                return Task.FromResult(false);
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Get the command to execute for this runtime.
        /// </summary>
        protected abstract string GetCommand();

        /// <summary>
        /// Whether the runtime expects the prompt to be written to stdin instead of passed as a CLI argument.
        /// </summary>
        protected virtual bool UsePromptStdin => false;

        /// <summary>
        /// Apply runtime-specific environment variables to the process start info.
        /// </summary>
        /// <param name="startInfo">The process start info being configured.</param>
        /// <param name="captain">The captain being launched, if any (for per-captain env such as reasoning effort).</param>
        protected virtual void ApplyEnvironment(ProcessStartInfo startInfo, Captain? captain)
        {
        }

        /// <summary>
        /// Resolve a PATH-based executable name to a concrete Windows-friendly launcher when needed.
        /// npm-installed CLIs on Windows often expose .cmd wrappers that must be launched directly
        /// when UseShellExecute=false.
        /// </summary>
        protected string ResolveExecutable(string command)
        {
            if (String.IsNullOrEmpty(command)) throw new ArgumentNullException(nameof(command));

            if (!OperatingSystem.IsWindows())
                return command;

            if (command.Contains(Path.DirectorySeparatorChar) || command.Contains(Path.AltDirectorySeparatorChar))
                return command;

            string appDataNpm = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm",
                command + ".cmd");

            if (File.Exists(appDataNpm))
                return appDataNpm;

            return command;
        }

        #endregion
    }
}
