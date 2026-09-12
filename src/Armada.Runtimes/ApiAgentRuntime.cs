namespace Armada.Runtimes
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes.Interfaces;
    using Armada.Runtimes.Mcp;
    using Armada.Runtimes.Tools;
    using Armada.Runtimes.Tools.Tasks;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;
    using ArmadaToolDefinition = Armada.Runtimes.Tools.ToolDefinition;
    using PolyToolDefinition = PolyPrompt.Models.ToolDefinition;

    /// <summary>
    /// An agent runtime that drives a configured inference <see cref="ModelEndpoint"/> as a captain. Instead of
    /// spawning a CLI harness, it runs an in-process tool-calling loop (via PolyPrompt) over the Armada
    /// built-in coding tools, executing every tool against the mission's working directory. It surfaces the
    /// same <see cref="IAgentRuntime"/> events as a process-backed runtime (started, output, exited), so the
    /// agent lifecycle handler treats it identically. Processless: it uses a synthetic process id and
    /// cooperative cancellation keyed by that id.
    /// </summary>
    public class ApiAgentRuntime : IAgentRuntime
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name => _Endpoint != null ? "ApiEndpoint:" + _Endpoint.Name : "ApiEndpoint";

        /// <inheritdoc />
        public bool SupportsResume => false;

        /// <inheritdoc />
        public bool SupportsPlanningSessions => false;

        /// <inheritdoc />
        public event Action<int, string>? OnOutputReceived;

        /// <inheritdoc />
        public event Action<int, string>? OnStdoutReceived;

        /// <inheritdoc />
        public event Action<int>? OnProcessStarted;

        /// <inheritdoc />
        public event Action<int, int?>? OnProcessExited;

        /// <summary>
        /// Sentinel prefix for structured tool-activity lines emitted on the stdout channel. Consumers that
        /// render chat tool cards recognize a line beginning with this marker as a JSON tool event rather
        /// than reply text.
        /// </summary>
        public const string ToolEventMarker = "[ARMADA:TOOLEVENT] ";

        #endregion

        #region Private-Members

        // Synthetic process ids start well above any real OS process id so a stray Process.GetProcessById does
        // not collide with an unrelated live process.
        private static int _PidCounter = 2_000_000_000;
        private static readonly ConcurrentDictionary<int, CancellationTokenSource> _Running = new ConcurrentDictionary<int, CancellationTokenSource>();

        private readonly ModelEndpoint? _Endpoint;
        private readonly Func<string, ModelEndpoint?>? _EndpointResolver;
        private readonly LoggingModule _Logging;
        private readonly int _MaxIterations;
        private readonly Func<ModelEndpoint, LoggingModule, CompletionClientBase> _ClientFactory;
        private readonly string _Header = "[ApiAgentRuntime] ";
        private StreamWriter? _LogWriter;
        private readonly object _LogLock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a specific inference endpoint.
        /// </summary>
        /// <param name="endpoint">The inference model endpoint to drive.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="maxIterations">Maximum tool-call iterations before the loop stops. Clamped to 1..1000.</param>
        /// <param name="clientFactory">Optional inference-client factory seam for testing; defaults to the
        /// production <see cref="ModelEndpointClientFactory"/>.</param>
        public ApiAgentRuntime(
            ModelEndpoint endpoint,
            LoggingModule logging,
            int maxIterations = 100,
            Func<ModelEndpoint, LoggingModule, CompletionClientBase>? clientFactory = null)
        {
            _Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _MaxIterations = Math.Clamp(maxIterations, 1, 1000);
            _ClientFactory = clientFactory ?? ((ep, log) => ModelEndpointClientFactory.Create(ep, log));
        }

        /// <summary>
        /// Instantiate with a resolver that maps a captain's endpoint id to a <see cref="ModelEndpoint"/> at
        /// launch time. Used in production where the endpoint is chosen per captain.
        /// </summary>
        /// <param name="endpointResolver">Resolves a model-endpoint id to the endpoint, or null when not found.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="maxIterations">Maximum tool-call iterations before the loop stops. Clamped to 1..1000.</param>
        /// <param name="clientFactory">Optional inference-client factory seam for testing.</param>
        public ApiAgentRuntime(
            Func<string, ModelEndpoint?> endpointResolver,
            LoggingModule logging,
            int maxIterations = 100,
            Func<ModelEndpoint, LoggingModule, CompletionClientBase>? clientFactory = null)
        {
            _EndpointResolver = endpointResolver ?? throw new ArgumentNullException(nameof(endpointResolver));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _MaxIterations = Math.Clamp(maxIterations, 1, 1000);
            _ClientFactory = clientFactory ?? ((ep, log) => ModelEndpointClientFactory.Create(ep, log));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether an API-endpoint captain loop is currently running under the given synthetic process id.
        /// </summary>
        /// <param name="processId">Synthetic process id.</param>
        /// <returns>True when the loop is tracked and alive.</returns>
        public static bool IsTracked(int processId)
        {
            return _Running.ContainsKey(processId);
        }

        /// <inheritdoc />
        public Task<int> StartAsync(
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
            if (String.IsNullOrWhiteSpace(workingDirectory)) throw new ArgumentNullException(nameof(workingDirectory));

            ModelEndpoint? endpoint = _Endpoint;
            if (endpoint == null && _EndpointResolver != null && captain != null && !String.IsNullOrEmpty(captain.ModelEndpointId))
                endpoint = _EndpointResolver(captain.ModelEndpointId);

            if (endpoint == null)
                throw new InvalidOperationException("API-endpoint captain has no resolvable inference endpoint. Set the captain's model endpoint to a configured Inference endpoint.");

            // Optional MCP tool access: when the caller supplies an MCP endpoint (and, typically, a
            // short-lived per-user session token) the in-process loop also exposes that server's tools to
            // the model alongside the built-in coding tools. This is what lets an Ask Armada chat backed by
            // an inference endpoint actually drive Armada's own orchestration tools, scoped to the caller.
            string? mcpUrl = null;
            string? mcpToken = null;
            if (environment != null)
            {
                environment.TryGetValue("ARMADA_MCP_URL", out mcpUrl);
                environment.TryGetValue("ARMADA_MCP_TOKEN", out mcpToken);
            }

            int processId = Interlocked.Increment(ref _PidCounter);
            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _Running[processId] = cts;

            OpenLog(logFilePath, prompt, endpoint);
            OnProcessStarted?.Invoke(processId);

            // Run the loop in the background so StartAsync returns the pid promptly, mirroring a process launch.
            _ = Task.Run(() => RunLoopAsync(processId, endpoint, workingDirectory, prompt, model, finalMessageFilePath, mcpUrl, mcpToken, cts));

            return Task.FromResult(processId);
        }

        /// <inheritdoc />
        public Task StopAsync(int processId, CancellationToken token = default)
        {
            if (_Running.TryGetValue(processId, out CancellationTokenSource? cts))
            {
                try { cts.Cancel(); } catch { }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<bool> IsRunningAsync(int processId, CancellationToken token = default)
        {
            return Task.FromResult(_Running.ContainsKey(processId));
        }

        #endregion

        #region Private-Methods

        private async Task RunLoopAsync(
            int processId,
            ModelEndpoint endpoint,
            string workingDirectory,
            string prompt,
            string? model,
            string? finalMessageFilePath,
            string? mcpUrl,
            string? mcpToken,
            CancellationTokenSource cts)
        {
            int exitCode = 0;
            string finalText = String.Empty;
            CancellationToken token = cts.Token;
            McpToolClient? mcpClient = null;

            try
            {
                using CompletionClientBase client = _ClientFactory(endpoint, _Logging);
                if (!String.IsNullOrWhiteSpace(model)) client.Model = model;

                TaskPlan taskPlan = new TaskPlan();
                BuiltInToolRegistry registry = new BuiltInToolRegistry(taskPlan);
                List<PolyToolDefinition> tools = BuildToolDefinitions(registry);

                // Merge in the tools advertised by the optional MCP endpoint. Built-in tool names always win
                // on a collision so a remote server can never shadow the local file/process tools. The
                // routing map records which tool names must be dispatched to the MCP client rather than the
                // local registry.
                Dictionary<string, McpToolClient> mcpRouting = new Dictionary<string, McpToolClient>(StringComparer.Ordinal);
                if (!String.IsNullOrWhiteSpace(mcpUrl))
                {
                    HashSet<string> builtInNames = new HashSet<string>(StringComparer.Ordinal);
                    foreach (PolyToolDefinition builtIn in tools) builtInNames.Add(builtIn.Name);

                    try
                    {
                        mcpClient = new McpToolClient(mcpUrl!, mcpToken, null, _Logging);
                        await mcpClient.InitializeAsync(token).ConfigureAwait(false);
                        List<McpRemoteTool> remoteTools = await mcpClient.ListToolsAsync(token).ConfigureAwait(false);

                        int added = 0;
                        foreach (McpRemoteTool remote in remoteTools)
                        {
                            if (builtInNames.Contains(remote.Name) || mcpRouting.ContainsKey(remote.Name)) continue;
                            Dictionary<string, object> parameters = SchemaJsonToDictionary(remote.InputSchemaJson);
                            tools.Add(PolyToolDefinition.Function(remote.Name, remote.Description, parameters));
                            mcpRouting[remote.Name] = mcpClient;
                            added++;
                        }

                        Emit(processId, "[mcp] connected to " + mcpUrl + "; " + added + " tool(s) available");
                    }
                    catch (Exception ex)
                    {
                        Emit(processId, "[mcp] tool access unavailable (" + ex.Message + "); continuing with built-in tools only");
                        try { mcpClient?.Dispose(); } catch { }
                        mcpClient = null;
                        mcpRouting.Clear();
                    }
                }

                List<ChatMessage> messages = new List<ChatMessage>();
                messages.Add(ChatMessage.System(BuildSystemPrompt(workingDirectory)));
                messages.Add(ChatMessage.User(prompt));

                for (int iteration = 0; iteration < _MaxIterations; iteration++)
                {
                    token.ThrowIfCancellationRequested();

                    ToolChatRequest request = new ToolChatRequest();
                    request.Messages = messages;
                    request.Tools = tools;
                    if (!String.IsNullOrWhiteSpace(model)) request.Model = model;

                    ToolChatResponse response = await client.ToolChatAsync(request, token).ConfigureAwait(false);

                    if (!response.Success && !String.IsNullOrEmpty(response.Error))
                    {
                        Emit(processId, "[error] inference call failed: " + response.Error);
                        exitCode = 1;
                        break;
                    }

                    if (!String.IsNullOrEmpty(response.Text))
                    {
                        finalText = response.Text!;
                        Emit(processId, response.Text!);
                    }

                    messages.Add(response.ToAssistantMessage());

                    if (response.ToolCalls == null || response.ToolCalls.Count == 0)
                    {
                        // No more tool calls: the model has finished.
                        break;
                    }

                    foreach (ToolCall call in response.ToolCalls)
                    {
                        token.ThrowIfCancellationRequested();
                        string resultContent = await ExecuteToolAsync(processId, registry, mcpRouting, call, workingDirectory, token).ConfigureAwait(false);
                        messages.Add(ChatMessage.ToolResult(call.Id, call.Name, resultContent));
                    }

                    if (iteration == _MaxIterations - 1)
                    {
                        Emit(processId, "[warning] reached the maximum of " + _MaxIterations + " tool iterations; stopping.");
                    }
                }

                WriteFinalMessage(finalMessageFilePath, finalText);
            }
            catch (OperationCanceledException)
            {
                exitCode = -1;
                Emit(processId, "[cancelled] the captain run was stopped.");
            }
            catch (Exception e)
            {
                exitCode = 1;
                _Logging.Warn(_Header + "loop error for process " + processId + ": " + e.ToString());
                Emit(processId, "[error] " + e.Message);
            }
            finally
            {
                try { mcpClient?.Dispose(); } catch { }
                CloseLog();
                _Running.TryRemove(processId, out CancellationTokenSource? _);
                try { cts.Dispose(); } catch { }
                OnProcessExited?.Invoke(processId, exitCode);
            }
        }

        private async Task<string> ExecuteToolAsync(
            int processId,
            BuiltInToolRegistry registry,
            Dictionary<string, McpToolClient> mcpRouting,
            ToolCall call,
            string workingDirectory,
            CancellationToken token)
        {
            string argsJson = String.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson;
            Emit(processId, "[tool] " + call.Name + " " + Truncate(argsJson, 500));
            EmitToolEvent(processId, new { phase = "started", id = call.Id, name = call.Name, arguments = Truncate(argsJson, 4000) });

            long startTicks = Environment.TickCount64;

            // Route to the MCP endpoint when the tool belongs to it; otherwise run the built-in local tool.
            if (mcpRouting.TryGetValue(call.Name, out McpToolClient? mcpClient) && mcpClient != null)
            {
                try
                {
                    string mcpResult = await mcpClient.CallToolAsync(call.Name, argsJson, token).ConfigureAwait(false);
                    long elapsed = Environment.TickCount64 - startTicks;
                    Emit(processId, "[tool:result] " + call.Name + " ok " + Truncate(mcpResult, 500));
                    EmitToolEvent(processId, new { phase = "completed", id = call.Id, name = call.Name, ok = true, elapsedMs = elapsed, result = Truncate(mcpResult, 16000) });
                    return mcpResult;
                }
                catch (Exception ex)
                {
                    long elapsed = Environment.TickCount64 - startTicks;
                    string message = "MCP tool call failed: " + ex.Message;
                    Emit(processId, "[tool:result] " + call.Name + " failed " + message);
                    EmitToolEvent(processId, new { phase = "completed", id = call.Id, name = call.Name, ok = false, elapsedMs = elapsed, result = message });
                    return JsonSerializer.Serialize(new { error = "mcp_tool_failed", message });
                }
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(argsJson);
                ToolResult result = await registry.ExecuteAsync(call.Id ?? String.Empty, call.Name, document.RootElement, workingDirectory, token).ConfigureAwait(false);
                long elapsed = Environment.TickCount64 - startTicks;
                Emit(processId, "[tool:result] " + call.Name + " " + (result.Success ? "ok" : "failed") + " " + Truncate(result.Content, 500));
                EmitToolEvent(processId, new { phase = "completed", id = call.Id, name = call.Name, ok = result.Success, elapsedMs = elapsed, result = Truncate(result.Content, 16000) });
                return result.Content ?? String.Empty;
            }
            catch (JsonException)
            {
                string message = "Tool arguments were not valid JSON: " + Truncate(argsJson, 200);
                Emit(processId, "[tool:result] " + call.Name + " failed " + message);
                EmitToolEvent(processId, new { phase = "completed", id = call.Id, name = call.Name, ok = false, result = message });
                return JsonSerializer.Serialize(new { error = "invalid_arguments", message });
            }
        }

        private List<PolyToolDefinition> BuildToolDefinitions(BuiltInToolRegistry registry)
        {
            List<PolyToolDefinition> definitions = new List<PolyToolDefinition>();
            foreach (ArmadaToolDefinition tool in registry.GetToolDefinitions())
            {
                Dictionary<string, object> parameters = SchemaToDictionary(tool.ParametersSchema);
                definitions.Add(PolyToolDefinition.Function(tool.Name, tool.Description, parameters));
            }

            return definitions;
        }

        private static Dictionary<string, object> SchemaToDictionary(object schema)
        {
            try
            {
                string json = JsonSerializer.Serialize(schema);
                Dictionary<string, object>? parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                return parsed ?? new Dictionary<string, object>();
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        private static Dictionary<string, object> SchemaJsonToDictionary(string? schemaJson)
        {
            if (String.IsNullOrWhiteSpace(schemaJson))
                return new Dictionary<string, object> { ["type"] = "object" };

            try
            {
                Dictionary<string, object>? parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(schemaJson);
                return parsed ?? new Dictionary<string, object> { ["type"] = "object" };
            }
            catch
            {
                return new Dictionary<string, object> { ["type"] = "object" };
            }
        }

        private string BuildSystemPrompt(string workingDirectory)
        {
            return
                "You are an autonomous coding agent operating as an Armada captain. You are working inside a git " +
                "worktree at " + workingDirectory + ". Use the provided tools to inspect and modify files and to run " +
                "commands in order to complete the mission described by the user. Make focused changes, verify your " +
                "work, and when the mission is complete stop calling tools and reply with a concise summary of what " +
                "you changed. If the mission instructions define [ARMADA:...] signals, emit them as plain text lines.";
        }

        private void Emit(int processId, string line)
        {
            if (String.IsNullOrEmpty(line)) return;
            WriteLog(line);
            try { OnOutputReceived?.Invoke(processId, line); } catch { }
            try { OnStdoutReceived?.Invoke(processId, line); } catch { }
        }

        /// <summary>
        /// Emit a structured tool-activity event on the stdout channel, marked with a sentinel prefix so a
        /// consumer (the chat service) can lift it out into a UI tool card instead of treating it as reply
        /// text. The payload carries the tool name, phase (started/completed), and, on completion, success,
        /// elapsed time, and a truncated result.
        /// </summary>
        private void EmitToolEvent(int processId, object payload)
        {
            try
            {
                string line = ToolEventMarker + JsonSerializer.Serialize(payload);
                WriteLog(line);
                OnStdoutReceived?.Invoke(processId, line);
            }
            catch { }
        }

        private static string Truncate(string? value, int max)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            string single = value!.Replace("\r", " ").Replace("\n", " ");
            return single.Length <= max ? single : single.Substring(0, max) + "...";
        }

        private void OpenLog(string? logFilePath, string prompt, ModelEndpoint endpoint)
        {
            if (String.IsNullOrEmpty(logFilePath)) return;
            try
            {
                lock (_LogLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
                    _LogWriter = new StreamWriter(logFilePath, append: true) { AutoFlush = true };
                    string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    _LogWriter.WriteLine("[" + timestamp + "] API-endpoint captain starting: " + endpoint.Name + " (" + endpoint.Provider + "/" + (endpoint.Model ?? "default") + ")");
                    if (!String.IsNullOrEmpty(prompt)) _LogWriter.WriteLine(prompt);
                    _LogWriter.WriteLine(String.Empty);
                }
            }
            catch
            {
                _LogWriter = null;
            }
        }

        private void WriteLog(string data)
        {
            lock (_LogLock)
            {
                try { _LogWriter?.WriteLine(data); }
                catch (ObjectDisposedException) { }
                catch { }
            }
        }

        private void CloseLog()
        {
            lock (_LogLock)
            {
                try { _LogWriter?.Flush(); } catch { }
                try { _LogWriter?.Dispose(); } catch { }
                _LogWriter = null;
            }
        }

        private void WriteFinalMessage(string? finalMessageFilePath, string finalText)
        {
            if (String.IsNullOrEmpty(finalMessageFilePath) || String.IsNullOrEmpty(finalText)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(finalMessageFilePath)!);
                File.WriteAllText(finalMessageFilePath, finalText);
            }
            catch (Exception e)
            {
                _Logging.Debug(_Header + "could not write final message file: " + e.Message);
            }
        }

        #endregion
    }
}
