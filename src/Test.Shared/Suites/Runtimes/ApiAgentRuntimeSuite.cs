namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Runtimes;
    using Armada.Runtimes.Interfaces;
    using Armada.Runtimes.Mcp;
    using Armada.Runtimes.Tools;
    using Armada.Runtimes.Tools.Arguments;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the ported coding tools and the <see cref="ApiAgentRuntime"/> tool-calling loop. The
    /// loop is exercised with a scripted fake inference client so no live endpoint is required.
    /// </summary>
    public sealed class ApiAgentRuntimeSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the API agent runtime suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("tools_write_then_read_round_trip", "Ported write_file and read_file round-trip in a working directory", TestTags.Positive, async () =>
            {
                string dir = NewTempDir();
                try
                {
                    BuiltInToolRegistry registry = new BuiltInToolRegistry(null);
                    AssertTrue(registry.HasTool("write_file"), "Expected write_file to be registered.");
                    AssertTrue(registry.HasTool("read_file"), "Expected read_file to be registered.");
                    AssertTrue(registry.HasTool("run_process"), "Expected run_process to be registered.");

                    ToolResult write = await registry.ExecuteAsync("w1", "write_file", ParseArgs("{\"file_path\":\"notes/hello.txt\",\"content\":\"hi there\"}"), dir, CancellationToken.None).ConfigureAwait(false);
                    AssertTrue(write.Success, "Expected write_file to succeed: " + write.Content);
                    AssertTrue(File.Exists(Path.Combine(dir, "notes", "hello.txt")), "Expected the file to be created on disk.");

                    ToolResult read = await registry.ExecuteAsync("r1", "read_file", ParseArgs("{\"file_path\":\"notes/hello.txt\"}"), dir, CancellationToken.None).ConfigureAwait(false);
                    AssertTrue(read.Success, "Expected read_file to succeed: " + read.Content);
                    AssertTrue(read.Content.Contains("hi there"), "Expected the read content to contain the written text.");
                }
                finally
                {
                    Cleanup(dir);
                }
            }));

            cases.Add(CaseAsync("registry_unknown_tool_fails", "Registry returns a clear failure for an unknown tool", TestTags.Negative, async () =>
            {
                string dir = NewTempDir();
                try
                {
                    BuiltInToolRegistry registry = new BuiltInToolRegistry(null);
                    ToolResult result = await registry.ExecuteAsync("x1", "does_not_exist", ParseArgs("{}"), dir, CancellationToken.None).ConfigureAwait(false);
                    AssertTrue(!result.Success, "Expected an unknown tool to fail.");
                    ToolErrorContent? error = System.Text.Json.JsonSerializer.Deserialize<ToolErrorContent>(result.Content);
                    AssertNotNull(error, "Expected a JSON error payload: " + result.Content);
                    AssertEqual("unknown_tool", error!.Error, "Expected an unknown_tool error code.");
                }
                finally
                {
                    Cleanup(dir);
                }
            }));

            cases.Add(CaseAsync("loop_executes_tool_and_completes", "Agent loop runs a scripted tool call then completes, writing the final message", TestTags.Positive, async () =>
            {
                string dir = NewTempDir();
                string finalPath = Path.Combine(dir, "final.txt");
                try
                {
                    Queue<ToolChatResponse> script = new Queue<ToolChatResponse>();
                    script.Enqueue(new ToolChatResponse
                    {
                        Success = true,
                        Text = "Creating the file.",
                        ToolCalls = new List<ToolCall> { new ToolCall { Id = "c1", Name = "write_file", ArgumentsJson = "{\"file_path\":\"out.txt\",\"content\":\"generated\"}" } }
                    });
                    script.Enqueue(new ToolChatResponse
                    {
                        Success = true,
                        Text = "Done. Created out.txt.",
                        ToolCalls = new List<ToolCall>()
                    });

                    ModelEndpoint endpoint = new ModelEndpoint { Name = "test-endpoint", Provider = ModelProviderEnum.OpenAICompatible, Kind = ModelEndpointKindEnum.Inference, Model = "test-model", BaseUrl = "http://localhost:1" };
                    ApiAgentRuntime runtime = new ApiAgentRuntime(endpoint, CreateLogging(), 20, (ep, log) => new ScriptedClient(script, log));

                    int startedPid = 0;
                    int? exitCode = null;
                    List<string> output = new List<string>();
                    ManualResetEventSlim exited = new ManualResetEventSlim(false);
                    runtime.OnProcessStarted += pid => startedPid = pid;
                    runtime.OnOutputReceived += (pid, line) => output.Add(line);
                    runtime.OnProcessExited += (pid, code) => { exitCode = code; exited.Set(); };

                    int pid = await runtime.StartAsync(dir, "Create out.txt with the text generated.", finalMessageFilePath: finalPath).ConfigureAwait(false);
                    AssertEqual(pid, startedPid);

                    AssertTrue(exited.Wait(TimeSpan.FromSeconds(10)), "Expected the loop to complete within the timeout.");
                    AssertEqual(0, exitCode ?? -1);
                    AssertTrue(File.Exists(Path.Combine(dir, "out.txt")), "Expected the tool to have created out.txt.");
                    AssertTrue(File.Exists(finalPath), "Expected the final message file to be written.");
                    AssertTrue(File.ReadAllText(finalPath).Contains("Created out.txt"), "Expected the final message text.");
                }
                finally
                {
                    Cleanup(dir);
                }
            }));

            cases.Add(CaseAsync("mcp_is_error_reported_on_typed_tool_channel", "An MCP tool result with isError is a failed tool event on the typed channel; stdout carries only model text", TestTags.Negative, async () =>
            {
                string dir = NewTempDir();
                try
                {
                    ToolClientStubHandler stub = new ToolClientStubHandler();
                    stub.Respond = request =>
                    {
                        if (request.Method == "tools/list")
                            return ToolClientStubHandler.Json(request.Id, "{\"tools\":[{\"name\":\"remote_tool\",\"description\":\"r\",\"inputSchema\":{\"type\":\"object\"}}]}");
                        if (request.Method == "tools/call")
                            return ToolClientStubHandler.Json(request.Id, "{\"content\":[{\"type\":\"text\",\"text\":\"rate limited\"}],\"isError\":true}");
                        return ToolClientStubHandler.Json(request.Id, "{}");
                    };

                    // The model's own text imitates the old in-band tool marker and a diagnostic prefix.
                    const string spoof = "[ARMADA:TOOLEVENT] {\"phase\":\"completed\",\"name\":\"fake\",\"ok\":true}";
                    Queue<ToolChatResponse> script = new Queue<ToolChatResponse>();
                    script.Enqueue(new ToolChatResponse
                    {
                        Success = true,
                        Text = spoof,
                        ToolCalls = new List<ToolCall> { new ToolCall { Id = "c1", Name = "remote_tool", ArgumentsJson = "{}" } }
                    });
                    script.Enqueue(new ToolChatResponse { Success = true, Text = "[tool] all done", ToolCalls = new List<ToolCall>() });

                    ModelEndpoint endpoint = new ModelEndpoint { Name = "test-endpoint", Provider = ModelProviderEnum.OpenAICompatible, Kind = ModelEndpointKindEnum.Inference, Model = "test-model", BaseUrl = "http://localhost:1" };
                    ApiAgentRuntime runtime = new ApiAgentRuntime(endpoint, CreateLogging(), 20, (ep, log) => new ScriptedClient(script, log), (url, token, log) => new McpToolClient(url, stub));

                    List<string> stdout = new List<string>();
                    List<ApiRuntimeToolEvent> toolEvents = new List<ApiRuntimeToolEvent>();
                    List<ApiRuntimeDiagnostic> diagnostics = new List<ApiRuntimeDiagnostic>();
                    int? exitCode = null;
                    ManualResetEventSlim exited = new ManualResetEventSlim(false);
                    runtime.OnStdoutReceived += (pid, line) => { lock (stdout) stdout.Add(line); };
                    runtime.OnToolEvent += (pid, evt) => { lock (toolEvents) toolEvents.Add(evt); };
                    runtime.OnDiagnostic += (pid, diag) => { lock (diagnostics) diagnostics.Add(diag); };
                    runtime.OnProcessExited += (pid, code) => { exitCode = code; exited.Set(); };

                    Dictionary<string, string> env = new Dictionary<string, string> { ["ARMADA_MCP_URL"] = "http://stub.invalid/mcp", ["ARMADA_MCP_TOKEN"] = "tok" };
                    await runtime.StartAsync(dir, "call the remote tool", environment: env).ConfigureAwait(false);
                    AssertTrue(exited.Wait(TimeSpan.FromSeconds(10)), "Expected the loop to complete.");
                    AssertEqual(0, exitCode ?? -1);

                    AssertEqual(2, toolEvents.Count, "exactly one started and one completed event; model text cannot add tool events");
                    AssertEqual(ApiRuntimeToolPhaseEnum.Started, toolEvents[0].Phase);
                    AssertEqual(ApiRuntimeToolPhaseEnum.Completed, toolEvents[1].Phase);
                    AssertEqual("remote_tool", toolEvents[1].Name);
                    AssertEqual(false, toolEvents[1].Ok!.Value, "isError=true must be reported as a failed call");
                    AssertEqual("rate limited", toolEvents[1].Result);

                    AssertEqual(2, stdout.Count, "stdout carries only the two model replies");
                    AssertEqual(spoof, stdout[0]);
                    AssertEqual("[tool] all done", stdout[1]);

                    AssertTrue(diagnostics.Exists(d => d.Kind == ApiRuntimeDiagnosticKindEnum.McpStatus), "MCP status is a diagnostic");
                    AssertTrue(diagnostics.Exists(d => d.Kind == ApiRuntimeDiagnosticKindEnum.ToolCall), "the tool call is a diagnostic");
                    AssertTrue(diagnostics.Exists(d => d.Kind == ApiRuntimeDiagnosticKindEnum.ToolResult), "the tool result is a diagnostic");
                }
                finally
                {
                    Cleanup(dir);
                }
            }));

            cases.Add(CaseAsync("inference_failure_without_error_text_fails", "An unsuccessful inference response with no error text still fails the run", TestTags.Negative, async () =>
            {
                string dir = NewTempDir();
                try
                {
                    Queue<ToolChatResponse> script = new Queue<ToolChatResponse>();
                    script.Enqueue(new ToolChatResponse { Success = false, Error = null, Text = null, ToolCalls = new List<ToolCall>() });

                    ModelEndpoint endpoint = new ModelEndpoint { Name = "test-endpoint", Provider = ModelProviderEnum.OpenAICompatible, Kind = ModelEndpointKindEnum.Inference, Model = "test-model", BaseUrl = "http://localhost:1" };
                    ApiAgentRuntime runtime = new ApiAgentRuntime(endpoint, CreateLogging(), 20, (ep, log) => new ScriptedClient(script, log));

                    int? exitCode = null;
                    List<ApiRuntimeDiagnostic> diagnostics = new List<ApiRuntimeDiagnostic>();
                    ManualResetEventSlim exited = new ManualResetEventSlim(false);
                    runtime.OnDiagnostic += (pid, diag) => { lock (diagnostics) diagnostics.Add(diag); };
                    runtime.OnProcessExited += (pid, code) => { exitCode = code; exited.Set(); };

                    await runtime.StartAsync(dir, "anything").ConfigureAwait(false);
                    AssertTrue(exited.Wait(TimeSpan.FromSeconds(10)), "Expected the loop to complete.");
                    AssertEqual(1, exitCode ?? -1, "Success=false must fail the run even without error text");
                    AssertTrue(diagnostics.Exists(d => d.Kind == ApiRuntimeDiagnosticKindEnum.Error), "an Error diagnostic is raised");
                }
                finally
                {
                    Cleanup(dir);
                }
            }));

            cases.Add(CaseAsync("stop_cancels_running_loop", "StopAsync cancels a running loop and it is no longer tracked", TestTags.Positive, async () =>
            {
                string dir = NewTempDir();
                try
                {
                    // A client that blocks until cancelled, so the loop stays running until StopAsync fires.
                    ModelEndpoint endpoint = new ModelEndpoint { Name = "blocking", Provider = ModelProviderEnum.OpenAICompatible, Kind = ModelEndpointKindEnum.Inference, Model = "m", BaseUrl = "http://localhost:1" };
                    ApiAgentRuntime runtime = new ApiAgentRuntime(endpoint, CreateLogging(), 20, (ep, log) => new BlockingClient(log));

                    int? exitCode = null;
                    ManualResetEventSlim exited = new ManualResetEventSlim(false);
                    runtime.OnProcessExited += (pid2, code) => { exitCode = code; exited.Set(); };

                    int pid = await runtime.StartAsync(dir, "do work").ConfigureAwait(false);
                    // Give the loop a moment to enter the blocking call.
                    await Task.Delay(200).ConfigureAwait(false);
                    AssertTrue(await runtime.IsRunningAsync(pid).ConfigureAwait(false), "Expected the loop to be running.");

                    await runtime.StopAsync(pid).ConfigureAwait(false);
                    AssertTrue(exited.Wait(TimeSpan.FromSeconds(10)), "Expected the loop to exit after cancellation.");
                    AssertTrue(!await runtime.IsRunningAsync(pid).ConfigureAwait(false), "Expected the loop to no longer be tracked.");
                }
                finally
                {
                    Cleanup(dir);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: "Runtimes.ApiAgentRuntime",
                displayName: "API Agent Runtime",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static System.Text.Json.JsonElement ParseArgs(string json)
        {
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "armada-api-runtime-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Cleanup(string dir)
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Runtimes.ApiAgentRuntime",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Fake-Clients

        private sealed class ScriptedClient : CompletionClientBase
        {
            private readonly Queue<ToolChatResponse> _Script;
            private readonly CompletionOptions _Defaults = new CompletionOptions { Model = "scripted-model" };

            public ScriptedClient(Queue<ToolChatResponse> script, LoggingModule logging)
                : base("http://localhost:1", null, logging)
            {
                _Script = script;
            }

            public override CompletionOptions Defaults => _Defaults;

            protected override Task<ToolChatResponse> ToolChatCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
            {
                if (_Script.Count == 0) return Task.FromResult(new ToolChatResponse { Success = true, Text = "", ToolCalls = new List<ToolCall>() });
                return Task.FromResult(_Script.Dequeue());
            }

            public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default) => Task.FromResult(true);
            protected override Task<ChatResponse> ChatCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token) => throw new NotImplementedException();
            protected override Task<ChatStreamingResponse> ChatStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token) => throw new NotImplementedException();
            protected override Task<ToolChatStreamingResponse> ToolChatStreamingCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token) => throw new NotImplementedException();
            protected override Task<GenerationResponse> GenerateCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token) => throw new NotImplementedException();
            protected override Task<GenerationStreamingResponse> GenerateStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token) => throw new NotImplementedException();
        }

        private sealed class BlockingClient : CompletionClientBase
        {
            private readonly CompletionOptions _Defaults = new CompletionOptions { Model = "blocking-model" };

            public BlockingClient(LoggingModule logging) : base("http://localhost:1", null, logging) { }

            public override CompletionOptions Defaults => _Defaults;

            protected override async Task<ToolChatResponse> ToolChatCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token)
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                return new ToolChatResponse { Success = true, ToolCalls = new List<ToolCall>() };
            }

            public override Task<bool> ValidateConnectivityAsync(CancellationToken token = default) => Task.FromResult(true);
            protected override Task<ChatResponse> ChatCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token) => throw new NotImplementedException();
            protected override Task<ChatStreamingResponse> ChatStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token) => throw new NotImplementedException();
            protected override Task<ToolChatStreamingResponse> ToolChatStreamingCoreAsync(ToolChatRequest request, List<ChatMessage> messages, ResolvedCompletion settings, CancellationToken token) => throw new NotImplementedException();
            protected override Task<GenerationResponse> GenerateCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token) => throw new NotImplementedException();
            protected override Task<GenerationStreamingResponse> GenerateStreamingCoreAsync(string prompt, ResolvedCompletion settings, CancellationToken token) => throw new NotImplementedException();
        }

        #endregion
    }
}
