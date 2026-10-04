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
    /// the agent's final response is read from the runtime's final-message artifact. There is no separate
    /// model-endpoint (PolyPrompt) path: Mux, like the others, is a CLI that runs headless.
    /// Streaming events of the direct chat endpoint go only to the caller's own sockets; Ask Armada threads use
    /// <see cref="RunTurnAsync"/>, which takes a fully built prompt, a thread-scoped MCP token, and callbacks.
    /// </summary>
    public class CaptainChatService : IAskCaptainTurnRunner
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly AgentRuntimeFactory _RuntimeFactory;
        private readonly ArmadaWebSocketHub? _WebSocketHub;
        private readonly IPromptTemplateService? _PromptTemplates;
        private readonly ISessionTokenService? _SessionTokenService;
        private readonly int _McpPort;
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
        {
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
                options.OnTool = activity => SendToCaller(tenantId, userId, "ask.tool", new { turnId, phase = activity.Phase, id = activity.Id, name = activity.Name, arguments = activity.Arguments, ok = activity.Ok, elapsedMs = activity.ElapsedMs, result = activity.Result });
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
            int processId = -1;

            try
            {
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    runtime = _RuntimeFactory.Create(captain.Runtime);
                }
                catch (Exception e)
                {
                    return Failed("This captain's runtime (" + captain.Runtime + ") could not be launched for chat: " + e.Message);
                }

                // Claude Code streams token-by-token only in streaming-JSON mode; enable it for chat so the
                // reply renders incrementally and we can read a clean final message + metrics from the
                // terminal "result" event instead of scraping human-readable --print output.
                if (runtime is ClaudeCodeRuntime claudeRuntime)
                {
                    claudeRuntime.StreamJsonOutput = true;
                }

                TaskCompletionSource<int?> exitSource = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
                object outputLock = new object();
                StringBuilder output = new StringBuilder();
                StringBuilder thinking = new StringBuilder();
                DateTime startUtc = DateTime.UtcNow;
                DateTime? firstOutputUtc = null;

                // Per-turn telemetry harvested from the captain's own output. Mux emits JSONL protocol
                // events (run_started/assistant_text/run_completed) carrying model, duration, and token
                // estimates; CLI runtimes that only stream text still yield wall-clock timing.
                bool isMux = captain.Runtime == AgentRuntimeEnum.Mux;
                bool isOpenCode = captain.Runtime == AgentRuntimeEnum.OpenCode;
                bool isClaude = captain.Runtime == AgentRuntimeEnum.ClaudeCode;
                bool isApiEndpoint = captain.Runtime == AgentRuntimeEnum.ApiEndpoint;
                double? reportedDurationMs = null;
                int? reportedTokens = null;
                string? reportedModel = null;
                string? claudeFinalReply = null;
                
                runtime.OnStdoutReceived += (pid, line) =>
                {
                    // Structured tool-activity events from the in-process (ApiEndpoint) runtime arrive as a
                    // marked JSON line on stdout. Lift them into ask.tool card events (stamped with this
                    // turn's id) and never let the marker line leak into the accumulated reply text.
                    if (!String.IsNullOrEmpty(line) && line.StartsWith(ApiAgentRuntime.ToolEventMarker, StringComparison.Ordinal))
                    {
                        try
                        {
                            string toolJson = line.Substring(ApiAgentRuntime.ToolEventMarker.Length);
                            using (JsonDocument doc = JsonDocument.Parse(toolJson))
                            {
                                JsonElement root = doc.RootElement;
                                string? phase = root.TryGetProperty("phase", out JsonElement ph) && ph.ValueKind == JsonValueKind.String ? ph.GetString() : null;
                                string? id = root.TryGetProperty("id", out JsonElement idv) && idv.ValueKind == JsonValueKind.String ? idv.GetString() : null;
                                string? name = root.TryGetProperty("name", out JsonElement nmv) && nmv.ValueKind == JsonValueKind.String ? nmv.GetString() : null;
                                string? arguments = root.TryGetProperty("arguments", out JsonElement av) && av.ValueKind == JsonValueKind.String ? av.GetString() : null;
                                bool? ok = root.TryGetProperty("ok", out JsonElement okv) && (okv.ValueKind == JsonValueKind.True || okv.ValueKind == JsonValueKind.False) ? okv.GetBoolean() : (bool?)null;
                                double? elapsedMs = root.TryGetProperty("elapsedMs", out JsonElement elv) && elv.ValueKind == JsonValueKind.Number ? elv.GetDouble() : (double?)null;
                                string? resultText = root.TryGetProperty("result", out JsonElement rv) && rv.ValueKind == JsonValueKind.String ? rv.GetString() : null;
                                emitTool(new CaptainToolActivity { Phase = phase ?? "started", Id = id, Name = name, Arguments = arguments, Ok = ok, ElapsedMs = elapsedMs, Result = resultText });
                            }
                        }
                        catch (JsonException) { }
                        return;
                    }

                    // The in-process (ApiEndpoint) runtime interleaves human-readable diagnostics
                    // ([mcp] ..., [tool] ..., [tool:result] ..., and lifecycle [error]/[warning]/[cancelled])
                    // on the same stdout channel as the model's reply text. Those belong in the tool cards
                    // (delivered separately as TOOLEVENT lines), not in the answer -- drop them so they never
                    // stream into or accumulate as the reply.
                    if (isApiEndpoint && IsApiRuntimeDiagnostic(line))
                    {
                        return;
                    }

                    if (isClaude)
                    {
                        // Claude Code streaming-JSON: each stdout line is one JSON event. Incremental
                        // content_block_delta/text_delta events stream the reply token-by-token; the terminal
                        // "result" event carries the authoritative final message and metrics.
                        try
                        {
                            using (JsonDocument doc = JsonDocument.Parse(line.Trim()))
                            {
                                JsonElement root = doc.RootElement;
                                if (root.ValueKind != JsonValueKind.Object) return;
                                string eventType = root.TryGetProperty("type", out JsonElement ty) && ty.ValueKind == JsonValueKind.String
                                    ? ty.GetString() ?? String.Empty : String.Empty;

                                if (eventType == "stream_event"
                                    && root.TryGetProperty("event", out JsonElement ev) && ev.ValueKind == JsonValueKind.Object
                                    && ev.TryGetProperty("type", out JsonElement evt) && evt.ValueKind == JsonValueKind.String
                                    && evt.GetString() == "content_block_delta"
                                    && ev.TryGetProperty("delta", out JsonElement delta) && delta.ValueKind == JsonValueKind.Object
                                    && delta.TryGetProperty("type", out JsonElement dty) && dty.ValueKind == JsonValueKind.String
                                    && dty.GetString() == "text_delta"
                                    && delta.TryGetProperty("text", out JsonElement dtx) && dtx.ValueKind == JsonValueKind.String)
                                {
                                    string? deltaText = dtx.GetString();
                                    if (!String.IsNullOrEmpty(deltaText))
                                    {
                                        lock (outputLock)
                                        {
                                            if (firstOutputUtc == null) firstOutputUtc = DateTime.UtcNow;
                                            if (output.Length < _MaxOutputChars) output.Append(deltaText);
                                        }
                                        emitChunk(deltaText!);
                                    }
                                }
                                else if (eventType == "assistant" || eventType == "user")
                                {
                                    ObserveClaudeToolBlocks(line, toolCalls, emitTool);
                                }

                                if (eventType == "assistant"
                                    && root.TryGetProperty("message", out JsonElement assistantMessage)
                                    && assistantMessage.ValueKind == JsonValueKind.Object
                                    && assistantMessage.TryGetProperty("model", out JsonElement am) && am.ValueKind == JsonValueKind.String)
                                {
                                    lock (outputLock) reportedModel = am.GetString();
                                }
                                else if (eventType == "result")
                                {
                                    lock (outputLock)
                                    {
                                        if (root.TryGetProperty("result", out JsonElement resultText) && resultText.ValueKind == JsonValueKind.String)
                                            claudeFinalReply = resultText.GetString();
                                        if (root.TryGetProperty("duration_ms", out JsonElement durMs) && durMs.ValueKind == JsonValueKind.Number)
                                            reportedDurationMs = durMs.GetDouble();
                                        if (root.TryGetProperty("usage", out JsonElement usage) && usage.ValueKind == JsonValueKind.Object
                                            && usage.TryGetProperty("output_tokens", out JsonElement outTok) && outTok.ValueKind == JsonValueKind.Number)
                                            reportedTokens = outTok.GetInt32();
                                    }
                                }
                            }
                        }
                        catch (JsonException) { }
                        return;
                    }

                    if (isMux && MuxRuntime.IsProtocolEventLine(line))
                    {
                        // Parse the Mux event for telemetry and live text. The final reply still comes
                        // from the final-message artifact; assistant_text carries the streamed deltas.
                        try
                        {
                            using (JsonDocument doc = JsonDocument.Parse(line.Trim()))
                            {
                                JsonElement root = doc.RootElement;
                                string eventType = root.TryGetProperty("eventType", out JsonElement et) && et.ValueKind == JsonValueKind.String
                                    ? et.GetString() ?? String.Empty : String.Empty;

                                string? deltaText = null;
                                lock (outputLock)
                                {
                                    if (root.TryGetProperty("model", out JsonElement m) && m.ValueKind == JsonValueKind.String)
                                        reportedModel = m.GetString();
                                    if (eventType == "assistant_text")
                                    {
                                        if (firstOutputUtc == null) firstOutputUtc = DateTime.UtcNow;
                                        if (root.TryGetProperty("text", out JsonElement tx) && tx.ValueKind == JsonValueKind.String)
                                        {
                                            deltaText = tx.GetString();
                                            // Accumulate streamed assistant text so a reply survives even if
                                            // the final-message artifact (reply.txt) is not written.
                                            if (!String.IsNullOrEmpty(deltaText) && output.Length < _MaxOutputChars)
                                                output.Append(deltaText);
                                        }
                                    }
                                    else if (eventType == "run_completed")
                                    {
                                        if (root.TryGetProperty("durationMs", out JsonElement d) && d.ValueKind == JsonValueKind.Number)
                                            reportedDurationMs = d.GetDouble();
                                        if (root.TryGetProperty("finalEstimatedTokens", out JsonElement ft) && ft.ValueKind == JsonValueKind.Number)
                                            reportedTokens = ft.GetInt32();
                                    }
                                }

                                // When --show-thinking is active, Mux streams the model's reasoning as
                                // assistant_thinking events on a separate channel from the answer.
                                string? thinkingDelta = null;
                                if (eventType == "assistant_thinking"
                                    && root.TryGetProperty("text", out JsonElement think)
                                    && think.ValueKind == JsonValueKind.String)
                                {
                                    thinkingDelta = think.GetString();
                                    if (!String.IsNullOrEmpty(thinkingDelta))
                                    {
                                        lock (outputLock)
                                        {
                                            if (thinking.Length < _MaxOutputChars) thinking.Append(thinkingDelta);
                                        }
                                    }
                                }

                                if (!String.IsNullOrEmpty(deltaText)) emitChunk(deltaText!);
                                if (!String.IsNullOrEmpty(thinkingDelta)) emitThinking(thinkingDelta!);

                                // Surface tool activity to the chat UI: when a tool call is proposed and when
                                // it completes (with success/failure, runtime, and result for inspection).
                                if (eventType == "tool_call_proposed" && root.TryGetProperty("toolCall", out JsonElement proposed))
                                {
                                    string? toolId = proposed.TryGetProperty("id", out JsonElement propId) && propId.ValueKind == JsonValueKind.String ? propId.GetString() : null;
                                    string? toolName = proposed.TryGetProperty("name", out JsonElement pnm) && pnm.ValueKind == JsonValueKind.String ? pnm.GetString() : null;
                                    string? argsJson = proposed.TryGetProperty("arguments", out JsonElement parg) ? Truncate(parg.GetRawText(), 4000) : null;
                                    emitTool(new CaptainToolActivity { Phase = "started", Id = toolId, Name = toolName, Arguments = argsJson });
                                }
                                else if (eventType == "tool_call_completed")
                                {
                                    string? toolId = root.TryGetProperty("toolCallId", out JsonElement cid) && cid.ValueKind == JsonValueKind.String ? cid.GetString() : null;
                                    string? toolName = root.TryGetProperty("toolName", out JsonElement cnm) && cnm.ValueKind == JsonValueKind.String ? cnm.GetString() : null;
                                    double? elapsedMs = root.TryGetProperty("elapsedMs", out JsonElement cel) && cel.ValueKind == JsonValueKind.Number ? cel.GetDouble() : (double?)null;
                                    bool? ok = null;
                                    string? resultJson = null;
                                    if (root.TryGetProperty("result", out JsonElement res))
                                    {
                                        if (res.TryGetProperty("success", out JsonElement suc) && (suc.ValueKind == JsonValueKind.True || suc.ValueKind == JsonValueKind.False))
                                            ok = suc.GetBoolean();
                                        JsonElement resultBody = res.TryGetProperty("content", out JsonElement content) ? content : res;
                                        resultJson = Truncate(resultBody.GetRawText(), 16000);
                                    }
                                    emitTool(new CaptainToolActivity { Phase = "completed", Id = toolId, Name = toolName, Ok = ok, ElapsedMs = elapsedMs, Result = resultJson });
                                }
                            }
                        }
                        catch (JsonException) { }
                        return;
                    }

                    if (isOpenCode && OpenCodeRuntime.IsProtocolEventLine(line))
                    {
                        // OpenCode --format json streams "type"-tagged events with a nested "part". Surface
                        // assistant text and tool-call chips; drop the raw JSON envelope so it never leaks.
                        try
                        {
                            using (JsonDocument doc = JsonDocument.Parse(line.Trim()))
                            {
                                JsonElement root = doc.RootElement;
                                string ocType = root.TryGetProperty("type", out JsonElement oct) && oct.ValueKind == JsonValueKind.String ? oct.GetString() ?? "" : "";
                                JsonElement part = root.TryGetProperty("part", out JsonElement p) && p.ValueKind == JsonValueKind.Object ? p : default;

                                if (ocType == "text" && part.ValueKind == JsonValueKind.Object
                                    && part.TryGetProperty("text", out JsonElement txt) && txt.ValueKind == JsonValueKind.String)
                                {
                                    string deltaText = txt.GetString() ?? String.Empty;
                                    if (!String.IsNullOrEmpty(deltaText))
                                    {
                                        lock (outputLock)
                                        {
                                            if (firstOutputUtc == null) firstOutputUtc = DateTime.UtcNow;
                                            if (output.Length < _MaxOutputChars) output.Append(deltaText);
                                        }
                                        emitChunk(deltaText);
                                    }
                                }
                                else if (ocType == "reasoning" && part.ValueKind == JsonValueKind.Object
                                    && part.TryGetProperty("text", out JsonElement rtxt) && rtxt.ValueKind == JsonValueKind.String)
                                {
                                    // OpenCode --thinking streams reasoning on a separate channel; surface it as
                                    // thinking (never as reply text).
                                    string thinkingDelta = rtxt.GetString() ?? String.Empty;
                                    if (!String.IsNullOrEmpty(thinkingDelta) && showThinking)
                                    {
                                        lock (outputLock)
                                        {
                                            if (thinking.Length < _MaxOutputChars) thinking.Append(thinkingDelta);
                                        }
                                        emitThinking(thinkingDelta);
                                    }
                                }
                                else if (ocType == "tool_use" && part.ValueKind == JsonValueKind.Object)
                                {
                                    string? toolName = part.TryGetProperty("tool", out JsonElement tnm) && tnm.ValueKind == JsonValueKind.String ? tnm.GetString() : null;
                                    string? toolId = part.TryGetProperty("callID", out JsonElement cid) && cid.ValueKind == JsonValueKind.String ? cid.GetString() : null;
                                    string? status = null;
                                    string? argsJson = null;
                                    string? resultJson = null;
                                    bool? ok = null;
                                    if (part.TryGetProperty("state", out JsonElement state) && state.ValueKind == JsonValueKind.Object)
                                    {
                                        status = state.TryGetProperty("status", out JsonElement stt) && stt.ValueKind == JsonValueKind.String ? stt.GetString() : null;
                                        if (state.TryGetProperty("input", out JsonElement inp)) argsJson = Truncate(inp.GetRawText(), 4000);
                                        if (state.TryGetProperty("output", out JsonElement outp) && outp.ValueKind == JsonValueKind.String) resultJson = Truncate(outp.GetString() ?? "", 16000);
                                        if (state.TryGetProperty("metadata", out JsonElement md) && md.ValueKind == JsonValueKind.Object
                                            && md.TryGetProperty("exit", out JsonElement ex) && ex.ValueKind == JsonValueKind.Number)
                                            ok = ex.GetInt32() == 0;
                                    }
                                    string phase = String.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) ? "completed" : "started";
                                    emitTool(new CaptainToolActivity { Phase = phase, Id = toolId, Name = toolName, Arguments = argsJson, Ok = ok, Result = resultJson });
                                }
                            }
                        }
                        catch (JsonException) { }
                        return;
                    }

                    lock (outputLock)
                    {
                        if (firstOutputUtc == null) firstOutputUtc = DateTime.UtcNow;
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
                if (!String.IsNullOrEmpty(options.McpSessionToken) && _McpPort > 0)
                {
                    if (captain.Runtime == AgentRuntimeEnum.ApiEndpoint)
                    {
                        // Use the same canonical MCP URL captains' generated configs target (http://localhost:<port>/mcp).
                        // The MCP listener binds to the configured hostname (default "localhost"), and Windows HTTP.sys
                        // rejects a request whose Host header does not match the registered prefix.
                        environment = new Dictionary<string, string>
                        {
                            ["ARMADA_MCP_URL"] = Armada.Core.Services.ArmadaMcpConfigBuilder.GetMcpUrl(_McpPort),
                            ["ARMADA_MCP_TOKEN"] = options.McpSessionToken!
                        };
                    }
                    else if (scopedMcp
                        && runtime is BaseAgentRuntime scopedRuntime
                        && Armada.Core.Services.CaptainThreadMcpPlanner.SupportsApprovalGating(captain.Runtime))
                    {
                        scopedRuntime.McpSessionToken = options.McpSessionToken;
                        isolateLaunch = true;
                    }
                }

                processId = await runtime.StartAsync(
                    workingDirectory,
                    prompt,
                    environment: environment,
                    finalMessageFilePath: finalMessageFilePath,
                    model: captain.Model,
                    captain: captain,
                    isolateLaunch: isolateLaunch,
                    mcpPort: _McpPort,
                    showThinking: showThinking,
                    token: token).ConfigureAwait(false);

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

                // Keep all timing on one wall-clock base so time-to-first-token never exceeds total and
                // streaming always resolves. reportedDurationMs is captain-internal and, being on a
                // different base than our wall clock, is used only as a fallback total.
                double wallClockMs = (DateTime.UtcNow - startUtc).TotalMilliseconds;
                double totalMs = wallClockMs > 0 ? wallClockMs : (reportedDurationMs ?? wallClockMs);
                double? ttftMs = firstOutputUtc.HasValue
                    ? Math.Min((firstOutputUtc.Value - startUtc).TotalMilliseconds, totalMs)
                    : (double?)null;

                // Only Claude Code reports a real completion-token count (output_tokens from its "result"
                // event). Mux's reportedTokens is finalEstimatedTokens -- a whole-context estimate, not the
                // reply -- so it is NOT passed here; the shared builder estimates completion tokens from the
                // reply text instead, matching planning-session metrics.
                int? realCompletionTokens = isClaude ? reportedTokens : null;

                string thinkingText;
                lock (outputLock) thinkingText = thinking.ToString().Trim();

                CaptainChatResponse response = new CaptainChatResponse
                {
                    Success = true,
                    Reply = reply,
                    Thinking = String.IsNullOrEmpty(thinkingText) ? null : thinkingText,
                    Model = !String.IsNullOrEmpty(reportedModel) ? reportedModel
                        : (String.IsNullOrEmpty(captain.Model) ? captain.Runtime.ToString() : captain.Model),
                    Metrics = Armada.Core.Services.ChatTurnMetricsBuilder.Build(totalMs, ttftMs, reply, realCompletionTokens),
                };

                // Best-effort token accounting: real output tokens for Claude Code, estimated otherwise.
                await Armada.Core.Services.TokenUsageCapture.CaptureAsync(
                    _Database, _Logging, "chat",
                    model: response.Model,
                    runtime: captain.Runtime.ToString(),
                    tenantId: options.TenantId ?? captain.TenantId,
                    userId: options.UserId,
                    vesselId: null,
                    captainId: captain.Id,
                    sourceId: captain.CurrentMissionId,
                    inputTokens: null,
                    outputTokens: realCompletionTokens.HasValue ? (long?)realCompletionTokens.Value : null,
                    cachedTokens: null,
                    inputText: prompt,
                    outputText: reply,
                    token: token).ConfigureAwait(false);

                _Logging.Debug(_Header + "chat turn for captain " + captainId + " (" + captain.Runtime + "): " +
                    (response.Metrics.TotalMs?.ToString("F0") ?? "?") + "ms, exit " + (exitCode?.ToString() ?? "?"));

                return new CaptainChatTurnResult { Response = response, ToolCalls = toolCalls.ToList() };
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

        private static void ObserveClaudeToolBlocks(string line, ToolCallCollector collector, Action<CaptainToolActivity> emitTool)
        {
            ClaudeStreamLine? parsed = null;
            try { parsed = JsonSerializer.Deserialize<ClaudeStreamLine>(line.Trim()); }
            catch (JsonException) { return; }
            if (parsed?.Message?.Content == null) return;

            foreach (ClaudeStreamContentBlock block in parsed.Message.Content)
            {
                if (block == null) continue;
                if (String.Equals(block.Type, "tool_use", StringComparison.Ordinal))
                {
                    emitTool(new CaptainToolActivity
                    {
                        Phase = "started",
                        Id = block.Id,
                        Name = block.Name,
                        Arguments = Truncate(block.Input?.ToJsonString(), 4000)
                    });
                }
                else if (String.Equals(block.Type, "tool_result", StringComparison.Ordinal))
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

        /// <summary>
        /// Whether a stdout line from the in-process (ApiEndpoint) runtime is diagnostic chatter (MCP status,
        /// tool call/result echoes, or a lifecycle marker) rather than the model's reply text. Such lines are
        /// surfaced as tool cards separately and must not leak into the answer.
        /// </summary>
        private static bool IsApiRuntimeDiagnostic(string line)
        {
            if (String.IsNullOrEmpty(line)) return false;
            return line.StartsWith("[mcp]", StringComparison.Ordinal)
                || line.StartsWith("[tool]", StringComparison.Ordinal)
                || line.StartsWith("[tool:result]", StringComparison.Ordinal)
                || line.StartsWith("[error]", StringComparison.Ordinal)
                || line.StartsWith("[warning]", StringComparison.Ordinal)
                || line.StartsWith("[cancelled]", StringComparison.Ordinal);
        }

        private static CaptainChatResponse Fail(string error)
        {
            return new CaptainChatResponse { Success = false, Error = error };
        }

        #endregion
    }
}
