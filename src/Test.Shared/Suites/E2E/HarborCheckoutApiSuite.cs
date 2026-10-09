namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using WatsonWebserver.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Split mode end to end: a private Admiral whose data folder has no checkout of the vessel, and a real Harbor (link
    /// client, dock manager, and local git) linked to it over the WebSocket link with the vessel's checkout mapped in its
    /// settings. A check run through REST and through the run_check MCP tool Ask Armada uses runs in the Harbor's checkout
    /// and records its output; Workspace browses and reads there; readiness names the Harbor. A vessel no Harbor has a
    /// checkout of fails with 409 and VesselCheckoutErrorDetail over REST, and with an Unavailable tool error and Code
    /// VesselCheckoutUnavailable.NoHarborCheckout over MCP.
    /// </summary>
    public sealed class HarborCheckoutApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.HarborCheckoutApi";
        private const string HarborId = "hbr_e2e_checkout";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "check_run_and_workspace_on_harbor_over_rest_and_mcp",
                displayName: "Check runs (REST and the run_check MCP tool), Workspace, and readiness run on the Harbor's checkout; a vessel with none fails typed",
                executeAsync: async (CancellationToken ct) =>
                {
                    using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                    string dataDir = Path.Combine(git.AdmiralData, "server");
                    DatabaseSettings dbSettings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(dataDir, "armada.db") };
                    using (InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(dataDir, dbSettings).ConfigureAwait(false))
                    using (CancellationTokenSource session = new CancellationTokenSource())
                    {
                        LoggingModule logging = new LoggingModule();
                        logging.Settings.EnableConsole = false;
                        HarborDockSettings harborSettings = git.Settings.Clone();
                        harborSettings.Repositories.Add(new HarborRepositoryMapping("e2e-app", git.Checkout));
                        Task harborSession = await ConnectHarborAsync(server, harborSettings, logging, session.Token).ConfigureAwait(false);
                        try
                        {
                            Vessel vessel = await CreateVesselAsync(server.Client, "e2e-app", git.Origin, ct).ConfigureAwait(false);
                            AssertNull(vessel.WorkingDirectory, "the Admiral has no working directory for the vessel");

                            // Check run through REST.
                            HttpResponseMessage runResponse = await server.Client.PostAsync("/api/v1/check-runs", JsonHelper.ToJsonContent(new
                            {
                                VesselId = vessel.Id,
                                Type = "Build",
                                CommandOverride = "echo rest-check-ran && test -f README.md"
                            }), ct).ConfigureAwait(false);
                            string runText = await runResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            AssertEqual(201, (int)runResponse.StatusCode, "check run (body: " + runText + ")");
                            CheckRun run = JsonHelper.Deserialize<CheckRun>(runText);
                            AssertEqual(CheckRunStatusEnum.Passed, run.Status, run.Output);
                            AssertContains("rest-check-ran", run.Output ?? String.Empty);
                            AssertTrue(PathCanonicalizer.AreEquivalent(git.Checkout, run.WorkingDirectory!), "ran in the Harbor's checkout");

                            // The run_check MCP tool, as Ask Armada calls it.
                            using HttpClient mcp = CreateMcpClient(server);
                            string toolText = await CallToolAsync(mcp, "run_check", new { vesselId = vessel.Id, type = "UnitTest", commandOverride = "echo mcp-check-ran" }).ConfigureAwait(false);
                            CheckRun mcpRun = JsonHelper.Deserialize<CheckRun>(toolText);
                            AssertEqual(CheckRunStatusEnum.Passed, mcpRun.Status, mcpRun.Output);
                            AssertContains("mcp-check-ran", mcpRun.Output ?? String.Empty);

                            // Workspace through REST.
                            HttpResponseMessage treeResponse = await server.Client.GetAsync("/api/v1/workspace/vessels/" + vessel.Id + "/tree", ct).ConfigureAwait(false);
                            AssertStatusCode(HttpStatusCode.OK, treeResponse, "workspace tree");
                            WorkspaceTreeResult tree = await JsonHelper.DeserializeAsync<WorkspaceTreeResult>(treeResponse).ConfigureAwait(false);
                            AssertTrue(tree.Entries.Any(e => e.Name == "README.md"), "the Harbor's checkout was listed");
                            HttpResponseMessage fileResponse = await server.Client.GetAsync("/api/v1/workspace/vessels/" + vessel.Id + "/file?path=README.md", ct).ConfigureAwait(false);
                            AssertStatusCode(HttpStatusCode.OK, fileResponse, "workspace file");
                            WorkspaceFileResponse file = await JsonHelper.DeserializeAsync<WorkspaceFileResponse>(fileResponse).ConfigureAwait(false);
                            AssertEqual("# app\n", file.Content);
                            HttpResponseMessage escape = await server.Client.GetAsync("/api/v1/workspace/vessels/" + vessel.Id + "/file?path=../../remote/app.git/config", ct).ConfigureAwait(false);
                            AssertStatusCode(HttpStatusCode.BadRequest, escape, "traversal is refused");

                            // Readiness names the Harbor.
                            HttpResponseMessage readyResponse = await server.Client.GetAsync("/api/v1/vessels/" + vessel.Id + "/readiness", ct).ConfigureAwait(false);
                            AssertStatusCode(HttpStatusCode.OK, readyResponse, "readiness");
                            VesselReadinessResult readiness = await JsonHelper.DeserializeAsync<VesselReadinessResult>(readyResponse).ConfigureAwait(false);
                            AssertTrue(readiness.HasWorkingDirectory, "a checkout on the Harbor");
                            AssertEqual(HarborId, readiness.HarborId);

                            // A vessel no Harbor has a checkout of.
                            Vessel orphan = await CreateVesselAsync(server.Client, "DocConverter", "https://git.example.invalid/acme/DocConverter.git", ct).ConfigureAwait(false);
                            HttpResponseMessage refused = await server.Client.PostAsync("/api/v1/check-runs", JsonHelper.ToJsonContent(new
                            {
                                VesselId = orphan.Id,
                                Type = "UnitTest",
                                CommandOverride = "echo never"
                            }), ct).ConfigureAwait(false);
                            string refusedText = await refused.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            AssertEqual(409, (int)refused.StatusCode, "no checkout (body: " + refusedText + ")");
                            E2eVesselCheckoutErrorBody body = JsonHelper.Deserialize<E2eVesselCheckoutErrorBody>(refusedText);
                            AssertEqual(ApiResultEnum.Conflict, body.Error);
                            AssertNotNull(body.Data, "typed detail");
                            AssertEqual(VesselCheckoutUnavailableException.ErrorCode, body.Data!.Code);
                            AssertEqual(VesselCheckoutErrorCodeEnum.NoHarborCheckout, body.Data.Reason);
                            AssertEqual(orphan.Id, body.Data.VesselId);
                            AssertEqual(1, body.Data.HarborReasons.Count, "the connected Harbor's reason");

                            HttpResponseMessage orphanTree = await server.Client.GetAsync("/api/v1/workspace/vessels/" + orphan.Id + "/tree", ct).ConfigureAwait(false);
                            AssertStatusCode(HttpStatusCode.Conflict, orphanTree, "workspace without a checkout");

                            string toolError = await CallToolAsync(mcp, "run_check", new { vesselId = orphan.Id, type = "UnitTest", commandOverride = "echo never" }).ConfigureAwait(false);
                            McpToolResultProbe probe = McpToolResultProbe.FromText(toolError);
                            AssertEqual(McpToolErrorCodeEnum.Unavailable, probe.ErrorCode, "the tool error Ask sees (" + probe.Error + ")");
                            AssertEqual(VesselCheckoutUnavailableException.ErrorCode + "." + VesselCheckoutErrorCodeEnum.NoHarborCheckout, probe.Code);
                            AssertFalse(String.IsNullOrWhiteSpace(probe.Error), "the message says what to set");
                        }
                        finally
                        {
                            session.Cancel();
                            try { await harborSession.ConfigureAwait(false); }
                            catch { }
                        }
                    }
                },
                tags: new List<string> { TestTags.Positive, TestTags.EndToEnd }));

            return new TestSuiteDescriptor(SuiteId, "Harbor Checkout API", cases);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Link a real Harbor to the server over its WebSocket endpoint and wait until the handshake is accepted.
        /// </summary>
        private static async Task<Task> ConnectHarborAsync(InProcessArmadaServer server, HarborDockSettings settings, LoggingModule logging, CancellationToken token)
        {
            HarborDockManager docks = new HarborDockManager(() => settings, logging);
            List<HarborCapability> capabilities = new List<HarborCapability> { new HarborCapability { Name = "git", Available = true } };
            HarborLinkClient client = new HarborLinkClient(HarborId, "E2E Mac", capabilities, 2, new LocalHostCommandExecutor(), logging, 0, null, null, docks);
            Uri link = new Uri("ws://127.0.0.1:" + server.Settings.AdmiralPort + server.Settings.Harbor.LinkPath);
            WebSocketHarborTransport transport = new WebSocketHarborTransport(link);

            TaskCompletionSource<bool> connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task session = Task.Run(() => client.RunSessionAsync(transport, token, () => connected.TrySetResult(true)));
            Task finished = await Task.WhenAny(connected.Task, session, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
            if (finished != connected.Task) throw new AssertionException("the Harbor did not link to the Admiral");
            return session;
        }

        private static async Task<Vessel> CreateVesselAsync(HttpClient client, string name, string? repoUrl, CancellationToken token)
        {
            HttpResponseMessage response = await client.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new
            {
                Name = name,
                RepoUrl = repoUrl ?? String.Empty,
                DefaultBranch = "main"
            }), token).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            AssertEqual(201, (int)response.StatusCode, "vessel create (body: " + text + ")");
            return JsonHelper.Deserialize<Vessel>(text);
        }

        private static HttpClient CreateMcpClient(InProcessArmadaServer server)
        {
            HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + server.Settings.McpPort), Timeout = TimeSpan.FromSeconds(120) };
            client.DefaultRequestHeaders.Add("X-Api-Key", server.Settings.ApiKey);
            return client;
        }

        /// <summary>
        /// Call a tool over the MCP HTTP endpoint and return the text of its first content item. A JSON-RPC error or an
        /// untyped isError result fails the test.
        /// </summary>
        private static async Task<string> CallToolAsync(HttpClient mcp, string tool, object arguments)
        {
            HttpRequestMessage init = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/rpc");
            init.Content = JsonHelper.ToJsonContent(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new { protocolVersion = "2024-11-05", capabilities = new { }, clientInfo = new { name = "harbor-checkout-test", version = "1.0" } }
            });
            HttpResponseMessage initResponse = await mcp.SendAsync(init).ConfigureAwait(false);
            string sessionId = initResponse.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values) ? values.First() : String.Empty;

            HttpRequestMessage message = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/rpc");
            message.Content = JsonHelper.ToJsonContent(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = tool, arguments = arguments } });
            if (!String.IsNullOrEmpty(sessionId)) message.Headers.Add("Mcp-Session-Id", sessionId);
            HttpResponseMessage response = await mcp.SendAsync(message).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new AssertionException("MCP " + tool + " failed with HTTP " + (int)response.StatusCode + ": " + body);
            E2eMcpToolEnvelope envelope = JsonHelper.Deserialize<E2eMcpToolEnvelope>(body);
            if (envelope.Error != null) throw new AssertionException("MCP " + tool + " returned a JSON-RPC error: " + body);
            if (envelope.Result == null || envelope.Result.Content.Count == 0) throw new AssertionException("MCP " + tool + " returned no content: " + body);
            if (envelope.Result.IsError) throw new AssertionException("MCP " + tool + " returned an untyped tool error: " + body);
            return envelope.Result.Content[0].Text;
        }

        #endregion
    }
}
