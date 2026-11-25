namespace Test.Shared.Infrastructure.OpenModel
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A loopback model server that speaks the Ollama API (<c>/api/chat</c>, <c>/api/show</c>, <c>/api/ps</c>,
    /// <c>/api/tags</c>) and the OpenAI-compatible API (<c>/v1/chat/completions</c>, <c>/v1/models</c>) and answers
    /// each chat request with the next <see cref="SimulatedTurn"/> in its script. It reproduces open-model and
    /// model-server behavior on demand (text tool calls, malformed arguments, Ollama's tool-call parse 500, local 429
    /// backpressure, runner crashes, context overflow, truncation, leaked reasoning, hangs) so the runtime and
    /// recovery code can be tested without a real model. Routes may be prefixed (for example a Conductor-style
    /// <c>/v1.0/api/qwen/api/chat</c>); matching uses the path suffix.
    /// </summary>
    /// <remarks>Thread safety: the script and request log are guarded by a lock; one listener loop serves requests.</remarks>
    public sealed class OpenModelSimulator : IDisposable
    {
        #region Public-Members

        /// <summary>Base URL, <c>http://127.0.0.1:port</c>.</summary>
        public string BaseUrl { get; }

        /// <summary>Model name reported by the metadata routes and chat replies. Default <c>sim-model</c>.</summary>
        public string ModelName
        {
            get => _ModelName;
            set => _ModelName = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(ModelName)) : value;
        }

        /// <summary>
        /// Context length reported by <c>/api/show</c>, <c>/api/ps</c> and <c>/v1/models</c>. Default 32768, minimum
        /// 512, maximum 10485760.
        /// </summary>
        public int ContextLength
        {
            get => _ContextLength;
            set
            {
                if (value < 512 || value > 10485760) throw new ArgumentOutOfRangeException(nameof(ContextLength), "ContextLength must be between 512 and 10485760.");
                _ContextLength = value;
            }
        }

        /// <summary>
        /// How long a <see cref="SimulatedTurnKindEnum.Hang"/> turn waits before closing the connection, in
        /// milliseconds. Default 30000, minimum 0, maximum 600000.
        /// </summary>
        public int HangMs
        {
            get => _HangMs;
            set
            {
                if (value < 0 || value > 600000) throw new ArgumentOutOfRangeException(nameof(HangMs), "HangMs must be between 0 and 600000.");
                _HangMs = value;
            }
        }

        /// <summary>Text sent when the script is exhausted. Default <c>DONE</c>.</summary>
        public string FallbackText { get; set; } = "DONE";

        /// <summary>Requests received, in order.</summary>
        public List<SimulatorRequest> Requests
        {
            get { lock (_Lock) return new List<SimulatorRequest>(_Requests); }
        }

        /// <summary>Number of scripted turns not yet served.</summary>
        public int Remaining
        {
            get { lock (_Lock) return _Script.Count; }
        }

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly Queue<SimulatedTurn> _Script = new Queue<SimulatedTurn>();
        private readonly List<SimulatorRequest> _Requests = new List<SimulatorRequest>();
        private readonly HttpListener _Listener;
        private readonly Task _Loop;
        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private string _ModelName = "sim-model";
        private int _ContextLength = 32768;
        private int _HangMs = 30000;
        private int _CallCounter = 0;

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        #endregion

        #region Constructors-and-Factories

        /// <summary>Start the simulator on a free loopback port.</summary>
        public OpenModelSimulator()
        {
            _Listener = TestPorts.StartHttpListener(p => new[] { "http://127.0.0.1:" + p + "/" }, out int port);
            BaseUrl = "http://127.0.0.1:" + port;
            _Loop = Task.Run(LoopAsync);
        }

        #endregion

        #region Public-Methods

        /// <summary>Append turns to the script.</summary>
        /// <param name="turns">Turns, served in order.</param>
        /// <returns>This simulator.</returns>
        /// <exception cref="ArgumentNullException">When <paramref name="turns"/> is null.</exception>
        public OpenModelSimulator Enqueue(params SimulatedTurn[] turns)
        {
            if (turns == null) throw new ArgumentNullException(nameof(turns));
            lock (_Lock) foreach (SimulatedTurn turn in turns) _Script.Enqueue(turn ?? throw new ArgumentNullException(nameof(turns)));
            return this;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _Stop.Cancel();
            try { _Listener.Stop(); } catch (ObjectDisposedException) { }
            _Listener.Close();
            try { _Loop.Wait(2000); } catch (AggregateException) { }
            _Stop.Dispose();
        }

        #endregion

        #region Private-Methods

        private async Task LoopAsync()
        {
            while (_Listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _Listener.GetContextAsync().ConfigureAwait(false); }
                catch (HttpListenerException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (InvalidOperationException) { return; }
                _ = Task.Run(() => ServeAsync(context));
            }
        }

        private async Task ServeAsync(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url!.AbsolutePath;
                string body;
                using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)) body = await reader.ReadToEndAsync().ConfigureAwait(false);
                lock (_Lock) _Requests.Add(new SimulatorRequest { Method = context.Request.HttpMethod, Path = path, Body = body, Authorization = context.Request.Headers["Authorization"] });

                if (path.EndsWith("/api/show", StringComparison.Ordinal)) { await WriteJsonAsync(context, 200, ShowJson()).ConfigureAwait(false); return; }
                if (path.EndsWith("/api/ps", StringComparison.Ordinal)) { await WriteJsonAsync(context, 200, PsJson()).ConfigureAwait(false); return; }
                if (path.EndsWith("/api/tags", StringComparison.Ordinal)) { await WriteJsonAsync(context, 200, TagsJson()).ConfigureAwait(false); return; }
                if (path.EndsWith("/v1/models", StringComparison.Ordinal)) { await WriteJsonAsync(context, 200, ModelsJson()).ConfigureAwait(false); return; }

                bool ollama = path.EndsWith("/api/chat", StringComparison.Ordinal);
                bool openAi = path.EndsWith("/chat/completions", StringComparison.Ordinal);
                if (!ollama && !openAi) { await WriteJsonAsync(context, 404, "{\"error\":\"not found\"}").ConfigureAwait(false); return; }

                SimulatorChatRequest request = String.IsNullOrWhiteSpace(body) ? new SimulatorChatRequest() : (JsonSerializer.Deserialize<SimulatorChatRequest>(body, _Json) ?? new SimulatorChatRequest());
                bool stream = ollama ? request.Stream != false : request.Stream == true;
                SimulatedTurn turn = Next();
                foreach (KeyValuePair<string, string> header in turn.Headers) context.Response.Headers[header.Key] = header.Value;
                await ServeTurnAsync(context, turn, ollama, stream).ConfigureAwait(false);
            }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
        }

        private SimulatedTurn Next()
        {
            lock (_Lock) return _Script.Count > 0 ? _Script.Dequeue() : SimulatedTurn.Say(FallbackText);
        }

        private async Task ServeTurnAsync(HttpListenerContext context, SimulatedTurn turn, bool ollama, bool stream)
        {
            switch (turn.Kind)
            {
                case SimulatedTurnKindEnum.OllamaToolCallParse500:
                    await WriteJsonAsync(context, 500, ErrorJson(ollama, "error parsing tool call: raw='{\"name\": \"" + (turn.ToolName ?? "read_file") + "\", \"arguments\": {\"path\": \"README.md\"', err=unexpected end of JSON input", "server_error")).ConfigureAwait(false);
                    return;
                case SimulatedTurnKindEnum.Backpressure429:
                    if (!turn.Headers.ContainsKey("Retry-After")) context.Response.Headers["Retry-After"] = "1";
                    await WriteJsonAsync(context, 429, ErrorJson(ollama, "server busy, please try again.  maximum pending requests exceeded", "rate_limit_exceeded")).ConfigureAwait(false);
                    return;
                case SimulatedTurnKindEnum.QuotaExhausted429:
                    await WriteJsonAsync(context, 429, "{\"error\":{\"message\":\"You exceeded your current quota, please check your plan and billing details.\",\"type\":\"insufficient_quota\",\"code\":\"insufficient_quota\"}}").ConfigureAwait(false);
                    return;
                case SimulatedTurnKindEnum.RunnerTerminated500:
                    await WriteJsonAsync(context, 500, ErrorJson(ollama, "llama runner process has terminated: exit status 2", "server_error")).ConfigureAwait(false);
                    return;
                case SimulatedTurnKindEnum.OutOfMemory500:
                    await WriteJsonAsync(context, 500, ErrorJson(ollama, "model requires more system memory (21.3 GiB) than is available (12.1 GiB)", "server_error")).ConfigureAwait(false);
                    return;
                case SimulatedTurnKindEnum.ContextOverflow400:
                    string overflow = "This model's maximum context length is " + _ContextLength + " tokens. However, your messages resulted in " + (_ContextLength + 4096) + " tokens.";
                    await WriteJsonAsync(context, 400, ollama ? "{\"error\":" + JsonSerializer.Serialize("input length exceeds the context length: " + overflow) + "}" : "{\"error\":{\"message\":" + JsonSerializer.Serialize(overflow) + ",\"type\":\"invalid_request_error\",\"code\":\"context_length_exceeded\"}}").ConfigureAwait(false);
                    return;
                case SimulatedTurnKindEnum.Hang:
                    try { await Task.Delay(_HangMs, _Stop.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
                    try { context.Response.Abort(); } catch (ObjectDisposedException) { }
                    return;
                case SimulatedTurnKindEnum.RawResponse:
                    await WriteJsonAsync(context, turn.StatusCode, turn.Body ?? String.Empty).ConfigureAwait(false);
                    return;
            }

            ReplyShape shape = Shape(turn);
            if (!stream) await WriteJsonAsync(context, 200, ollama ? OllamaReply(shape) : OpenAiReply(shape)).ConfigureAwait(false);
            else if (ollama) await StreamOllamaAsync(context, shape, turn.ChunkDelayMs).ConfigureAwait(false);
            else await StreamOpenAiAsync(context, shape, turn.ChunkDelayMs).ConfigureAwait(false);
        }

        private ReplyShape Shape(SimulatedTurn turn)
        {
            ReplyShape shape = new ReplyShape { Content = turn.Text, Reasoning = turn.Reasoning, FinishReason = "stop" };
            string name = turn.ToolName ?? "unknown_tool";
            string textCall = "{\"name\":" + JsonSerializer.Serialize(name) + ",\"arguments\":" + turn.ToolArgumentsJson + "}";
            switch (turn.Kind)
            {
                case SimulatedTurnKindEnum.NativeToolCall:
                case SimulatedTurnKindEnum.MalformedToolArguments:
                case SimulatedTurnKindEnum.UnknownToolName:
                    shape.ToolName = name;
                    shape.ToolArgumentsJson = turn.ToolArgumentsJson;
                    shape.ArgumentsAreValidJson = turn.Kind != SimulatedTurnKindEnum.MalformedToolArguments;
                    shape.FinishReason = "tool_calls";
                    break;
                case SimulatedTurnKindEnum.HermesTextToolCall:
                    shape.Content = Join(turn.Text, "<tool_call>\n" + textCall + "\n</tool_call>");
                    break;
                case SimulatedTurnKindEnum.FunctionTagTextToolCall:
                    shape.Content = Join(turn.Text, "<function=" + name + ">" + turn.ToolArgumentsJson + "</function>");
                    break;
                case SimulatedTurnKindEnum.FencedJsonTextToolCall:
                    shape.Content = Join(turn.Text, "```json\n" + textCall + "\n```");
                    break;
                case SimulatedTurnKindEnum.LengthCut:
                    shape.Content = String.IsNullOrEmpty(turn.Text) ? "The change touches three files. First, in src/" : turn.Text;
                    shape.FinishReason = "length";
                    break;
                case SimulatedTurnKindEnum.ReasoningWithoutOpenTag:
                    shape.Content = "The user wants the file read, so I should check the layout first and then answer.\n</think>\n\n" + (String.IsNullOrEmpty(turn.Text) ? "DONE" : turn.Text);
                    break;
            }

            return shape;
        }

        private static string Join(string before, string call) => String.IsNullOrEmpty(before) ? call : before + "\n\n" + call;

        private string OllamaReply(ReplyShape shape)
        {
            Dictionary<string, object?> message = OllamaMessage(shape, shape.Content);
            Dictionary<string, object?> reply = new Dictionary<string, object?>
            {
                ["model"] = _ModelName,
                ["created_at"] = "2026-11-25T00:00:00Z",
                ["message"] = message,
                ["done"] = true,
                ["done_reason"] = shape.FinishReason == "length" ? "length" : "stop",
                ["prompt_eval_count"] = 64,
                ["eval_count"] = 16
            };
            return JsonSerializer.Serialize(reply);
        }

        private Dictionary<string, object?> OllamaMessage(ReplyShape shape, string content)
        {
            Dictionary<string, object?> message = new Dictionary<string, object?> { ["role"] = "assistant", ["content"] = content };
            if (!String.IsNullOrEmpty(shape.Reasoning)) message["thinking"] = shape.Reasoning;
            if (shape.ToolName != null)
            {
                // Ollama sends arguments as an object; arguments that are not valid JSON can only travel as a string.
                object? arguments = shape.ArgumentsAreValidJson ? JsonSerializer.Deserialize<Dictionary<string, object?>>(shape.ToolArgumentsJson) : shape.ToolArgumentsJson;
                message["tool_calls"] = new List<object> { new Dictionary<string, object?> { ["function"] = new Dictionary<string, object?> { ["name"] = shape.ToolName, ["arguments"] = arguments } } };
            }

            return message;
        }

        private string OpenAiReply(ReplyShape shape)
        {
            Dictionary<string, object?> message = new Dictionary<string, object?> { ["role"] = "assistant", ["content"] = shape.ToolName != null && String.IsNullOrEmpty(shape.Content) ? null : shape.Content };
            if (!String.IsNullOrEmpty(shape.Reasoning)) message["reasoning_content"] = shape.Reasoning;
            if (shape.ToolName != null) message["tool_calls"] = new List<object> { OpenAiToolCall(shape, null) };
            Dictionary<string, object?> reply = new Dictionary<string, object?>
            {
                ["id"] = "chatcmpl-sim",
                ["object"] = "chat.completion",
                ["created"] = 1,
                ["model"] = _ModelName,
                ["choices"] = new List<object> { new Dictionary<string, object?> { ["index"] = 0, ["message"] = message, ["finish_reason"] = shape.FinishReason } },
                ["usage"] = new Dictionary<string, object?> { ["prompt_tokens"] = 64, ["completion_tokens"] = 16, ["total_tokens"] = 80 }
            };
            return JsonSerializer.Serialize(reply);
        }

        private Dictionary<string, object?> OpenAiToolCall(ReplyShape shape, int? index)
        {
            Dictionary<string, object?> call = new Dictionary<string, object?>
            {
                ["id"] = "call_sim_" + Interlocked.Increment(ref _CallCounter),
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?> { ["name"] = shape.ToolName, ["arguments"] = shape.ToolArgumentsJson }
            };
            if (index.HasValue) call["index"] = index.Value;
            return call;
        }

        private async Task StreamOllamaAsync(HttpListenerContext context, ReplyShape shape, int delayMs)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/x-ndjson";
            context.Response.SendChunked = true;
            Stream output = context.Response.OutputStream;
            if (!String.IsNullOrEmpty(shape.Reasoning))
            {
                foreach (string piece in Chunks(shape.Reasoning!))
                {
                    await WriteLineAsync(output, JsonSerializer.Serialize(new Dictionary<string, object?> { ["model"] = _ModelName, ["message"] = new Dictionary<string, object?> { ["role"] = "assistant", ["content"] = "", ["thinking"] = piece }, ["done"] = false }) + "\n", delayMs).ConfigureAwait(false);
                }
            }

            foreach (string piece in Chunks(shape.Content))
            {
                await WriteLineAsync(output, JsonSerializer.Serialize(new Dictionary<string, object?> { ["model"] = _ModelName, ["message"] = new Dictionary<string, object?> { ["role"] = "assistant", ["content"] = piece }, ["done"] = false }) + "\n", delayMs).ConfigureAwait(false);
            }

            if (shape.ToolName != null)
            {
                ReplyShape callOnly = new ReplyShape { ToolName = shape.ToolName, ToolArgumentsJson = shape.ToolArgumentsJson, ArgumentsAreValidJson = shape.ArgumentsAreValidJson };
                await WriteLineAsync(output, JsonSerializer.Serialize(new Dictionary<string, object?> { ["model"] = _ModelName, ["message"] = OllamaMessage(callOnly, ""), ["done"] = false }) + "\n", delayMs).ConfigureAwait(false);
            }

            Dictionary<string, object?> done = new Dictionary<string, object?>
            {
                ["model"] = _ModelName,
                ["message"] = new Dictionary<string, object?> { ["role"] = "assistant", ["content"] = "" },
                ["done"] = true,
                ["done_reason"] = shape.FinishReason == "length" ? "length" : "stop",
                ["prompt_eval_count"] = 64,
                ["eval_count"] = 16
            };
            await WriteLineAsync(output, JsonSerializer.Serialize(done) + "\n", 0).ConfigureAwait(false);
            context.Response.Close();
        }

        private async Task StreamOpenAiAsync(HttpListenerContext context, ReplyShape shape, int delayMs)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            context.Response.SendChunked = true;
            Stream output = context.Response.OutputStream;
            await WriteLineAsync(output, Sse(new Dictionary<string, object?> { ["role"] = "assistant", ["content"] = "" }, null), 0).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(shape.Reasoning))
                foreach (string piece in Chunks(shape.Reasoning!)) await WriteLineAsync(output, Sse(new Dictionary<string, object?> { ["reasoning_content"] = piece }, null), delayMs).ConfigureAwait(false);
            foreach (string piece in Chunks(shape.Content)) await WriteLineAsync(output, Sse(new Dictionary<string, object?> { ["content"] = piece }, null), delayMs).ConfigureAwait(false);
            if (shape.ToolName != null)
                await WriteLineAsync(output, Sse(new Dictionary<string, object?> { ["tool_calls"] = new List<object> { OpenAiToolCall(shape, 0) } }, null), delayMs).ConfigureAwait(false);
            await WriteLineAsync(output, Sse(new Dictionary<string, object?>(), shape.FinishReason), 0).ConfigureAwait(false);
            await WriteLineAsync(output, "data: [DONE]\n\n", 0).ConfigureAwait(false);
            context.Response.Close();
        }

        private string Sse(Dictionary<string, object?> delta, string? finishReason)
        {
            Dictionary<string, object?> chunk = new Dictionary<string, object?>
            {
                ["id"] = "chatcmpl-sim",
                ["object"] = "chat.completion.chunk",
                ["created"] = 1,
                ["model"] = _ModelName,
                ["choices"] = new List<object> { new Dictionary<string, object?> { ["index"] = 0, ["delta"] = delta, ["finish_reason"] = finishReason } }
            };
            return "data: " + JsonSerializer.Serialize(chunk) + "\n\n";
        }

        private static IEnumerable<string> Chunks(string text)
        {
            const int size = 16;
            for (int i = 0; i < text.Length; i += size) yield return text.Substring(i, Math.Min(size, text.Length - i));
        }

        private async Task WriteLineAsync(Stream output, string text, int delayMs)
        {
            if (delayMs > 0) await Task.Delay(delayMs, _Stop.Token).ConfigureAwait(false);
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            await output.WriteAsync(bytes, 0, bytes.Length, _Stop.Token).ConfigureAwait(false);
            await output.FlushAsync(_Stop.Token).ConfigureAwait(false);
        }

        private static async Task WriteJsonAsync(HttpListenerContext context, int status, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            context.Response.Close();
        }

        private static string ErrorJson(bool ollama, string message, string type)
        {
            return ollama
                ? "{\"error\":" + JsonSerializer.Serialize(message) + "}"
                : "{\"error\":{\"message\":" + JsonSerializer.Serialize(message) + ",\"type\":" + JsonSerializer.Serialize(type) + ",\"code\":null}}";
        }

        private string ShowJson()
        {
            Dictionary<string, object?> show = new Dictionary<string, object?>
            {
                ["details"] = new Dictionary<string, object?> { ["family"] = "sim", ["parameter_size"] = "27B", ["quantization_level"] = "Q4_K_M" },
                ["model_info"] = new Dictionary<string, object?> { ["general.architecture"] = "sim", ["sim.context_length"] = _ContextLength },
                ["capabilities"] = new List<string> { "completion", "tools", "thinking" }
            };
            return JsonSerializer.Serialize(show);
        }

        private string PsJson()
        {
            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["models"] = new List<object> { new Dictionary<string, object?> { ["name"] = _ModelName, ["model"] = _ModelName, ["context_length"] = _ContextLength, ["size_vram"] = 1 } }
            });
        }

        private string TagsJson()
        {
            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["models"] = new List<object> { new Dictionary<string, object?> { ["name"] = _ModelName, ["model"] = _ModelName, ["details"] = new Dictionary<string, object?> { ["parameter_size"] = "27B" } } }
            });
        }

        private string ModelsJson()
        {
            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["object"] = "list",
                ["data"] = new List<object> { new Dictionary<string, object?> { ["id"] = _ModelName, ["object"] = "model", ["max_model_len"] = _ContextLength } }
            });
        }

        #endregion
    }
}
