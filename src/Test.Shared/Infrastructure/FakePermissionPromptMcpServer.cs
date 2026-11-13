namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Server.Mcp;

    /// <summary>
    /// A minimal Armada MCP endpoint for pi permission gate tests: answers <c>initialize</c> (with an Mcp-Session-Id
    /// header) and <c>tools/call cli_permission_prompt</c> with the decision <see cref="Decide"/> returns, after its
    /// optional delay, and records every prompt call.
    /// </summary>
    public sealed class FakePermissionPromptMcpServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The endpoint URL (http://127.0.0.1:port/mcp).
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// Session id returned by initialize.
        /// </summary>
        public const string SessionId = "sess-pi-gate";

        /// <summary>
        /// Decides a prompt call: returns the tool result text (a CliPermissionPromptResult JSON) and may delay.
        /// Defaults to allow.
        /// </summary>
        public Func<FakePermissionPromptCall, Task<string>> Decide { get; set; } = call => Task.FromResult(Allow());

        /// <summary>
        /// When true, tools/call answers are sent as text/event-stream (one data line), as streamable HTTP MCP servers may.
        /// </summary>
        public bool UseEventStream { get; set; } = false;

        /// <summary>
        /// When true, tools/call results carry isError=true.
        /// </summary>
        public bool ResultIsError { get; set; } = false;

        /// <summary>
        /// Prompt calls received, in arrival order.
        /// </summary>
        public List<FakePermissionPromptCall> Calls
        {
            get
            {
                lock (_Lock) return new List<FakePermissionPromptCall>(_Calls);
            }
        }

        #endregion

        #region Private-Members

        private readonly HttpListener _Listener = new HttpListener();
        private readonly List<FakePermissionPromptCall> _Calls = new List<FakePermissionPromptCall>();
        private readonly object _Lock = new object();
        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start listening on a free loopback port.
        /// </summary>
        public FakePermissionPromptMcpServer()
        {
            int port = FreePort();
            Url = "http://127.0.0.1:" + port + "/mcp";
            _Listener.Prefixes.Add("http://127.0.0.1:" + port + "/mcp/");
            _Listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// An allow decision.
        /// </summary>
        /// <returns>The tool result text.</returns>
        public static string Allow()
        {
            return JsonSerializer.Serialize(new CliPermissionPromptResult { Behavior = CliPermissionPromptResult.Allow });
        }

        /// <summary>
        /// A deny decision with a message.
        /// </summary>
        /// <param name="message">Reason.</param>
        /// <returns>The tool result text.</returns>
        public static string Deny(string message)
        {
            return JsonSerializer.Serialize(new CliPermissionPromptResult { Behavior = CliPermissionPromptResult.Deny, Message = message });
        }

        /// <summary>
        /// A loopback URL nothing listens on.
        /// </summary>
        /// <returns>The URL.</returns>
        public static string UnreachableUrl()
        {
            return "http://127.0.0.1:" + FreePort() + "/mcp";
        }

        /// <summary>
        /// Stop the listener.
        /// </summary>
        public void Dispose()
        {
            _Stop.Cancel();
            try { _Listener.Stop(); } catch { }
            try { _Listener.Close(); } catch { }
        }

        #endregion

        #region Private-Methods

        private static int FreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task AcceptLoopAsync()
        {
            while (!_Stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _Listener.GetContextAsync().ConfigureAwait(false);
                }
                catch
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                string body;
                using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)) body = await reader.ReadToEndAsync().ConfigureAwait(false);
                FakePermissionPromptRpc rpc = JsonSerializer.Deserialize<FakePermissionPromptRpc>(body) ?? new FakePermissionPromptRpc();
                if (rpc.Method == "initialize")
                {
                    context.Response.Headers["Mcp-Session-Id"] = SessionId;
                    await WriteAsync(context, false, "{\"jsonrpc\":\"2.0\",\"id\":" + rpc.Id + ",\"result\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{\"tools\":{}},\"serverInfo\":{\"name\":\"fake-armada\",\"version\":\"1.0\"}}}").ConfigureAwait(false);
                    return;
                }

                if (rpc.Method != "tools/call" || rpc.Params?.Name != "cli_permission_prompt")
                {
                    await WriteAsync(context, false, "{\"jsonrpc\":\"2.0\",\"id\":" + (rpc.Id?.ToString() ?? "null") + ",\"error\":{\"code\":-32601,\"message\":\"unexpected " + rpc.Method + "\"}}").ConfigureAwait(false);
                    return;
                }

                FakePermissionPromptArguments args = rpc.Params.Arguments ?? new FakePermissionPromptArguments();
                FakePermissionPromptCall call = new FakePermissionPromptCall
                {
                    ToolName = args.ToolName ?? "",
                    Command = args.Input != null && args.Input.TryGetValue("command", out JsonElement command) && command.ValueKind == JsonValueKind.String ? command.GetString() : null,
                    ToolUseId = args.ToolUseId,
                    Token = context.Request.Headers["X-Token"],
                    SessionId = context.Request.Headers["Mcp-Session-Id"],
                    ArrivedUtc = DateTime.UtcNow
                };
                lock (_Lock) _Calls.Add(call);

                string text = await Decide(call).ConfigureAwait(false);
                call.AnsweredUtc = DateTime.UtcNow;
                string result = "{\"jsonrpc\":\"2.0\",\"id\":" + rpc.Id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(text) + "}],\"isError\":" + (ResultIsError ? "true" : "false") + "}}";
                await WriteAsync(context, UseEventStream, result).ConfigureAwait(false);
            }
            catch
            {
                try { context.Response.Abort(); } catch { }
            }
        }

        private static async Task WriteAsync(HttpListenerContext context, bool eventStream, string json)
        {
            string payload = eventStream ? "event: message\ndata: " + json + "\n\n" : json;
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            context.Response.StatusCode = 200;
            context.Response.ContentType = eventStream ? "text/event-stream" : "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            context.Response.Close();
        }

        #endregion
    }
}
