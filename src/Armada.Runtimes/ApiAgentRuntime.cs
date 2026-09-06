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

            int processId = Interlocked.Increment(ref _PidCounter);
            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _Running[processId] = cts;

            OpenLog(logFilePath, prompt, endpoint);
            OnProcessStarted?.Invoke(processId);

            // Run the loop in the background so StartAsync returns the pid promptly, mirroring a process launch.
            _ = Task.Run(() => RunLoopAsync(processId, endpoint, workingDirectory, prompt, model, finalMessageFilePath, cts));

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
            CancellationTokenSource cts)
        {
            int exitCode = 0;
            string finalText = String.Empty;
            CancellationToken token = cts.Token;

            try
            {
                using CompletionClientBase client = _ClientFactory(endpoint, _Logging);
                if (!String.IsNullOrWhiteSpace(model)) client.Model = model;

                TaskPlan taskPlan = new TaskPlan();
                BuiltInToolRegistry registry = new BuiltInToolRegistry(taskPlan);
                List<PolyToolDefinition> tools = BuildToolDefinitions(registry);

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
                        string resultContent = await ExecuteToolAsync(processId, registry, call, workingDirectory, token).ConfigureAwait(false);
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
                _Logging.Warn(_Header + "loop error for process " + processId + ": " + e.Message);
                Emit(processId, "[error] " + e.Message);
            }
            finally
            {
                CloseLog();
                _Running.TryRemove(processId, out CancellationTokenSource? _);
                try { cts.Dispose(); } catch { }
                OnProcessExited?.Invoke(processId, exitCode);
            }
        }

        private async Task<string> ExecuteToolAsync(int processId, BuiltInToolRegistry registry, ToolCall call, string workingDirectory, CancellationToken token)
        {
            string argsJson = String.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson;
            Emit(processId, "[tool] " + call.Name + " " + Truncate(argsJson, 500));

            try
            {
                using JsonDocument document = JsonDocument.Parse(argsJson);
                ToolResult result = await registry.ExecuteAsync(call.Id ?? String.Empty, call.Name, document.RootElement, workingDirectory, token).ConfigureAwait(false);
                Emit(processId, "[tool:result] " + call.Name + " " + (result.Success ? "ok" : "failed") + " " + Truncate(result.Content, 500));
                return result.Content ?? String.Empty;
            }
            catch (JsonException)
            {
                string message = "Tool arguments were not valid JSON: " + Truncate(argsJson, 200);
                Emit(processId, "[tool:result] " + call.Name + " failed " + message);
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
