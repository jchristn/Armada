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
        /// Event raised when the runtime reports a structured provider error. Base runtimes raise it for output lines
        /// that <see cref="TryParseProviderError"/> recognizes as the CLI's own protocol error.
        /// </summary>
        public event Action<int, RuntimeProviderError>? OnProviderError;

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

        /// <summary>
        /// When an agent process exits, how long to wait for its redirected stdout and stderr to be read to the end before
        /// raising <see cref="OnProcessExited"/> and disposing the process, in milliseconds. Default 5000, minimum 0,
        /// maximum 60000. The bound only matters when a child the agent left behind still holds the pipes open.
        /// </summary>
        public static int OutputDrainTimeoutMs
        {
            get => _OutputDrainTimeoutMs;
            set => _OutputDrainTimeoutMs = value < 0 ? 0 : (value > 60000 ? 60000 : value);
        }

        /// <summary>
        /// Optional session token for the scoped Armada MCP connection of an isolated launch (for example a thread-scoped
        /// Ask Armada token). When set and the launch is isolated, the scoped MCP configuration carries the token as an
        /// X-Token header and is written to a per-launch directory that is deleted when the process exits, so concurrent
        /// launches of the same captain never share or leak a token. Default null (no token, captain-scoped directory).
        /// </summary>
        public string? McpSessionToken { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Header = "[BaseAgentRuntime] ";
        private LoggingModule _Logging;
        private static int _GracefulStopTimeoutMs = 10000;
        private static int _OutputDrainTimeoutMs = 5000;

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
            string? perLaunchConfigDirectory = null;
            if (isolateLaunch && mcpPort > 0)
            {
                string scopedConfigDirectory = Path.Combine(Path.GetTempPath(), "armada", "isolation", (captain?.Id ?? Guid.NewGuid().ToString("N")));
                if (!String.IsNullOrEmpty(McpSessionToken))
                {
                    scopedConfigDirectory = Path.Combine(Path.GetTempPath(), "armada", "isolation", "launch-" + Guid.NewGuid().ToString("N"));
                    perLaunchConfigDirectory = scopedConfigDirectory;
                }

                if (!String.IsNullOrEmpty(McpSessionToken))
                {
                    // Thread-scoped (Ask) launch: bind the Armada MCP connection to the session token through each CLI's
                    // per-invocation override, never by redirecting HOME / CODEX_HOME / the config directory, so the
                    // CLI's own login stays visible. The working directory here is the turn's throwaway directory.
                    Armada.Core.Services.CaptainThreadMcpPlanRequest planRequest = new Armada.Core.Services.CaptainThreadMcpPlanRequest
                    {
                        Runtime = RuntimeType,
                        McpPort = mcpPort,
                        ScopedConfigDirectory = scopedConfigDirectory,
                        WorkingDirectory = workingDirectory,
                        SessionToken = McpSessionToken!
                    };
                    PopulateHostMcpConfiguration(planRequest);
                    isolationPlan = Armada.Core.Services.CaptainThreadMcpPlanner.Plan(planRequest);
                }
                else
                {
                    isolationPlan = Armada.Core.Services.CaptainLaunchIsolationPlanner.Plan(RuntimeType, mcpPort, scopedConfigDirectory, McpSessionToken);
                }

                if (!isolationPlan.IsEmpty)
                {
                    foreach (Armada.Core.Services.IsolationConfigFile file in isolationPlan.FilesToWrite)
                    {
                        string baseDirectory = file.RelativeToWorkingDirectory ? workingDirectory : scopedConfigDirectory;
                        string absolutePath = Path.Combine(baseDirectory, file.RelativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
                        File.WriteAllText(absolutePath, file.Contents);
                    }
                    AppendLaunchArguments(args, isolationPlan.ExtraArguments);
                    _Logging.Debug(_Header + (String.IsNullOrEmpty(McpSessionToken) ? "isolated" : "thread-scoped MCP") + " launch for " + RuntimeType + " using scoped config " + scopedConfigDirectory);
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
                // Write the command and its leading arguments on the first line, then the prompt argument (when the
                // prompt is passed as an argument) preserving newlines. The prompt is located by its position in the
                // argument list, not by searching the joined text for a marker.
                string firstFlag = "";
                string promptContent = argsJoined;
                int promptIndex = String.IsNullOrEmpty(prompt) ? -1 : args.IndexOf(prompt);
                if (promptIndex > 0)
                {
                    firstFlag = String.Join(" ", args.GetRange(0, promptIndex)).Trim();
                    promptContent = String.Join(" ", args.GetRange(promptIndex, args.Count - promptIndex));
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

            // Captured once after Start: the Exited handler disposes the Process, after which reading process.Id throws.
            // Handlers used to read process.Id on every line, so a line delivered after the exit (common for the last
            // lines of a short-lived process) threw inside the swallowed try and was silently dropped.
            int launchedPid = 0;
            TaskCompletionSource<bool> readersStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            process.OutputDataReceived += (sender, e) =>
            {
                if (!String.IsNullOrEmpty(e.Data))
                {
                    _Logging.Debug(_Header + "[stdout] " + e.Data);
                    try { logWriter?.WriteLine(e.Data); }
                    catch (ObjectDisposedException) { }

                    try { OnOutputReceived?.Invoke(launchedPid, e.Data); }
                    catch { }

                    // stdout-only consumers (chat, planning) rely on this to exclude stderr banners.
                    try { OnStdoutReceived?.Invoke(launchedPid, e.Data); }
                    catch { }

                    RaiseProviderErrorIfAny(launchedPid, e.Data, true);
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
                    try { OnOutputReceived?.Invoke(launchedPid, e.Data); }
                    catch { }

                    RaiseProviderErrorIfAny(launchedPid, e.Data, false);
                }
            };

            process.Exited += (sender, e) =>
            {
                // Let the async readers deliver the remaining stdout/stderr lines before subscribers learn of the exit and
                // before the process (and its streams) is disposed. A process can exit before StartAsync has begun reading
                // (it raced through its work while the prompt was being written), so first wait for the readers to start;
                // then WaitForExitAsync completes when both redirected streams reach end of file.
                readersStarted.Task.Wait(_OutputDrainTimeoutMs);
                try
                {
                    using (CancellationTokenSource drain = new CancellationTokenSource(_OutputDrainTimeoutMs))
                    {
                        process.WaitForExitAsync(drain.Token).GetAwaiter().GetResult();
                    }
                }
                catch (OperationCanceledException) { }
                catch (InvalidOperationException) { }

                int? code = null;
                int processId = launchedPid;
                if (processId == 0)
                {
                    try { processId = process.Id; } catch { }
                }
                try { code = ((Process?)sender)?.ExitCode; } catch { }
                try { logWriter?.WriteLine("[" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "] Agent exited with code " + (code?.ToString() ?? "unknown")); }
                catch (ObjectDisposedException) { }
                logWriter?.Dispose();

                // Notify subscribers that the process has exited BEFORE disposing.
                // Disposing first invalidates the PID, which can cause the health check
                // to race with the exit handler and trigger spurious recovery.
                try { OnProcessExited?.Invoke(processId, code); }
                catch (Exception ex) { _Logging.Warn(_Header + "error in OnProcessExited handler for process " + processId + ": " + ex.ToString()); }

                // A per-launch scoped config holds a session token; remove it as soon as the process is gone.
                if (perLaunchConfigDirectory != null)
                {
                    try { if (Directory.Exists(perLaunchConfigDirectory)) Directory.Delete(perLaunchConfigDirectory, true); }
                    catch { }
                }

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
            launchedPid = process.Id;

            try { OnProcessStarted?.Invoke(launchedPid); }
            catch (Exception ex) { _Logging.Warn(_Header + "error in OnProcessStarted handler for process " + launchedPid + ": " + ex.ToString()); }

            // Start reading before writing the prompt: output is never missed, and a large prompt cannot deadlock against
            // an agent blocked on a full stdout pipe.
            try
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            finally
            {
                readersStarted.TrySetResult(true);
            }

            try
            {
                if (UsePromptStdin)
                {
                    await process.StandardInput.WriteAsync(prompt).ConfigureAwait(false);
                    await process.StandardInput.FlushAsync().ConfigureAwait(false);
                }

                // Close stdin after writing any prompt content so the agent doesn't block
                // waiting for piped input.
                process.StandardInput.Close();
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException)
            {
                // The agent exited before reading its prompt (broken pipe, or already cleaned up by the exit handler). Its exit is reported through OnProcessExited.
                _Logging.Warn(_Header + "process " + launchedPid + " closed stdin before the prompt was written: " + ex.Message);
            }

            _Logging.Info(_Header + "started process " + launchedPid + " (" + command + ") in " + workingDirectory);

            return launchedPid;
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
        /// Raise <see cref="OnProcessStarted"/> (for derived runtimes that run in-process instead of launching a process).
        /// </summary>
        /// <param name="processId">Process id (synthetic for in-process runtimes).</param>
        protected void RaiseProcessStarted(int processId)
        {
            OnProcessStarted?.Invoke(processId);
        }

        /// <summary>
        /// Raise <see cref="OnOutputReceived"/> and <see cref="OnStdoutReceived"/> for one stdout line (for derived
        /// runtimes that run in-process instead of launching a process).
        /// </summary>
        /// <param name="processId">Process id.</param>
        /// <param name="line">Output line.</param>
        protected void RaiseStdout(int processId, string line)
        {
            OnOutputReceived?.Invoke(processId, line);
            OnStdoutReceived?.Invoke(processId, line);
        }

        /// <summary>
        /// Raise <see cref="OnProcessExited"/> (for derived runtimes that run in-process instead of launching a process).
        /// </summary>
        /// <param name="processId">Process id.</param>
        /// <param name="exitCode">Exit code, or null when unknown.</param>
        protected void RaiseProcessExited(int processId, int? exitCode)
        {
            OnProcessExited?.Invoke(processId, exitCode);
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
            // Non-positive ids are never a real child process. On macOS/Linux, GetProcessById(-1) can
            // succeed because kill(-1, 0) addresses every process the caller may signal.
            if (processId <= 0) return Task.FromResult(false);

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
        /// Recognize a structured provider error in one line of the agent's output. The default recognizes nothing;
        /// a runtime overrides this only for its CLI's own machine-readable error channel (a protocol error line or
        /// a JSON error event), never for keyword matches over free-form output.
        /// </summary>
        /// <param name="line">One output line.</param>
        /// <param name="fromStdout">True for a stdout line, false for stderr.</param>
        /// <returns>The provider error, or null.</returns>
        protected virtual RuntimeProviderError? TryParseProviderError(string line, bool fromStdout)
        {
            return null;
        }

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
        /// Append launch-plan arguments (isolation or thread-scoped MCP) to the runtime's arguments. The default appends
        /// them at the end, which suits every runtime that reads its prompt from stdin; a runtime whose last argument is
        /// a positional prompt overrides this to insert them earlier.
        /// </summary>
        /// <param name="args">The runtime's arguments, modified in place.</param>
        /// <param name="extraArguments">The plan's extra arguments.</param>
        protected virtual void AppendLaunchArguments(List<string> args, List<string> extraArguments)
        {
            foreach (string extraArg in extraArguments)
            {
                args.Add(extraArg);
            }
        }

        /// <summary>
        /// Read (never write) the parts of the host user's own client configuration that a thread-scoped MCP plan needs,
        /// such as existing Armada server entries to disable. The default reads nothing.
        /// </summary>
        /// <param name="request">The plan request to populate.</param>
        protected virtual void PopulateHostMcpConfiguration(Armada.Core.Services.CaptainThreadMcpPlanRequest request)
        {
        }

        /// <summary>
        /// Read a host configuration file for planning, returning null when it is missing or unreadable.
        /// </summary>
        /// <param name="path">Absolute path to the file.</param>
        /// <returns>The file text, or null.</returns>
        protected static string? TryReadHostFile(string? path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void RaiseProviderErrorIfAny(int processId, string line, bool fromStdout)
        {
            RuntimeProviderError? error = null;
            try { error = TryParseProviderError(line, fromStdout); }
            catch (Exception ex) { _Logging.Debug(_Header + "provider error parse failed for process " + processId + ": " + ex.Message); }
            if (error == null) return;

            try { OnProviderError?.Invoke(processId, error); }
            catch (Exception ex) { _Logging.Warn(_Header + "error in OnProviderError handler for process " + processId + ": " + ex.ToString()); }
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
