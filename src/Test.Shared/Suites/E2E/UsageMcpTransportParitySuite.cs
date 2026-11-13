namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Server.Mcp;
    using Armada.Server.Mcp.Tools;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using Voltaic.Mcp;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Phase 4 AC4 transport parity: a <c>capture_captain_usage</c> call that omits the required <c>captainId</c> is a
    /// JSON-RPC <c>-32602</c> protocol error over BOTH the Admiral's HTTP MCP endpoint and <c>armada mcp stdio</c>,
    /// and the same session keeps dispatching afterwards. The stdio case drives the real Helm process over its
    /// stdin/stdout against a throwaway data directory.
    /// </summary>
    public sealed class UsageMcpTransportParitySuite : IArmadaTestSuite
    {
        private const string Suite = "E2E.UsageMcpTransportParity";
        private const int StdioReadTimeoutMs = 60000;

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Suite, "Usage MCP required-argument parity across HTTP and stdio", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(Suite, "stdio_missing_argument_is_protocol_error_and_recovers", "Initialized armada mcp stdio answers capture_captain_usage {} with error.code -32602 and keeps serving", async ct =>
                {
                    string dataDir = TestTemp.NewDirectory("usage-stdio-guard");
                    using StdioSession session = StdioSession.Start(dataDir);
                    try
                    {
                        string init = await session.CallAsync(1, "initialize", "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"armada-test\",\"version\":\"1.0\"}}").ConfigureAwait(false);
                        AssertContains("\"protocolVersion\"", init, "stdio initialize answered");
                        await session.NotifyAsync("notifications/initialized").ConfigureAwait(false);

                        string[] missingParams = new[]
                        {
                            "{\"name\":\"capture_captain_usage\",\"arguments\":{}}",
                            "{\"name\":\"capture_captain_usage\"}",
                            "{\"name\":\"capture_captain_usage\",\"arguments\":{\"harborId\":\"hbr_only\"}}",
                            "{\"name\":\"capture_captain_usage\",\"arguments\":{\"CaptainId\":\"cpt_wrong_case\"}}"
                        };
                        int id = 10;
                        foreach (string parameters in missingParams)
                        {
                            id++;
                            string body = await session.CallAsync(id, "tools/call", parameters).ConfigureAwait(false);
                            McpRpcEnvelope envelope = JsonHelper.Deserialize<McpRpcEnvelope>(body);
                            AssertNotNull(envelope.Error, "stdio protocol error object for " + parameters + ": " + body);
                            AssertEqual(-32602, envelope.Error!.Code, "stdio error.code for " + parameters + ": " + body);
                            AssertNull(envelope.Result, "stdio protocol error carries no tool result: " + body);
                            AssertFalse(body.Contains("\"isError\"", StringComparison.Ordinal), "stdio answer is not an isError tool result: " + body);
                            AssertContains("\"data\":{\"tool\":\"capture_captain_usage\",\"argument\":\"captainId\"}", body, "stdio error.data names the tool and argument");
                        }

                        // Recovery: the same initialized session still dispatches tool calls through the original handler.
                        string present = await session.CallAsync(30, "tools/call", "{\"name\":\"capture_captain_usage\",\"arguments\":{\"captainId\":\"cpt_stdio_present\"}}").ConfigureAwait(false);
                        McpRpcEnvelope presentEnvelope = JsonHelper.Deserialize<McpRpcEnvelope>(present);
                        AssertNull(presentEnvelope.Error, "a present captainId is not a protocol error over stdio: " + present);
                        AssertNotNull(presentEnvelope.Result, "the handler answered with a tool result: " + present);
                        AssertContains("Unavailable", present, "stdio capture reaches its handler, which reports the Admiral-only usage service as Unavailable");

                        string otherTool = await session.CallAsync(31, "tools/call", "{\"name\":\"get_captain\",\"arguments\":{}}").ConfigureAwait(false);
                        AssertNull(JsonHelper.Deserialize<McpRpcEnvelope>(otherTool).Error, "tools outside the guard keep the default form over stdio: " + otherTool);
                        AssertContains("\"isError\":true", otherTool, "other tools still answer a missing required argument with isError over stdio");

                        string wrongType = await session.CallAsync(32, "tools/call", "{\"name\":\"capture_captain_usage\",\"arguments\":{\"captainId\":42}}").ConfigureAwait(false);
                        AssertNull(JsonHelper.Deserialize<McpRpcEnvelope>(wrongType).Error, "type validation stays a tool result over stdio: " + wrongType);
                        AssertContains("\"isError\":true", wrongType, "type validation is an isError result over stdio");
                    }
                    finally
                    {
                        session.Close();
                    }
                }),
                new TestCaseDescriptor(Suite, "http_session_recovers_after_protocol_error", "An HTTP MCP session that received -32602 keeps dispatching usage tools", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    using HttpClient admin = new HttpClient { BaseAddress = fx.McpClient.BaseAddress };
                    admin.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
                    string sessionId = await HttpInitializeAsync(admin, ct).ConfigureAwait(false);

                    string missing = await HttpCallAsync(admin, sessionId, 2, "{\"name\":\"capture_captain_usage\",\"arguments\":{}}", ct).ConfigureAwait(false);
                    AssertEqual(-32602, JsonHelper.Deserialize<McpRpcEnvelope>(missing).Error?.Code ?? 0, "HTTP missing captainId: " + missing);
                    AssertContains("\"id\":2", missing, "HTTP correlation survives the guard");

                    string present = await HttpCallAsync(admin, sessionId, 3, "{\"name\":\"capture_captain_usage\",\"arguments\":{\"captainId\":\"cpt_http_recovery_missing\"}}", ct).ConfigureAwait(false);
                    AssertNull(JsonHelper.Deserialize<McpRpcEnvelope>(present).Error, "same session, present captainId is not a protocol error: " + present);
                    AssertContains("NotFound", present, "same session reaches the handler after the protocol error");

                    string status = await HttpCallAsync(admin, sessionId, 4, "{\"name\":\"usage_status\",\"arguments\":{}}", ct).ConfigureAwait(false);
                    AssertNull(JsonHelper.Deserialize<McpRpcEnvelope>(status).Error, "same session, usage_status still dispatches: " + status);
                    AssertFalse(status.Contains("\"isError\":true", StringComparison.Ordinal), "usage_status succeeds after the protocol error: " + status);
                }),
                new TestCaseDescriptor(Suite, "guard_installs_on_both_transports", "The guard captures the pinned library's dispatcher on the stdio and HTTP servers and the stdio command installs it", ct =>
                {
                    IReadOnlyDictionary<string, IReadOnlyList<string>> map = McpUsageTools.ProtocolRequiredArguments;
                    AssertTrue(McpRequiredArgumentGuard.Install(new McpServer(), map), "stdio McpServer tools/call dispatcher is captured");
                    AssertTrue(McpRequiredArgumentGuard.Install(new McpHttpServer("127.0.0.1", 1), map), "HTTP McpHttpServer tools/call dispatcher is captured");
                    AssertThrows<ArgumentNullException>(() => McpRequiredArgumentGuard.Install((McpServer)null!, map), "null stdio server");
                    AssertThrows<ArgumentNullException>(() => McpRequiredArgumentGuard.Install(new McpServer(), null!), "null map");

                    string source = File.ReadAllText(Path.Combine(DashboardSource.RepoRoot(), "src", "Armada.Helm", "Commands", "McpStdioCommand.cs"));
                    int register = source.IndexOf("toolSet.Register(RegisterAdapted);", StringComparison.Ordinal);
                    int install = source.IndexOf("McpRequiredArgumentGuard.Install(mcpServer, Armada.Server.Mcp.Tools.McpUsageTools.ProtocolRequiredArguments)", StringComparison.Ordinal);
                    int run = source.IndexOf("mcpServer.RunAsync(", StringComparison.Ordinal);
                    AssertTrue(register >= 0 && install > register && run > install, "armada mcp stdio installs the guard after registering tools and before RunAsync");
                    return Task.CompletedTask;
                })
            });
        }

        private static async Task<string> HttpInitializeAsync(HttpClient mcp, CancellationToken token)
        {
            using StringContent init = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"armada-test\",\"version\":\"1.0\"}}}", Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await mcp.PostAsync("/rpc", init, token).ConfigureAwait(false);
            AssertTrue(response.IsSuccessStatusCode, "HTTP MCP initialize: " + (int)response.StatusCode);
            return response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values) ? values.First() : String.Empty;
        }

        private static async Task<string> HttpCallAsync(HttpClient mcp, string sessionId, int id, string parameters, CancellationToken token)
        {
            using HttpRequestMessage call = new HttpRequestMessage(HttpMethod.Post, "/rpc");
            call.Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":" + parameters + "}", Encoding.UTF8, "application/json");
            if (!String.IsNullOrEmpty(sessionId)) call.Headers.Add("Mcp-Session-Id", sessionId);
            using HttpResponseMessage response = await mcp.SendAsync(call, token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            AssertTrue(response.IsSuccessStatusCode, "HTTP MCP tools/call " + (int)response.StatusCode + ": " + body);
            return body;
        }

        /// <summary>A live <c>armada mcp stdio</c> child process with a line-oriented JSON-RPC reader.</summary>
        private sealed class StdioSession : IDisposable
        {
            private readonly Process _Process;
            private readonly StringBuilder _Stderr = new StringBuilder();

            private StdioSession(Process process)
            {
                _Process = process;
            }

            public static StdioSession Start(string dataDir)
            {
                string helmDll = typeof(Armada.Helm.Commands.McpStdioCommand).Assembly.Location;
                string runtimeConfig = Path.ChangeExtension(helmDll, ".runtimeconfig.json");
                AssertTrue(File.Exists(runtimeConfig), "Armada.Helm runtimeconfig beside the test host's Armada.Helm.dll (needed to launch armada mcp stdio): " + runtimeConfig);

                string? hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
                string dotnet = !String.IsNullOrEmpty(hostPath) && File.Exists(hostPath) ? hostPath : "dotnet";
                ProcessStartInfo info = new ProcessStartInfo(dotnet)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    WorkingDirectory = dataDir
                };
                info.ArgumentList.Add(helmDll);
                info.ArgumentList.Add("mcp");
                info.ArgumentList.Add("stdio");
                info.Environment[Constants.DataDirectoryEnvVar] = dataDir;
                Process process = Process.Start(info) ?? throw new InvalidOperationException("could not start armada mcp stdio");
                StdioSession session = new StdioSession(process);
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (session._Stderr) session._Stderr.AppendLine(e.Data); };
                process.BeginErrorReadLine();
                return session;
            }

            public async Task NotifyAsync(string method)
            {
                await _Process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"" + method + "\"}").ConfigureAwait(false);
                await _Process.StandardInput.FlushAsync().ConfigureAwait(false);
            }

            public async Task<string> CallAsync(int id, string method, string parameters)
            {
                await _Process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":" + parameters + "}").ConfigureAwait(false);
                await _Process.StandardInput.FlushAsync().ConfigureAwait(false);
                string idToken = "\"id\":" + id + "}";
                using CancellationTokenSource timeout = new CancellationTokenSource(StdioReadTimeoutMs);
                while (true)
                {
                    string? line;
                    try
                    {
                        line = await _Process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw new TimeoutException("no stdio answer for id " + id + " within " + StdioReadTimeoutMs + "ms; stderr: " + Stderr());
                    }

                    if (line == null) throw new EndOfStreamException("armada mcp stdio closed stdout before answering id " + id + "; stderr: " + Stderr());
                    if (line.Contains(idToken, StringComparison.Ordinal) || line.Contains("\"id\":" + id + ",", StringComparison.Ordinal)) return line;
                }
            }

            public void Close()
            {
                try { _Process.StandardInput.Close(); } catch (Exception) { }
                if (!_Process.WaitForExit(15000))
                {
                    try { _Process.Kill(true); } catch (Exception) { }
                }
            }

            public void Dispose()
            {
                Close();
                _Process.Dispose();
            }

            private string Stderr()
            {
                lock (_Stderr) return _Stderr.ToString();
            }
        }
    }
}
