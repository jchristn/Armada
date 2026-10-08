namespace Armada.Server
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Protocol;
    using Armada.Core.Services.Interfaces;
    using Armada.Runtimes;
    using Armada.Runtimes.Interfaces;
    using Armada.Server.Ask;
    using Armada.Server.WebSocket;
    using SyslogLogging;

    /// <summary>
    /// Runs an interactive chat turn with a captain by launching its agent runtime headlessly, the same
    /// way missions and planning sessions drive the CLI. Every runtime (Mux, Claude Code, Codex, Gemini,
    /// Cursor) is invoked through its <see cref="IAgentRuntime"/> adapter in a throwaway working directory;
    /// the agent's final response is read from the runtime's final-message artifact. With a
    /// <see cref="LaunchRouter"/>, a turn goes to a connected, eligible Harbor exactly as a mission would: the CLI runs
    /// on the Harbor host in a scratch directory the Harbor owns, its output and final message stream back over the
    /// link, and stopping the turn kills it there. There is no separate
    /// model-endpoint (PolyPrompt) path: Mux, like the others, is a CLI that runs headless.
    /// Streaming events of the direct chat endpoint go only to the caller's own sockets; Ask Armada threads use
    /// <see cref="RunTurnAsync"/>, which takes a fully built prompt, a thread-scoped MCP token, and callbacks.
    /// </summary>
    public class CaptainChatService : IAskCaptainTurnRunner
    {
        #region Public-Members

        /// <summary>
        /// Routes each turn to a connected, eligible Harbor (the captain's CLI then runs on that machine and its output
        /// streams back over the link) or to the Admiral host, with the same policy missions use, including
        /// requireHarborForLaunch. Null runs every turn on the Admiral host.
        /// </summary>
        public CaptainLaunchRouter? LaunchRouter { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly AgentRuntimeFactory _RuntimeFactory;
        private readonly ArmadaWebSocketHub? _WebSocketHub;
        private readonly IPromptTemplateService? _PromptTemplates;
        private readonly ISessionTokenService? _SessionTokenService;
        private readonly int _McpPort;
        private readonly string _McpHost;
        private readonly LoggingModule _Logging;
        private readonly string _Header = "[CaptainChatService] ";

        // A chat turn spawns a real agent process; bound how long we wait and how much stdout we retain.
        private const int _DefaultTimeoutMs = 300000;
        private const int _MaxOutputChars = 200000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="runtimeFactory">Agent runtime factory used to launch the captain's CLI headlessly.</param>
        /// <param name="webSocketHub">WebSocket hub used to stream reply chunks live; may be null.</param>
        /// <param name="promptTemplates">Prompt template service used to resolve the Ask Armada system prompt; may be null.</param>
        /// <param name="sessionTokenService">Session token service used to mint a short-lived per-caller token
        /// so an in-process (ApiEndpoint) captain can reach Armada's own MCP server scoped to the caller; may
        /// be null (MCP tool access is then disabled).</param>
        /// <param name="mcpPort">The port Armada's MCP server listens on, used to build the in-runtime MCP
        /// endpoint URL; a non-positive value disables MCP tool access.</param>
        /// <param name="logging">Logging module.</param>
        public CaptainChatService(DatabaseDriver database, AgentRuntimeFactory runtimeFactory, ArmadaWebSocketHub? webSocketHub, IPromptTemplateService? promptTemplates, ISessionTokenService? sessionTokenService, int mcpPort, LoggingModule logging)
            : this(database, runtimeFactory, webSocketHub, promptTemplates, sessionTokenService, mcpPort, logging, Armada.Core.Services.ArmadaMcpConfigBuilder.DefaultHost)
        {
        }

        /// <summary>
        /// Instantiate with the host MCP clients must use to reach Armada's MCP listener.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="runtimeFactory">Agent runtime factory used to launch the captain's CLI headlessly.</param>
        /// <param name="webSocketHub">WebSocket hub used to stream reply chunks live; may be null.</param>
        /// <param name="promptTemplates">Prompt template service used to resolve the Ask Armada system prompt; may be null.</param>
        /// <param name="sessionTokenService">Session token service used to mint a short-lived per-caller token; may be null.</param>
        /// <param name="mcpPort">The port Armada's MCP server listens on; a non-positive value disables MCP tool access.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="mcpHost">Host placed in generated MCP URLs (see
        /// <see cref="Armada.Core.Services.ArmadaMcpConfigBuilder.ClientHostFor"/>); null or empty means localhost.</param>
        public CaptainChatService(DatabaseDriver database, AgentRuntimeFactory runtimeFactory, ArmadaWebSocketHub? webSocketHub, IPromptTemplateService? promptTemplates, ISessionTokenService? sessionTokenService, int mcpPort, LoggingModule logging, string? mcpHost)
        {
            _McpHost = String.IsNullOrWhiteSpace(mcpHost) ? Armada.Core.Services.ArmadaMcpConfigBuilder.DefaultHost : mcpHost!;
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _RuntimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
            _WebSocketHub = webSocketHub;
            _PromptTemplates = promptTemplates;
            _SessionTokenService = sessionTokenService;
            _McpPort = mcpPort > 0 ? mcpPort : 0;
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Send one chat turn to a captain and return the reply plus per-turn metrics. The captain's runtime
        /// is launched headlessly in a temporary working directory; the reply is the runtime's final-message
        /// artifact (falling back to accumulated stdout).
        /// </summary>
        /// <param name="captainId">Captain identifier (cpt_ prefix).</param>
        /// <param name="request">The new message and prior conversation.</param>
        /// <param name="auth">Caller authentication context, used to scope which captain can be chatted with.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The assistant reply and its timing statistics.</returns>
        public async Task<CaptainChatResponse> ChatAsync(string captainId, CaptainChatRequest request, AuthContext auth, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(request.Message)) return Fail("A message is required.");

            // Scope the captain read so a caller can only chat with a captain they can see: a global admin any,
            // a tenant admin any in their tenant, a regular user only their own. Not-visible reads as not-found.
            Captain? captain = auth.IsAdmin
                ? await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false)
                : auth.IsTenantAdmin
                    ? await _Database.Captains.ReadAsync(auth.TenantId!, captainId, token).ConfigureAwait(false)
                    : await _Database.Captains.ReadAsync(auth.TenantId!, auth.UserId!, captainId, token).ConfigureAwait(false);
            if (captain == null) return Fail("Captain not found.");

            // The editable Ask Armada system prompt (Configuration > Prompts, template 'ask.system').
            string? askSystemPrompt = null;
            if (_PromptTemplates != null)
            {
                try
                {
                    PromptTemplate? tpl = await _PromptTemplates.ResolveAsync("ask.system", token).ConfigureAwait(false);
                    askSystemPrompt = tpl?.Content;
                }
                catch { }
            }

            string prompt = BuildPrompt(captain, request, askSystemPrompt);

            CaptainChatTurnOptions options = new CaptainChatTurnOptions();
            options.Captain = captain;
            options.Prompt = prompt;
            options.ShowThinking = request.ShowThinking;
            options.TenantId = captain.TenantId;
            options.TimeoutMs = _DefaultTimeoutMs;

            // In-process (ApiEndpoint) captains have no CLI harness and thus no MCP config of their own. To let an Ask
            // Armada chat actually drive Armada's orchestration tools, mint a short-lived session token for the caller;
            // the runtime connects to Armada's own MCP server, which authenticates the token and scopes every tool call
            // to this caller -- exactly as a real per-user MCP client would.
            if (captain.Runtime == AgentRuntimeEnum.ApiEndpoint
                && _SessionTokenService != null
                && _McpPort > 0
                && !String.IsNullOrEmpty(auth.TenantId)
                && !String.IsNullOrEmpty(auth.UserId))
            {
                try
                {
                    AuthenticateResult minted = _SessionTokenService.CreateToken(auth.TenantId!, auth.UserId!);
                    if (!String.IsNullOrEmpty(minted.Token)) options.McpSessionToken = minted.Token;
                }
                catch (Exception mintEx)
                {
                    _Logging.Warn(_Header + "could not mint MCP session token for chat: " + mintEx.Message);
                }
            }

            // Stream to the caller's own sockets only (never to other users or tenants).
            string? turnId = request.TurnId;
            if (!String.IsNullOrEmpty(turnId) && !String.IsNullOrEmpty(auth.TenantId) && !String.IsNullOrEmpty(auth.UserId))
            {
                string tenantId = auth.TenantId!;
                string userId = auth.UserId!;
                options.OnChunk = delta => SendToCaller(tenantId, userId, "ask.chunk", new { turnId, delta });
                options.OnThinking = delta => SendToCaller(tenantId, userId, "ask.thinking", new { turnId, delta });
                options.OnTool = activity => SendToCaller(tenantId, userId, "ask.tool", new { turnId, phase = activity.Phase, id = activity.Id, name = activity.Name, arguments = activity.Arguments, ok = activity.Ok, elapsedMs = activity.ElapsedMs, result = activity.Result, permissionDenied = activity.PermissionDenied });
            }

            CaptainChatTurnResult result = await RunCoreAsync(options, false, token).ConfigureAwait(false);
            return result.Response;
        }

        /// <inheritdoc />
        public async Task<CaptainChatTurnResult> RunTurnAsync(CaptainChatTurnOptions options, CancellationToken token = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (String.IsNullOrWhiteSpace(options.Prompt)) return new CaptainChatTurnResult { Response = Fail("A prompt is required.") };
            return await RunCoreAsync(options, true, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Run one headless turn. When <paramref name="scopedMcp"/> is true and a session token is supplied, the captain's
        /// Armada MCP connection is bound to that token: ApiEndpoint captains through ARMADA_MCP_URL/ARMADA_MCP_TOKEN, and
        /// every CLI runtime through the per-launch override built by CaptainThreadMcpPlanner (an X-Token header that
        /// keeps the CLI's own login and configuration directory in place).
        /// </summary>
        private async Task<CaptainChatTurnResult> RunCoreAsync(CaptainChatTurnOptions options, bool scopedMcp, CancellationToken token)
        {
            Captain captain = options.Captain;
            string captainId = captain.Id;
            string prompt = options.Prompt;
            bool showThinking = options.ShowThinking;
            ToolCallCollector toolCalls = new ToolCallCollector();
            Action<string> emitChunk = delta => { if (!String.IsNullOrEmpty(delta)) { try { options.OnChunk?.Invoke(delta); } catch { } } };
            Action<string> emitThinking = delta => { if (!String.IsNullOrEmpty(delta)) { try { options.OnThinking?.Invoke(delta); } catch { } } };
            Action<CaptainToolActivity> emitTool = activity => { toolCalls.Observe(activity); try { options.OnTool?.Invoke(activity); } catch { } };

            string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-chat-" + Guid.NewGuid().ToString("N"));
            string finalMessageFilePath = Path.Combine(workingDirectory, "reply.txt");

            IAgentRuntime? runtime = null;
            RemoteAgentRuntime? remoteRuntime = null;
            int processId = -1;

            try
            {
                // Holds the final-message artifact. A turn on the Admiral host also runs here; a turn on a Harbor runs in
                // a scratch directory the Harbor creates on its own host, and its final message is written back here.
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    if (LaunchRouter != null)
                    {
                        CaptainLaunchContext launch = new CaptainLaunchContext(captain, "chat turn")
                        {
                            Kind = Armada.Core.Harbor.HarborJobKindEnum.AskTurn,
                            TenantId = options.TenantId ?? captain.TenantId,
                            UserId = options.UserId,
                            AllowScratchWorkingDirectory = true
                        };
                        CaptainLaunchTarget target = await LaunchRouter.SelectAsync(launch, token).ConfigureAwait(false);
                        runtime = target.Runtime;
                        remoteRuntime = runtime as RemoteAgentRuntime;
                    }
                    else
                    {
                        runtime = _RuntimeFactory.Create(captain.Runtime);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    if (CaptainLaunchRouter.TryDescribeFailure(e, out string described, out CaptainChatErrorCodeEnum code))
                        return Failed(described, code);
                    return Failed("This captain's runtime (" + captain.Runtime + ") could not be launched for chat: " + e.Message);
                }

                // Claude Code streams token-by-token only in streaming-JSON mode; enable it for chat so the
                // reply renders incrementally and we can read a clean final message + metrics from the
                // terminal "result" event instead of scraping human-readable --print output.
                // Codex likewise runs 'codex exec --json' so its items (reply, reasoning, tool calls) and the turn's token
                // usage arrive as typed events. On a Harbor both ride the launch's StreamJsonOutput flag.
                if (runtime is ClaudeCodeRuntime claudeRuntime)
                {
                    claudeRuntime.StreamJsonOutput = true;
                }
                else if (runtime is CodexRuntime codexRuntime)
                {
                    codexRuntime.JsonOutput = true;
                }
                else if (remoteRuntime != null)
                {
                    remoteRuntime.StreamJsonOutput = true;
                }

                TaskCompletionSource<int?> exitSource = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
                object outputLock = new object();
                StringBuilder output = new StringBuilder();
                StringBuilder thinking = new StringBuilder();
                // Per-turn telemetry (time to first output and first text, total time, the usage and cost the runtime
                // reports), recorded from the captain's own output as it arrives, on the monotonic clock: a wall-clock
                // difference grows by however long the host slept during the turn (or goes negative on a backward NTP
                // step). A turn on a Harbor streams the same lines back over the link, so it is recorded the same way.
                Armada.Core.Services.ChatTurnTelemetryRecorder telemetry = new Armada.Core.Services.ChatTurnTelemetryRecorder();

                bool isMux = captain.Runtime == AgentRuntimeEnum.Mux;
                bool isOpenCode = captain.Runtime == AgentRuntimeEnum.OpenCode;
                bool isClaude = captain.Runtime == AgentRuntimeEnum.ClaudeCode;
                bool isCodex = captain.Runtime == AgentRuntimeEnum.Codex;
                string? reportedModel = null;
                string? claudeFinalReply = null;

                // The in-process (ApiEndpoint) runtime reports tool activity on a typed channel; its stdout carries only the
                // model's reply text, so reply text can never spoof a tool card and no diagnostic line leaks into the answer.
                if (runtime is ApiAgentRuntime apiRuntime)
                {
                    apiRuntime.OnToolEvent += (pid, toolEvent) => { telemetry.MarkOutput(); emitTool(new CaptainToolActivity
                    {
                        Phase = toolEvent.Phase == ApiRuntimeToolPhaseEnum.Completed ? "completed" : "started",
                        Id = toolEvent.Id,
                        Name = toolEvent.Name,
                        Arguments = toolEvent.Arguments,
                        Ok = toolEvent.Ok,
                        ElapsedMs = toolEvent.ElapsedMs,
                        Result = toolEvent.Result,
                        PermissionDenied = toolEvent.PermissionDenied
                    }); };
                }

                // CLI tool permissions for the in-process (API-endpoint) runtime: its built-in shell tool is gated here,
                // whether or not the turn has a scoped MCP token.
                if (runtime is ApiAgentRuntime gatedRuntime && options.CliPermissionPolicy.HasValue)
                    CliPermissionLaunch.Apply(gatedRuntime, options.CliPermissionPolicy.Value, options.PermissionPromptTimeoutSeconds ?? 600, options.PermissionPrompt);

                runtime.OnStdoutReceived += (pid, line) =>
                {
                    if (isClaude)
                    {
                        // Claude Code streaming-JSON: each stdout line is one typed event. Incremental text deltas stream
                        // the reply token-by-token; the terminal "result" event carries the authoritative final message
                        // and metrics. Non-event lines are ignored.
                        if (!ClaudeStreamLine.TryParse(line, out ClaudeStreamLine? claudeEvent) || claudeEvent == null)
                        {
                            // A Harbor that predates streaming-JSON launches runs Claude Code in --print text mode: keep
                            // its plain reply lines instead of dropping them.
                            if (remoteRuntime != null && !String.IsNullOrEmpty(line) && !line.TrimStart().StartsWith("{", StringComparison.Ordinal))
                            {
                                telemetry.ObservePlainText(line);
                                lock (outputLock)
                                {
                                    if (output.Length < _MaxOutputChars) output.Append(line).Append('\n');
                                }
                                emitChunk(line + "\n");
                            }
                            return;
                        }

                        telemetry.ObserveClaude(claudeEvent);
                        string? deltaText = claudeEvent.TextDelta;
                        if (!String.IsNullOrEmpty(deltaText))
                        {
                            lock (outputLock)
                            {
                                if (output.Length < _MaxOutputChars) output.Append(deltaText);
                            }
                            emitChunk(deltaText!);
                        }
                        else if (claudeEvent.Type == ClaudeStreamLine.TypeAssistant || claudeEvent.Type == ClaudeStreamLine.TypeUser)
                        {
                            ObserveClaudeToolBlocks(claudeEvent, toolCalls, emitTool);
                        }

                        if (claudeEvent.Type == ClaudeStreamLine.TypeAssistant && !String.IsNullOrEmpty(claudeEvent.Message?.Model))
                        {
                            lock (outputLock) reportedModel = claudeEvent.Message!.Model;
                        }
                        else if (claudeEvent.Type == ClaudeStreamLine.TypeResult)
                        {
                            // Typed permission denials: mark the refused calls so the transcript can explain the policy.
                            if (claudeEvent.PermissionDenials != null)
                            {
                                foreach (ClaudePermissionDenial denial in claudeEvent.PermissionDenials)
                                {
                                    if (denial == null || String.IsNullOrEmpty(denial.ToolUseId)) continue;
                                    emitTool(new CaptainToolActivity
                                    {
                                        Phase = "completed",
                                        Id = denial.ToolUseId,
                                        Name = denial.ToolName ?? toolCalls.NameOf(denial.ToolUseId),
                                        Ok = false,
                                        PermissionDenied = true
                                    });
                                }
                            }

                            lock (outputLock)
                            {
                                if (claudeEvent.Result != null) claudeFinalReply = claudeEvent.Result;
                            }
                        }

                        return;
                    }

                    if (isMux && MuxProtocolEvent.TryParse(line, out MuxProtocolEvent? muxEvent) && muxEvent != null)
                    {
                        // Telemetry and live text from the typed Mux event. The final reply still comes from the
                        // final-message artifact; assistant_text carries the streamed deltas.
                        telemetry.ObserveMux(muxEvent);
                        string? deltaText = null;
                        lock (outputLock)
                        {
                            if (!String.IsNullOrEmpty(muxEvent.Model)) reportedModel = muxEvent.Model;
                            if (muxEvent.EventType == MuxProtocolEvent.AssistantText)
                            {
                                deltaText = muxEvent.Text;
                                // Accumulate streamed assistant text so a reply survives even if the final-message
                                // artifact (reply.txt) is not written.
                                if (!String.IsNullOrEmpty(deltaText) && output.Length < _MaxOutputChars)
                                    output.Append(deltaText);
                            }
                        }

                        // When --show-thinking is active, Mux streams the model's reasoning as assistant_thinking events
                        // on a separate channel from the answer.
                        string? thinkingDelta = null;
                        if (muxEvent.EventType == MuxProtocolEvent.AssistantThinking && !String.IsNullOrEmpty(muxEvent.Text))
                        {
                            thinkingDelta = muxEvent.Text;
                            lock (outputLock)
                            {
                                if (thinking.Length < _MaxOutputChars) thinking.Append(thinkingDelta);
                            }
                        }

                        if (!String.IsNullOrEmpty(deltaText)) emitChunk(deltaText!);
                        if (!String.IsNullOrEmpty(thinkingDelta)) emitThinking(thinkingDelta!);

                        // Surface tool activity to the chat UI: when a tool call is proposed and when it completes (with
                        // success/failure, runtime, and result for inspection).
                        if (muxEvent.EventType == MuxProtocolEvent.ToolCallProposed && muxEvent.ToolCall != null)
                        {
                            emitTool(new CaptainToolActivity { Phase = "started", Id = muxEvent.ToolCall.Id, Name = muxEvent.ToolCall.Name, Arguments = Truncate(muxEvent.ToolCall.Arguments, 4000) });
                        }
                        else if (muxEvent.EventType == MuxProtocolEvent.ToolCallCompleted)
                        {
                            bool? ok = muxEvent.Result?.Success;
                            string? resultJson = muxEvent.Result == null
                                ? null
                                : Truncate(muxEvent.Result.Content ?? JsonSerializer.Serialize(muxEvent.Result), 16000);
                            emitTool(new CaptainToolActivity { Phase = "completed", Id = muxEvent.ToolCallId, Name = muxEvent.ToolName, Ok = ok, ElapsedMs = muxEvent.ElapsedMs, Result = resultJson });
                        }

                        return;
                    }

                    if (isOpenCode && OpenCodeStreamEvent.TryParse(line, out OpenCodeStreamEvent? openCodeEvent) && openCodeEvent != null)
                    {
                        // OpenCode --format json streams typed events with a nested part. Surface assistant text and
                        // tool-call chips; the raw JSON envelope never leaks.
                        telemetry.ObserveOpenCode(openCodeEvent);
                        OpenCodePart? part = openCodeEvent.Part;
                        if (openCodeEvent.Type == OpenCodeStreamEvent.TypeText)
                        {
                            string? deltaText = openCodeEvent.AssistantText;
                            if (!String.IsNullOrEmpty(deltaText))
                            {
                                lock (outputLock)
                                {
                                    if (output.Length < _MaxOutputChars) output.Append(deltaText);
                                }
                                emitChunk(deltaText!);
                            }
                        }
                        else if (openCodeEvent.Type == OpenCodeStreamEvent.TypeReasoning)
                        {
                            // OpenCode --thinking streams reasoning on a separate channel; surface it as thinking (never
                            // as reply text).
                            string? thinkingDelta = part?.Text;
                            if (!String.IsNullOrEmpty(thinkingDelta) && showThinking)
                            {
                                lock (outputLock)
                                {
                                    if (thinking.Length < _MaxOutputChars) thinking.Append(thinkingDelta);
                                }
                                emitThinking(thinkingDelta!);
                            }
                        }
                        else if (openCodeEvent.Type == OpenCodeStreamEvent.TypeToolUse && part != null)
                        {
                            OpenCodeToolState? state = part.State;
                            string? status = state?.Status;
                            bool failed = String.Equals(status, "error", StringComparison.Ordinal);
                            bool completed = failed || String.Equals(status, "completed", StringComparison.Ordinal);
                            bool? ok = failed ? false : (state?.Metadata?.Exit.HasValue == true ? state.Metadata.Exit.Value == 0 : (bool?)null);
                            string? resultText = failed ? (state?.Error ?? state?.Output) : state?.Output;
                            emitTool(new CaptainToolActivity
                            {
                                Phase = completed ? "completed" : "started",
                                Id = part.CallId,
                                Name = part.Tool,
                                Arguments = Truncate(state?.Input, 4000),
                                Ok = ok,
                                Result = Truncate(resultText, 16000)
                            });
                        }

                        return;
                    }

                    if (isCodex && CodexStreamEvent.TryParse(line, out CodexStreamEvent? codexEvent) && codexEvent != null)
                    {
                        // codex exec --json: a completed agent message is reply text, reasoning is thinking, and command
                        // and MCP tool items become tool-call chips. The raw JSON envelope never leaks into the reply.
                        telemetry.ObserveCodex(codexEvent);
                        ObserveCodexItem(codexEvent, showThinking, outputLock, output, thinking, emitChunk, emitThinking, emitTool);
                        return;
                    }

                    telemetry.ObservePlainText(line);
                    lock (outputLock)
                    {
                        if (output.Length < _MaxOutputChars)
                        {
                            output.Append(line);
                            output.Append('\n');
                        }
                    }
                    emitChunk(line + "\n");
                };
                runtime.OnProcessExited += (pid, code) => exitSource.TrySetResult(code);

                // Bind the captain's Armada MCP connection to the supplied session token. ApiEndpoint captains read the
                // endpoint and token from the environment. Every CLI runtime (Claude Code, Codex, Gemini, Cursor, Mux,
                // OpenCode) gets a per-launch override from CaptainThreadMcpPlanner that leaves the CLI's own login in
                // place (only on scoped turns, so the direct chat endpoint's behavior is unchanged).
                Dictionary<string, string>? environment = null;
                bool isolateLaunch = false;
                if (remoteRuntime != null)
                {
                    // On a Harbor the captain reaches Armada's MCP server at the URL the Admiral advertised in the
                    // handshake (harbor.advertisedMcpBaseUrl); the Harbor binds the turn's token to it, as for missions.
                    if (scopedMcp
                        && !String.IsNullOrEmpty(options.McpSessionToken)
                        && Armada.Core.Services.CaptainThreadMcpPlanner.SupportsApprovalGating(captain.Runtime))
                    {
                        remoteRuntime.McpSessionToken = options.McpSessionToken;
                    }

                    // A Harbor cannot route permission prompts to Armada yet (as for missions), so ApproveInArmada runs as
                    // Refuse there: the launched captain's auto-approve is already off for anything but Bypass.
                    if (options.CliPermissionPolicy == CliPermissionPolicyEnum.ApproveInArmada)
                        _Logging.Info(_Header + "chat turn for captain " + captainId + " runs on Harbor " + remoteRuntime.HarborId + ": ApproveInArmada runs as Refuse there");
                }
                else if (!String.IsNullOrEmpty(options.McpSessionToken) && _McpPort > 0)
                {
                    if (captain.Runtime == AgentRuntimeEnum.ApiEndpoint)
                    {
                        // Use the same MCP URL captains' generated configs target (http://<host>:<port>/mcp). The MCP
                        // listener binds to the configured hostname (default "localhost") and only answers requests whose
                        // Host matches it, so the host comes from ArmadaMcpConfigBuilder.ClientHostFor of that hostname.
                        environment = new Dictionary<string, string>
                        {
                            ["ARMADA_MCP_URL"] = Armada.Core.Services.ArmadaMcpConfigBuilder.GetMcpUrl(_McpPort, _McpHost),
                            ["ARMADA_MCP_TOKEN"] = options.McpSessionToken!
                        };
                    }
                    else if (scopedMcp
                        && runtime is BaseAgentRuntime scopedRuntime
                        && Armada.Core.Services.CaptainThreadMcpPlanner.SupportsApprovalGating(captain.Runtime))
                    {
                        scopedRuntime.McpSessionToken = options.McpSessionToken;
                        scopedRuntime.McpHost = _McpHost;
                        isolateLaunch = true;

                        // ApproveInArmada: Claude Code sends permission prompts to Armada's prompt tool on the scoped
                        // "armada" server bound to this session's token.
                        if (options.CliPermissionPolicy.HasValue)
                            CliPermissionLaunch.Apply(runtime, options.CliPermissionPolicy.Value, options.PermissionPromptTimeoutSeconds ?? 600);
                    }
                }

                try
                {
                    processId = await runtime.StartAsync(
                        remoteRuntime != null ? String.Empty : workingDirectory,
                        prompt,
                        environment: environment,
                        finalMessageFilePath: finalMessageFilePath,
                        model: captain.Model,
                        captain: captain,
                        isolateLaunch: isolateLaunch,
                        mcpPort: _McpPort,
                        showThinking: showThinking,
                        token: token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception startFailure) when (CaptainLaunchRouter.TryDescribeFailure(startFailure, out string startMessage, out CaptainChatErrorCodeEnum startCode))
                {
                    _Logging.Warn(_Header + "chat turn for captain " + captainId + " could not start: " + startFailure.Message);
                    return Failed(startMessage, startCode);
                }

                using (CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeoutCts.CancelAfter(options.TimeoutMs);

                    Task finished = await Task.WhenAny(
                        exitSource.Task,
                        Task.Delay(Timeout.Infinite, timeoutCts.Token)).ConfigureAwait(false);

                    if (finished != exitSource.Task)
                    {
                        try { await runtime.StopAsync(processId, CancellationToken.None).ConfigureAwait(false); }
                        catch { }

                        if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                        return Failed("The captain did not respond within the time limit.");
                    }
                }

                int? exitCode = await exitSource.Task.ConfigureAwait(false);

                string reply = String.Empty;
                try
                {
                    if (File.Exists(finalMessageFilePath))
                    {
                        string artifact = await File.ReadAllTextAsync(finalMessageFilePath).ConfigureAwait(false);
                        if (!String.IsNullOrWhiteSpace(artifact)) reply = artifact.Trim();
                    }
                }
                catch { }

                if (String.IsNullOrWhiteSpace(reply))
                {
                    // Prefer Claude's authoritative streaming-JSON "result" text; otherwise fall back to the
                    // accumulated streamed output (concatenated deltas for Claude, raw stdout for others).
                    lock (outputLock)
                        reply = (!String.IsNullOrWhiteSpace(claudeFinalReply) ? claudeFinalReply! : output.ToString()).Trim();
                }

                if (String.IsNullOrWhiteSpace(reply))
                {
                    return Failed(exitCode.HasValue && exitCode.Value != 0
                        ? "The captain exited with code " + exitCode.Value + " before producing a response."
                        : "The captain produced no response.");
                }

                // For non-Mux runtimes, lift the prompt-instructed <thinking> block out of the reply so it
                // renders in the collapsible thinking section instead of inline with the final answer.
                if (showThinking && !isMux)
                {
                    string extractedThinking = ExtractAndStripThinking(ref reply);
                    if (!String.IsNullOrEmpty(extractedThinking))
                    {
                        lock (outputLock)
                        {
                            thinking.Clear();
                            thinking.Append(extractedThinking);
                        }
                    }
                }

                // All timing is on the recorder's one monotonic base, so time to first token never exceeds the total
                // and streaming always resolves. Real token counts come only from a runtime's own usage report (Claude
                // Code's result event, Codex's turn.completed, OpenCode's step_finish); otherwise the shared builder
                // estimates completion tokens from the reply, matching planning-session metrics. (Mux's
                // finalEstimatedTokens is a whole-context estimate, not the reply, and is never used.)
                List<AskMessageToolCall> finishedToolCalls = toolCalls.ToList();
                CaptainChatMetrics metrics = telemetry.Build(reply, finishedToolCalls);
                long? realOutputTokens = telemetry.OutputTokens;

                string thinkingText;
                lock (outputLock) thinkingText = thinking.ToString().Trim();

                CaptainChatResponse response = new CaptainChatResponse
                {
                    Success = true,
                    Reply = reply,
                    Thinking = String.IsNullOrEmpty(thinkingText) ? null : thinkingText,
                    Model = !String.IsNullOrEmpty(reportedModel) ? reportedModel
                        : (String.IsNullOrEmpty(captain.Model) ? captain.Runtime.ToString() : captain.Model),
                    Metrics = metrics,
                };

                // Best-effort token accounting: the runtime's reported usage where it has one, estimated otherwise.
                await Armada.Core.Services.TokenUsageCapture.CaptureAsync(
                    _Database, _Logging, "chat",
                    model: response.Model,
                    runtime: captain.Runtime.ToString(),
                    tenantId: options.TenantId ?? captain.TenantId,
                    userId: options.UserId,
                    vesselId: null,
                    captainId: captain.Id,
                    sourceId: captain.CurrentMissionId,
                    inputTokens: telemetry.InputTokens,
                    outputTokens: realOutputTokens,
                    cachedTokens: telemetry.CachedTokens,
                    inputText: prompt,
                    outputText: reply,
                    token: token,
                    harborId: remoteRuntime?.HarborId).ConfigureAwait(false);

                _Logging.Debug(_Header + "chat turn for captain " + captainId + " (" + captain.Runtime + "): " +
                    (response.Metrics.TotalMs?.ToString("F0") ?? "?") + "ms, exit " + (exitCode?.ToString() ?? "?"));

                return new CaptainChatTurnResult { Response = response, ToolCalls = finishedToolCalls };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "chat turn failed for captain " + captainId + ": " + e.ToString());
                return Failed(e.Message);
            }
            finally
            {
                try { if (Directory.Exists(workingDirectory)) Directory.Delete(workingDirectory, true); }
                catch { }
            }
        }


        private CaptainChatTurnResult Failed(string error)
        {
            return new CaptainChatTurnResult { Response = Fail(error) };
        }

        private CaptainChatTurnResult Failed(string error, CaptainChatErrorCodeEnum code)
        {
            CaptainChatResponse response = Fail(error);
            response.ErrorCode = code;
            return new CaptainChatTurnResult { Response = response };
        }

        private static void ObserveClaudeToolBlocks(ClaudeStreamLine parsed, ToolCallCollector collector, Action<CaptainToolActivity> emitTool)
        {
            if (parsed.Message?.Content == null) return;

            foreach (ClaudeStreamContentBlock block in parsed.Message.Content)
            {
                if (block == null) continue;
                if (String.Equals(block.Type, ClaudeStreamContentBlock.TypeToolUse, StringComparison.Ordinal))
                {
                    emitTool(new CaptainToolActivity
                    {
                        Phase = "started",
                        Id = block.Id,
                        Name = block.Name,
                        Arguments = Truncate(block.Input, 4000)
                    });
                }
                else if (String.Equals(block.Type, ClaudeStreamContentBlock.TypeToolResult, StringComparison.Ordinal))
                {
                    emitTool(new CaptainToolActivity
                    {
                        Phase = "completed",
                        Id = block.ToolUseId,
                        Name = collector.NameOf(block.ToolUseId),
                        Ok = block.IsError.HasValue ? !block.IsError.Value : true,
                        ElapsedMs = collector.ElapsedSince(block.ToolUseId),
                        Result = Truncate(block.ContentText(), 16000)
                    });
                }
            }
        }

        /// <summary>
        /// Surface one 'codex exec --json' item: a completed agent message is reply text (several messages are joined by
        /// a blank line), completed reasoning is thinking when the turn shows thinking, and command executions and MCP
        /// tool calls are tool-call chips (started, then completed with their status and exit code).
        /// </summary>
        private static void ObserveCodexItem(
            CodexStreamEvent evt,
            bool showThinking,
            object outputLock,
            StringBuilder output,
            StringBuilder thinking,
            Action<string> emitChunk,
            Action<string> emitThinking,
            Action<CaptainToolActivity> emitTool)
        {
            CodexStreamItem? item = evt.Item;
            if (item == null) return;
            bool started = evt.Type == CodexStreamEvent.TypeItemStarted;
            bool completed = evt.Type == CodexStreamEvent.TypeItemCompleted;

            if (item.Type == CodexStreamItem.TypeAgentMessage)
            {
                if (!completed || String.IsNullOrEmpty(item.Text)) return;
                string delta;
                lock (outputLock)
                {
                    delta = output.Length > 0 ? "\n\n" + item.Text : item.Text!;
                    if (output.Length < _MaxOutputChars) output.Append(delta);
                }

                emitChunk(delta);
                return;
            }

            if (item.Type == CodexStreamItem.TypeReasoning)
            {
                if (!completed || !showThinking || String.IsNullOrEmpty(item.Text)) return;
                string delta;
                lock (outputLock)
                {
                    delta = thinking.Length > 0 ? "\n\n" + item.Text : item.Text!;
                    if (thinking.Length < _MaxOutputChars) thinking.Append(delta);
                }

                emitThinking(delta);
                return;
            }

            bool isCommand = item.Type == CodexStreamItem.TypeCommandExecution;
            bool isMcp = item.Type == CodexStreamItem.TypeMcpToolCall;
            if (!isCommand && !isMcp) return;
            if (!started && !completed) return;

            string name = isCommand
                ? "shell"
                : (String.IsNullOrEmpty(item.Server) ? (item.Tool ?? "tool") : item.Server + "." + (item.Tool ?? "tool"));
            bool failed = String.Equals(item.Status, "failed", StringComparison.Ordinal) || (item.ExitCode.HasValue && item.ExitCode.Value != 0);
            emitTool(new CaptainToolActivity
            {
                Phase = completed ? "completed" : "started",
                Id = item.Id,
                Name = name,
                Arguments = isCommand ? Truncate(item.Command, 4000) : null,
                Ok = completed ? !failed : (bool?)null
            });
        }


        private static string BuildPrompt(Captain captain, CaptainChatRequest request, string? systemPrompt)
        {
            StringBuilder builder = new StringBuilder();

            if (!String.IsNullOrWhiteSpace(systemPrompt))
            {
                builder.AppendLine(systemPrompt.Trim());
                builder.AppendLine();
            }

            if (!String.IsNullOrWhiteSpace(captain.SystemInstructions))
            {
                builder.AppendLine(captain.SystemInstructions.Trim());
                builder.AppendLine();
            }

            // Mux surfaces reasoning natively via --show-thinking; every other runtime has no headless
            // thinking channel, so when the user asks to see thinking we instruct the model to emit its
            // reasoning in a single delimited block that the service then lifts out of the answer.
            if (request.ShowThinking && captain.Runtime != AgentRuntimeEnum.Mux)
            {
                builder.AppendLine("Before your final answer, briefly work through your reasoning inside a single block delimited by <thinking> and </thinking>. Write the final answer after the closing </thinking> tag. Use those tags only once, and never inside the final answer.");
                builder.AppendLine();
            }

            if (request.History != null && request.History.Count > 0)
            {
                foreach (CaptainChatMessage message in request.History)
                {
                    if (String.IsNullOrWhiteSpace(message.Content)) continue;
                    string speaker = String.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "User";
                    builder.AppendLine(speaker + ": " + message.Content.Trim());
                }
                builder.AppendLine();
            }

            builder.Append("User: " + request.Message.Trim());
            return builder.ToString();
        }

        /// <summary>
        /// Lift the prompt-instructed reasoning out of a reply: return the concatenated text of every
        /// &lt;thinking&gt;...&lt;/thinking&gt; block and rewrite <paramref name="reply"/> to the answer with those
        /// blocks removed. If removing them would leave no answer, the reply is left intact and empty is
        /// returned (so a model that put everything in the block does not yield a blank answer).
        /// </summary>
        /// <param name="reply">The reply to strip; rewritten in place when a block is removed.</param>
        /// <returns>The extracted reasoning text, or empty when none was present.</returns>
        private static string ExtractAndStripThinking(ref string reply)
        {
            if (String.IsNullOrEmpty(reply)) return String.Empty;

            MatchCollection matches = Regex.Matches(reply, "<thinking>(.*?)</thinking>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matches.Count == 0) return String.Empty;

            StringBuilder collected = new StringBuilder();
            foreach (Match match in matches)
            {
                string piece = match.Groups[1].Value.Trim();
                if (piece.Length == 0) continue;
                if (collected.Length > 0) collected.AppendLine();
                collected.Append(piece);
            }

            string stripped = Regex.Replace(reply, "<thinking>.*?</thinking>", String.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
            if (String.IsNullOrWhiteSpace(stripped)) return String.Empty;

            reply = stripped;
            return collected.ToString().Trim();
        }




        private void SendToCaller(string tenantId, string userId, string eventType, object payload)
        {
            if (_WebSocketHub == null) return;
            try { _WebSocketHub.SendToUser(tenantId, userId, eventType, payload); }
            catch { }
        }

        private static string? Truncate(string? value, int max)
        {
            if (String.IsNullOrEmpty(value) || value!.Length <= max) return value;
            return value.Substring(0, max) + "... (truncated)";
        }

        private static CaptainChatResponse Fail(string error)
        {
            return new CaptainChatResponse { Success = false, Error = error };
        }

        #endregion
    }
}
