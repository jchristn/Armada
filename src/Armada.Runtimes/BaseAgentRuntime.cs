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
    public abstract class BaseAgentRuntime : IAgentRuntime, IRawStdoutSource
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
        /// Whether this runtime can participate in planning sessions. Defaults to
        /// <see cref="Armada.Core.Services.AgentRuntimeCapabilities.SupportsPlanningSessions"/> for the runtime type, the
        /// same answer the captain model reports.
        /// </summary>
        public virtual bool SupportsPlanningSessions => Armada.Core.Services.AgentRuntimeCapabilities.SupportsPlanningSessions(RuntimeType);

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
        /// Event raised for each non-empty stdout line exactly as the CLI wrote it, before any structured decoding (see
        /// <see cref="IRawStdoutSource"/>). Without structured decoding it carries the same lines as
        /// <see cref="OnStdoutReceived"/>.
        /// </summary>
        public event Action<int, string>? OnRawStdoutReceived;

        /// <summary>
        /// Event raised only when the agent writes a line to stderr (every such line is also raised on
        /// <see cref="OnOutputReceived"/>). A Harbor uses it to report stdout and stderr on separate streams.
        /// </summary>
        public event Action<int, string>? OnStderrReceived;

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
        /// Event raised for each activity the captain reports in its structured output (a tool call it started, text it
        /// wrote, or reasoning) while <see cref="StructuredProgress"/> is in effect.
        /// </summary>
        public event Action<int, RuntimeActivity>? OnActivity;

        /// <summary>
        /// When true, a mission launch runs the CLI with its structured output (Claude Code stream-json, Codex exec
        /// --json) so its progress is visible while it runs. Each structured stdout line is decoded
        /// (<see cref="Armada.Core.Services.MissionStreamDecoder"/>): the readable output the CLI prints in text mode (the
        /// final reply, protocol lines) is raised on <see cref="OnOutputReceived"/> and <see cref="OnStdoutReceived"/> and
        /// written to the log as before, and each activity is raised on <see cref="OnActivity"/> and written to the log as
        /// "&gt; " and its summary. Ignored by runtimes without structured output and by an interactive launch that
        /// already streams JSON (chat). Default false.
        /// </summary>
        public bool StructuredProgress { get; set; } = false;

        /// <summary>
        /// When true together with <see cref="StructuredProgress"/>, the CLI still runs with its structured mission output
        /// (Claude Code stream-json without partial messages, Codex exec --json, Mux jsonl), but the runtime does not
        /// decode it: every stdout line is raised unchanged on <see cref="OnOutputReceived"/> and
        /// <see cref="OnStdoutReceived"/>, so a consumer elsewhere (the Admiral, for a mission run on a Harbor) decodes
        /// the raw events itself. Default false.
        /// </summary>
        public bool StructuredPassthrough { get; set; } = false;

        /// <summary>
        /// Milliseconds to wait for an agent process to exit gracefully (after SIGTERM on Unix) before it
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

        /// <summary>
        /// When true together with <see cref="McpSessionToken"/>, a token launch uses the full launch isolation plan (the
        /// same scoped configuration as <c>IsolateCaptainLaunch</c>, with the token as an X-Token header) instead of the
        /// per-invocation binding used for Ask turns. Default false.
        /// </summary>
        public bool McpTokenWithFullIsolation { get; set; } = false;

        /// <summary>
        /// Whether a per-invocation token binding may write client configuration into the working directory (Gemini,
        /// Cursor). Default true (Ask turns run in a throwaway directory); mission launches set false because their
        /// working directory is a repository worktree.
        /// </summary>
        public bool McpAllowWorkingDirectoryFiles { get; set; } = true;

        /// <summary>
        /// Host placed in the Armada MCP URLs written for an isolated or thread-scoped launch. The Admiral's MCP listener
        /// only answers requests whose Host matches the name it was bound with, so the caller sets this from
        /// <see cref="Armada.Core.Services.ArmadaMcpConfigBuilder.ClientHostFor"/> of the configured REST hostname.
        /// Default "localhost"; null or empty also means localhost.
        /// </summary>
        public string McpHost
        {
            get => _McpHost;
            set => _McpHost = String.IsNullOrWhiteSpace(value) ? Armada.Core.Services.ArmadaMcpConfigBuilder.DefaultHost : value;
        }

        #endregion

        #region Private-Members

        private string _McpHost = Armada.Core.Services.ArmadaMcpConfigBuilder.DefaultHost;

        private string _Header = "[BaseAgentRuntime] ";
        private LoggingModule _Logging;
        private static int _GracefulStopTimeoutMs = 10000;
        private static int _OutputDrainTimeoutMs = 5000;

        // UTF-8 without a byte order mark for the agent's stdin. Encoding.UTF8 emits a BOM preamble, and Process.Start
        // builds the stdin writer with AutoFlush, whose setter flushes immediately: the BOM was written to the pipe
        // inside Process.Start (an agent that had already exited made Start throw "Broken pipe", escaping the guarded
        // prompt write), and every prompt the agent read began with a stray U+FEFF.
        private static readonly UTF8Encoding _StdinEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // Process start failures meaning "not found": ENOENT (2) on Unix; ERROR_FILE_NOT_FOUND (2), ERROR_PATH_NOT_FOUND (3)
        // and ERROR_DIRECTORY (267, an invalid working directory) on Windows.
        private static readonly int[] _NotFoundErrors = new int[] { 2, 3, 267 };

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
            BeginLaunch(finalMessageFilePath);
            string command = GetCommand();
            List<string> args = BuildArguments(workingDirectory, prompt, model, finalMessageFilePath, captain);

            // Plan captain launch isolation (opt-in). An empty plan leaves the launch unchanged, so when
            // isolation is disabled the behavior below is identical to a non-isolated launch.
            Armada.Core.Services.CaptainLaunchIsolationPlan isolationPlan = new Armada.Core.Services.CaptainLaunchIsolationPlan();
            string? perLaunchConfigDirectory = null;
            List<Armada.Core.Services.IsolationConfigFile> removeAfterExit = new List<Armada.Core.Services.IsolationConfigFile>();
            if (isolateLaunch && mcpPort > 0)
            {
                string scopedConfigDirectory = Path.Combine(Path.GetTempPath(), "armada", "isolation", (captain?.Id ?? Guid.NewGuid().ToString("N")));
                if (!String.IsNullOrEmpty(McpSessionToken))
                {
                    scopedConfigDirectory = Path.Combine(Path.GetTempPath(), "armada", "isolation", "launch-" + Guid.NewGuid().ToString("N"));
                    perLaunchConfigDirectory = scopedConfigDirectory;
                }

                // A full-isolation token launch falls back to the per-invocation binding for a runtime whose isolation
                // plan cannot carry the token (OpenCode), so the captain is never launched without its token.
                bool tokenNeedsInvocationBinding = !McpTokenWithFullIsolation
                    || !Armada.Core.Services.CaptainLaunchIsolationPlanner.CarriesSessionToken(RuntimeType);
                if (!String.IsNullOrEmpty(McpSessionToken) && tokenNeedsInvocationBinding)
                {
                    // Thread-scoped (Ask) or mission-scoped launch: bind the Armada MCP connection to the session token through each CLI's
                    // per-invocation override, never by redirecting HOME / CODEX_HOME / the config directory, so the
                    // CLI's own login stays visible. The working directory here is the turn's throwaway directory.
                    Armada.Core.Services.CaptainThreadMcpPlanRequest planRequest = new Armada.Core.Services.CaptainThreadMcpPlanRequest
                    {
                        Runtime = RuntimeType,
                        McpPort = mcpPort,
                        McpHost = McpHost,
                        ScopedConfigDirectory = scopedConfigDirectory,
                        WorkingDirectory = workingDirectory,
                        SessionToken = McpSessionToken!,
                        AllowWorkingDirectoryFiles = McpAllowWorkingDirectoryFiles
                    };
                    PopulateHostMcpConfiguration(planRequest);
                    isolationPlan = Armada.Core.Services.CaptainThreadMcpPlanner.Plan(planRequest);
                }
                else
                {
                    isolationPlan = Armada.Core.Services.CaptainLaunchIsolationPlanner.Plan(RuntimeType, mcpPort, scopedConfigDirectory, McpSessionToken, McpHost);
                }

                if (!isolationPlan.IsEmpty)
                {
                    foreach (Armada.Core.Services.IsolationConfigFile file in isolationPlan.FilesToWrite)
                    {
                        string baseDirectory = file.RelativeToWorkingDirectory ? workingDirectory : scopedConfigDirectory;
                        string absolutePath = Path.Combine(baseDirectory, file.RelativePath);
                        if (file.RelativeToWorkingDirectory && file.ExcludeFromGit)
                            await ExcludeFromGitAsync(workingDirectory, file.RelativePath, token).ConfigureAwait(false);
                        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
                        File.WriteAllText(absolutePath, file.Contents);
                        if (file.RelativeToWorkingDirectory && file.RemoveAfterExit) removeAfterExit.Add(file);
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
                StandardInputEncoding = _StdinEncoding,
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

            // Mission progress: decode structured stdout into the readable output text mode prints plus activities.
            // A passthrough launch keeps the structured lines raw for a consumer that decodes them itself.
            Armada.Core.Services.MissionStreamDecoder? decoder = StructuredProgressActive && !StructuredPassthrough ? Armada.Core.Services.MissionStreamDecoder.For(RuntimeType) : null;
            object decoderLock = new object();

            // Captured once after Start: the Exited handler disposes the Process, after which reading process.Id throws.
            // Handlers used to read process.Id on every line, so a line delivered after the exit (common for the last
            // lines of a short-lived process) threw inside the swallowed try and was silently dropped.
            int launchedPid = 0;
            TaskCompletionSource<bool> readersStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            process.OutputDataReceived += (sender, e) =>
            {
                if (!String.IsNullOrEmpty(e.Data))
                {
                    try { OnRawStdoutReceived?.Invoke(launchedPid, e.Data); }
                    catch { }
                }

                if (!String.IsNullOrEmpty(e.Data) && decoder != null)
                {
                    _Logging.Debug(_Header + "[stdout] " + e.Data);
                    RaiseProviderErrorIfAny(launchedPid, e.Data, true);
                    Armada.Core.Services.MissionStreamDecodeResult decoded;
                    lock (decoderLock) decoded = decoder.Decode(e.Data, DateTime.UtcNow);
                    if (decoded.Structured)
                    {
                        RaiseDecoded(launchedPid, decoded, logWriter);
                        return;
                    }

                    // Plain text in a structured stream (a CLI warning): handled as text-mode output below, without
                    // parsing it for a provider error a second time.
                    RaisePlainStdout(launchedPid, e.Data, logWriter);
                    return;
                }

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

                    try { OnStderrReceived?.Invoke(launchedPid, e.Data); }
                    catch { }

                    RaiseProviderErrorIfAny(launchedPid, e.Data, false);
                }
            };

            // Async so draining output after an exit never blocks a thread-pool thread; the outer catch keeps a fault
            // here from escaping an async event handler and taking down the process.
            process.Exited += async (sender, e) =>
            {
                try
                {
                    // Let the async readers deliver the remaining stdout/stderr lines before subscribers learn of the exit and
                    // before the process (and its streams) is disposed. A process can exit before StartAsync has begun reading
                    // (it raced through its work while the prompt was being written), so first wait for the readers to start;
                    // then WaitForExitAsync completes when both redirected streams reach end of file.
                    await Task.WhenAny(readersStarted.Task, Task.Delay(_OutputDrainTimeoutMs)).ConfigureAwait(false);
                    try
                    {
                        using (CancellationTokenSource drain = new CancellationTokenSource(_OutputDrainTimeoutMs))
                        {
                            await process.WaitForExitAsync(drain.Token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (InvalidOperationException) { }

                    // Output a structured stream still held (a Codex message whose turn never completed).
                    if (decoder != null)
                    {
                        Armada.Core.Services.MissionStreamDecodeResult rest;
                        lock (decoderLock) rest = decoder.Flush(DateTime.UtcNow);
                        RaiseDecoded(launchedPid, rest, logWriter);
                    }

                    int? code = null;
                    int processId = launchedPid;
                    if (processId == 0)
                    {
                        try { processId = process.Id; } catch { }
                    }
                    try { code = ((Process?)sender)?.ExitCode; } catch { }

                    // The runtime decides what its CLI's exit means (Mux exits non-zero after a recoverable error even when its
                    // run completed) and may supply the final message the CLI skipped.
                    int? reportedCode = code;
                    try { reportedCode = ResolveExitCode(code); }
                    catch (Exception ex) { _Logging.Warn(_Header + "exit code resolution failed for process " + processId + ": " + ex.Message); }

                    string exitNote = "Agent exited with code " + (code?.ToString() ?? "unknown");
                    if (reportedCode != code)
                        exitNote += " (its run completed; reported as " + (reportedCode?.ToString() ?? "unknown") + ")";
                    try { logWriter?.WriteLine("[" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "] " + exitNote); }
                    catch (ObjectDisposedException) { }
                    logWriter?.Dispose();

                    // Launch files written into the working directory (pi's .pi/mcp.json) go before the exit is reported, so
                    // the dock's git status is clean when completion and landing look at it.
                    RemoveLaunchFiles(workingDirectory, removeAfterExit);

                    // Notify subscribers that the process has exited BEFORE disposing.
                    // Disposing first invalidates the PID, which can cause the health check
                    // to race with the exit handler and trigger spurious recovery.
                    try { OnProcessExited?.Invoke(processId, reportedCode); }
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
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error handling exit of process " + launchedPid + ": " + ex.ToString());
                }
            };
            process.EnableRaisingEvents = true;

            bool started;
            try
            {
                started = process.Start();
            }
            catch (System.ComponentModel.Win32Exception startFailure) when (IsNotFoundError(startFailure.NativeErrorCode))
            {
                // "File not found" is reported for a missing executable and for a missing working directory alike; tell
                // them apart so the caller can say which one (the CLI not installed on this host, or a bad directory).
                logWriter?.Dispose();
                RemoveLaunchFiles(workingDirectory, removeAfterExit);
                if (!Directory.Exists(workingDirectory))
                    throw new DirectoryNotFoundException("The working directory '" + workingDirectory + "' does not exist on this host.", startFailure);
                throw new Armada.Core.Services.AgentRuntimeNotInstalledException(RuntimeType, command, startFailure);
            }

            if (!started)
            {
                RemoveLaunchFiles(workingDirectory, removeAfterExit);
                throw new InvalidOperationException("Failed to start agent process: " + command);
            }
            launchedPid = process.Id;
            try
            {
                Armada.Core.ProcessSupervisor.RecordLaunch(launchedPid, process.StartTime.ToUniversalTime());
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "process " + launchedPid + " start time was not recorded: " + ex.Message);
            }

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
        /// Stop an agent process gracefully when its start time is the recorded launch.
        /// </summary>
        public virtual async Task<AgentStopResult> StopAsync(int processId, CancellationToken token = default)
        {
            Process process;
            try
            {
                process = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                _Logging.Debug(_Header + "process " + processId + " already exited");
                Armada.Core.ProcessSupervisor.ForgetLaunch(processId);
                return AgentStopResult.NotRunning();
            }

            using (process)
            {
                Armada.Core.Enums.LaunchedProcessIdentityEnum identity = Armada.Core.ProcessSupervisor.Classify(process, processId);
                if (identity == Armada.Core.Enums.LaunchedProcessIdentityEnum.NotRunning)
                {
                    _Logging.Debug(_Header + "process " + processId + " already exited");
                    Armada.Core.ProcessSupervisor.ForgetLaunch(processId);
                    return AgentStopResult.NotRunning();
                }

                if (identity != Armada.Core.Enums.LaunchedProcessIdentityEnum.Verified)
                {
                    string reason = identity == Armada.Core.Enums.LaunchedProcessIdentityEnum.Reused
                        ? AgentStopResult.IdentifierReusedReason
                        : AgentStopResult.IdentityUnverifiedReason;
                    _Logging.Warn(_Header + "refused to stop process " + processId + " reason " + reason);
                    return AgentStopResult.Refused(reason);
                }

                try
                {
                    // Ask the agent to shut down cleanly first (SIGTERM on Unix; CLIs flush their session and exit), then
                    // wait without blocking a thread, and only force-kill the whole tree if it is still running.
                    RequestGracefulExit(processId);
                    using (CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        wait.CancelAfter(_GracefulStopTimeoutMs);
                        try
                        {
                            await process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            // Timed out, or the caller gave up waiting: fall through to the forced stop.
                        }
                    }

                    if (!process.HasExited)
                    {
                        _Logging.Warn(_Header + "process " + processId + " did not exit gracefully, killing");
                        process.Kill(entireProcessTree: true);
                    }

                    if (!process.HasExited)
                    {
                        _Logging.Warn(_Header + "process " + processId + " was still running after the stop attempt");
                        return AgentStopResult.Refused(AgentStopResult.KillFailedReason);
                    }

                    Armada.Core.ProcessSupervisor.ForgetLaunch(processId);
                    _Logging.Info(_Header + "stopped process " + processId);
                    return AgentStopResult.Stopped();
                }
                catch (InvalidOperationException)
                {
                    _Logging.Debug(_Header + "process " + processId + " exited while stopping");
                    Armada.Core.ProcessSupervisor.ForgetLaunch(processId);
                    return AgentStopResult.Stopped();
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error stopping process " + processId + ": " + ex.ToString());
                    return AgentStopResult.Refused(AgentStopResult.KillFailedReason);
                }
            }
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
            if (!String.IsNullOrEmpty(line)) OnRawStdoutReceived?.Invoke(processId, line);
            OnOutputReceived?.Invoke(processId, line);
            OnStdoutReceived?.Invoke(processId, line);
        }

        /// <summary>
        /// Raise <see cref="OnProviderError"/> with a structured provider error (for derived runtimes that run in-process
        /// instead of launching a process, and so have no output line to parse it from).
        /// </summary>
        /// <param name="processId">Process id.</param>
        /// <param name="error">The provider error.</param>
        /// <exception cref="ArgumentNullException">Thrown when error is null.</exception>
        protected void RaiseProviderError(int processId, RuntimeProviderError error)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));
            try { OnProviderError?.Invoke(processId, error); }
            catch (Exception ex) { _Logging.Warn(_Header + "error in OnProviderError handler for process " + processId + ": " + ex.ToString()); }
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
        /// Whether this launch already asked the CLI for structured output for an interactive consumer (Claude Code
        /// stream-json for chat, Codex exec --json), which reads the raw events itself. The default is false.
        /// </summary>
        protected virtual bool InteractiveStructuredOutput => false;

        /// <summary>
        /// Whether this launch decodes structured output for mission progress: <see cref="StructuredProgress"/> is set,
        /// the runtime has structured output, and no interactive consumer reads the raw events.
        /// </summary>
        protected bool StructuredProgressActive => StructuredProgress
            && !InteractiveStructuredOutput
            && Armada.Core.Services.MissionStreamDecoder.Supports(RuntimeType);

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
            return Task.FromResult(Armada.Core.ProcessSupervisor.IsTrackedProcessAlive(processId));
        }

        #endregion

        #region Private-Methods

        private void RequestGracefulExit(int processId)
        {
            if (OperatingSystem.IsWindows()) return;
            try
            {
                if (SysKill(processId, 15) != 0)
                    _Logging.Debug(_Header + "SIGTERM to process " + processId + " was not delivered");
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                _Logging.Debug(_Header + "SIGTERM is unavailable on this platform: " + ex.Message);
            }
        }

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        private static extern int SysKill(int pid, int signal);

        /// <summary>
        /// Whether a process-start error code means the executable or the working directory was not found.
        /// </summary>
        private static bool IsNotFoundError(int nativeErrorCode)
        {
            return Array.IndexOf(_NotFoundErrors, nativeErrorCode) >= 0;
        }

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
        /// Called at the start of each <see cref="StartAsync"/>, before the arguments are built, so a runtime can reset
        /// the per-launch state it keeps (for example the run outcome a Mux launch tracks). The default does nothing.
        /// </summary>
        /// <param name="finalMessageFilePath">The launch's final-message file, or null.</param>
        protected virtual void BeginLaunch(string? finalMessageFilePath)
        {
        }

        /// <summary>
        /// Decide the exit code reported for the launch's process (on <see cref="OnProcessExited"/> and in the log), after
        /// all of its output was read and before subscribers learn of the exit. A runtime whose CLI exits non-zero for a
        /// run that completed overrides this, and may write the final message the CLI skipped. The default reports the
        /// exit code unchanged.
        /// </summary>
        /// <param name="exitCode">The process exit code, or null when unknown.</param>
        /// <returns>The exit code to report.</returns>
        protected virtual int? ResolveExitCode(int? exitCode)
        {
            return exitCode;
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

        /// <summary>
        /// Raise what a decoded structured stdout line stands for: each activity (logged as "&gt; " and its summary), each
        /// transcript line (raised as stderr), then each readable output line (logged and raised as stdout).
        /// </summary>
        private void RaiseDecoded(int processId, Armada.Core.Services.MissionStreamDecodeResult decoded, StreamWriter? logWriter)
        {
            foreach (RuntimeActivity activity in decoded.Activities)
            {
                try { logWriter?.WriteLine("> " + activity.Summary); }
                catch (ObjectDisposedException) { }

                try { OnActivity?.Invoke(processId, activity); }
                catch (Exception ex) { _Logging.Warn(_Header + "error in OnActivity handler for process " + processId + ": " + ex.Message); }
            }

            foreach (string line in decoded.TranscriptLines)
            {
                RaiseTranscriptLine(processId, line, logWriter);
            }

            foreach (string line in decoded.OutputLines)
            {
                RaisePlainStdout(processId, line, logWriter);
            }
        }

        /// <summary>
        /// Log a decoded transcript line and raise it as text mode raised the CLI's stderr transcript: on
        /// <see cref="OnOutputReceived"/> and <see cref="OnStderrReceived"/>, never as stdout.
        /// </summary>
        private void RaiseTranscriptLine(int processId, string line, StreamWriter? logWriter)
        {
            try { logWriter?.WriteLine("[stderr] " + line); }
            catch (ObjectDisposedException) { }

            try { OnOutputReceived?.Invoke(processId, line); }
            catch { }

            try { OnStderrReceived?.Invoke(processId, line); }
            catch { }
        }

        /// <summary>
        /// Log a readable stdout line and raise it on <see cref="OnOutputReceived"/> and <see cref="OnStdoutReceived"/>.
        /// </summary>
        private void RaisePlainStdout(int processId, string line, StreamWriter? logWriter)
        {
            try { logWriter?.WriteLine(line); }
            catch (ObjectDisposedException) { }

            try { OnOutputReceived?.Invoke(processId, line); }
            catch { }

            try { OnStdoutReceived?.Invoke(processId, line); }
            catch { }
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
        /// Add a working-directory file to the git exclude file (<c>info/exclude</c>) of the repository the directory
        /// belongs to, so it is never committed. A directory outside git is left alone. Never throws except on cancellation.
        /// </summary>
        /// <param name="workingDirectory">The launch's working directory.</param>
        /// <param name="relativePath">The file, relative to the working directory.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        protected static async Task ExcludeFromGitAsync(string workingDirectory, string relativePath, CancellationToken token)
        {
            try
            {
                string? excludePath = Armada.Core.Services.GitExcludeFile.ResolvePath(workingDirectory);
                if (String.IsNullOrEmpty(excludePath)) return;
                await Armada.Core.Services.GitExcludeFile.AddEntryAsync(excludePath, "/" + relativePath.Replace('\\', '/'), token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
            }
        }

        /// <summary>
        /// Remove the files a launch wrote into its working directory for the CLI's own use (or restore the contents they
        /// replaced), and their directory when it is then empty and did not exist before. Never throws.
        /// </summary>
        /// <param name="workingDirectory">The launch's working directory.</param>
        /// <param name="files">Files marked <see cref="Armada.Core.Services.IsolationConfigFile.RemoveAfterExit"/>.</param>
        protected static void RemoveLaunchFiles(string workingDirectory, List<Armada.Core.Services.IsolationConfigFile> files)
        {
            if (files == null) return;
            foreach (Armada.Core.Services.IsolationConfigFile file in files)
            {
                try
                {
                    string path = Path.Combine(workingDirectory, file.RelativePath);
                    if (file.OriginalContents != null)
                    {
                        File.WriteAllText(path, file.OriginalContents);
                        continue;
                    }

                    if (File.Exists(path)) File.Delete(path);
                    string? directory = Path.GetDirectoryName(path);
                    if (directory != null
                        && !String.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal)
                        && Directory.Exists(directory)
                        && Directory.GetFileSystemEntries(directory).Length == 0)
                    {
                        Directory.Delete(directory);
                    }
                }
                catch
                {
                }
            }

            files.Clear();
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
