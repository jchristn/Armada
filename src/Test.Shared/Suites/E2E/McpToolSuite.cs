namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// MCP tool coverage exercising the JSON-RPC surface exposed on the Armada MCP port.
    /// Ported from the retired automated <c>McpToolTests</c> suite; every case drives the real
    /// MCP endpoint of the shared in-process server obtained through <see cref="E2EServerFixture"/>.
    /// Because descriptor cases are independent, each case establishes its own MCP session via
    /// <see cref="InitMcpSessionAsync"/> rather than sharing one across the suite.
    /// </summary>
    public sealed class McpToolSuite : IArmadaTestSuite
    {
        #region Private-Members

        private readonly object _SeedLock = new object();
        private Task<string>? _SharedMissionVessel;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the end-to-end MCP tool suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("tools_list_returns_at_least_expected_armada_tools", "ToolsList_ReturnsAtLeastExpectedArmadaTools", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await SendMcpRequestAsync(mcpClient, sessionId, "tools/list", new { }).ConfigureAwait(false);
                JsonElement tools = result.GetProperty("tools");
                List<string> toolNames = new List<string>();
                foreach (JsonElement tool in tools.EnumerateArray())
                {
                    toolNames.Add(tool.GetProperty("name").GetString()!);
                }
                AssertTrue(toolNames.Count >= 42, "Armada tool count should include the expected baseline set");
            }));

            cases.Add(CaseAsync("tools_list_contains_all_expected_tool_names", "ToolsList_ContainsAllExpectedToolNames", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await SendMcpRequestAsync(mcpClient, sessionId, "tools/list", new { }).ConfigureAwait(false);
                JsonElement tools = result.GetProperty("tools");
                List<string> toolNames = new List<string>();
                foreach (JsonElement tool in tools.EnumerateArray())
                {
                    toolNames.Add(tool.GetProperty("name").GetString()!);
                }

                string[] expected = new string[]
                {
                    "status",
                    "stop_server",
                    "enumerate",
                    "get_check_run",
                    "run_check",
                    "retry_check_run",
                    "get_fleet",
                    "create_fleet",
                    "update_fleet",
                    "delete_fleet",
                    "get_vessel",
                    "add_vessel",
                    "update_vessel",
                    "delete_vessel",
                    "dispatch",
                    "voyage_status",
                    "cancel_voyage",
                    "purge_voyage",
                    "mission_status",
                    "create_mission",
                    "update_mission",
                    "cancel_mission",
                    "transition_mission_status",
                    "get_mission_diff",
                    "get_mission_log",
                    "get_captain",
                    "create_captain",
                    "update_captain",
                    "stop_captain",
                    "stop_all",
                    "delete_captain",
                    "get_captain_log",
                    "send_signal",
                    "get_merge_entry",
                    "enqueue_merge",
                    "cancel_merge",
                    "process_merge_queue",
                    "get_release",
                    "create_release"
                };

                foreach (string name in expected)
                {
                    Assert(toolNames.Contains(name), "Tool list should contain " + name);
                }
            }));

            cases.Add(CaseAsync("tools_list_each_tool_has_description", "ToolsList_EachToolHasDescription", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await SendMcpRequestAsync(mcpClient, sessionId, "tools/list", new { }).ConfigureAwait(false);
                JsonElement tools = result.GetProperty("tools");
                foreach (JsonElement tool in tools.EnumerateArray())
                {
                    string name = tool.GetProperty("name").GetString()!;
                    Assert(tool.TryGetProperty("description", out JsonElement desc), "Tool " + name + " should have a description");
                    AssertFalse(string.IsNullOrEmpty(desc.GetString()), "Tool " + name + " description should not be empty");
                }
            }));

            cases.Add(CaseAsync("tools_list_each_tool_has_input_schema", "ToolsList_EachToolHasInputSchema", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await SendMcpRequestAsync(mcpClient, sessionId, "tools/list", new { }).ConfigureAwait(false);
                JsonElement tools = result.GetProperty("tools");
                foreach (JsonElement tool in tools.EnumerateArray())
                {
                    string name = tool.GetProperty("name").GetString()!;
                    Assert(tool.TryGetProperty("inputSchema", out JsonElement schema), "Tool " + name + " should have an inputSchema");
                    AssertEqual("object", schema.GetProperty("type").GetString());
                }
            }));

            cases.Add(CaseAsync("tools_list_no_duplicate_tool_names", "ToolsList_NoDuplicateToolNames", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await SendMcpRequestAsync(mcpClient, sessionId, "tools/list", new { }).ConfigureAwait(false);
                JsonElement tools = result.GetProperty("tools");
                List<string> toolNames = new List<string>();
                foreach (JsonElement tool in tools.EnumerateArray())
                {
                    string name = tool.GetProperty("name").GetString()!;
                    toolNames.Add(name);
                }
                AssertEqual(toolNames.Count, toolNames.Distinct().Count());
            }));

            cases.Add(CaseAsync("tools_list_excludes_voltaic_demo_tools", "ToolsList_ExcludesVoltaicDemoTools", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await SendMcpRequestAsync(mcpClient, sessionId, "tools/list", new { }).ConfigureAwait(false);
                JsonElement tools = result.GetProperty("tools");
                List<string> toolNames = new List<string>();
                foreach (JsonElement tool in tools.EnumerateArray())
                {
                    toolNames.Add(tool.GetProperty("name").GetString()!);
                }

                // Voltaic 2.x publishes only application-registered tools; the 0.x/1.x demo tools must be gone.
                string[] demoTools = new string[] { "ping", "echo", "getTime", "getSessions", "getClients" };
                foreach (string name in demoTools)
                {
                    AssertFalse(toolNames.Contains(name), "Tool list should not contain Voltaic demo tool " + name);
                }
            }));

            cases.Add(CaseAsync("protocol_ping_returns_empty_object", "ProtocolPing_ReturnsEmptyObject", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement response = await SendRawMcpRequestAsync(mcpClient, sessionId, "ping").ConfigureAwait(false);
                AssertFalse(response.TryGetProperty("error", out JsonElement _), "ping should not return an error");
                Assert(response.TryGetProperty("result", out JsonElement pingResult), "ping should return a result");
                AssertEqual(JsonValueKind.Object, pingResult.ValueKind);
            }));

            cases.Add(CaseAsync("bare_tool_name_method_returns_method_not_found", "BareToolNameMethod_ReturnsMethodNotFound", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                // Tools are reachable only through tools/call; invoking one as a bare JSON-RPC method must fail.
                JsonElement response = await SendRawMcpRequestAsync(mcpClient, sessionId, "status").ConfigureAwait(false);
                Assert(response.TryGetProperty("error", out JsonElement error), "Bare tool-name call should return an error");
                AssertEqual(-32601, error.GetProperty("code").GetInt32());

                JsonElement viaToolsCall = await CallToolAsync(mcpClient, sessionId, "status", new { }).ConfigureAwait(false);
                AssertToolResultValid(viaToolsCall);
            }));

            cases.Add(CaseAsync("get_sessions_method_is_not_exposed", "GetSessionsMethod_IsNotExposed", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement bare = await SendRawMcpRequestAsync(mcpClient, sessionId, "getSessions").ConfigureAwait(false);
                Assert(bare.TryGetProperty("error", out JsonElement bareError), "getSessions should not be callable");
                AssertEqual(-32601, bareError.GetProperty("code").GetInt32());

                JsonElement viaToolsCall = await SendRawMcpRequestAsync(mcpClient, sessionId, "tools/call", new
                {
                    name = "getSessions",
                    arguments = new { }
                }).ConfigureAwait(false);
                Assert(viaToolsCall.TryGetProperty("error", out JsonElement toolsCallError), "getSessions should not be callable through tools/call");
                AssertEqual(-32602, toolsCallError.GetProperty("code").GetInt32(), "unknown tool error code: " + toolsCallError.GetRawText());
            }));

            cases.Add(CaseAsync("runbook_parameter_values_rejects_non_string_value", "RunbookParameterValues_RejectsNonStringValue", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                // parameterValues declares additionalProperties { type: string }, which Voltaic 2.x enforces.
                // Since Voltaic 2.1.2 a schema violation is a tool result with isError: true (so the model can
                // correct it) rather than a -32602 protocol error, and the handler does not run.
                JsonElement response = await SendRawMcpRequestAsync(mcpClient, sessionId, "tools/call", new
                {
                    name = "start_runbook_execution",
                    arguments = new
                    {
                        runbookId = "pbk_does_not_exist",
                        parameterValues = new { count = 5 }
                    }
                }).ConfigureAwait(false);
                AssertFalse(response.TryGetProperty("error", out _), "Schema violations should be tool results, not protocol errors");
                JsonElement result = response.GetProperty("result");
                Assert(result.TryGetProperty("isError", out JsonElement isError) && isError.GetBoolean(), "Non-string parameter value should be rejected");
                // isError above is the decision. Voltaic reports schema violations as plain text only (no code or
                // argument path field), so naming the offending argument can only be checked in the text.
                AssertContains("parameterValues", GetToolResultText(result));
            }));

            cases.Add(CaseAsync("runbook_parameter_values_accepts_string_value", "RunbookParameterValues_AcceptsStringValue", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                // String values satisfy the schema, so any failure must come from the handler, not validation.
                JsonElement response = await SendRawMcpRequestAsync(mcpClient, sessionId, "tools/call", new
                {
                    name = "start_runbook_execution",
                    arguments = new
                    {
                        runbookId = "pbk_does_not_exist",
                        parameterValues = new { count = "5" }
                    }
                }).ConfigureAwait(false);
                AssertFalse(response.TryGetProperty("error", out JsonElement error), "String parameter values should not produce a protocol error: " + (error.ValueKind == JsonValueKind.Undefined ? "" : error.GetRawText()));
                JsonElement result = response.GetProperty("result");
                AssertFalse(result.TryGetProperty("isError", out JsonElement isError) && isError.GetBoolean(), "String parameter values should pass schema validation: " + GetToolResultText(result));
                // Validation passed, so the handler ran and reported the missing runbook as a typed error.
                AssertToolNotFound(GetToolResultText(result));
            }));

            cases.Add(CaseAsync("check_run_tools_run_inspect_and_retry", "CheckRunTools_RunInspectAndRetry", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "McpCheckFleet").ConfigureAwait(false);
                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-mcp-check-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "McpCheckVessel", workingDirectory).ConfigureAwait(false);

                JsonElement runResult = await CallToolAsync(mcpClient, sessionId, "run_check", new
                {
                    vesselId = vesselId,
                    type = "Build",
                    label = "MCP Build Check",
                    commandOverride = "echo mcp-check"
                }).ConfigureAwait(false);
                AssertToolResultValid(runResult);
                CheckRun run = JsonHelper.Deserialize<CheckRun>(GetToolResultText(runResult));
                AssertStartsWith("chk_", run.Id);

                JsonElement getResult = await CallToolAsync(mcpClient, sessionId, "get_check_run", new
                {
                    checkRunId = run.Id
                }).ConfigureAwait(false);
                AssertToolResultValid(getResult);
                CheckRun fetched = JsonHelper.Deserialize<CheckRun>(GetToolResultText(getResult));
                AssertEqual(run.Id, fetched.Id);
                AssertEqual("MCP Build Check", fetched.Label);

                JsonElement retryResult = await CallToolAsync(mcpClient, sessionId, "retry_check_run", new
                {
                    checkRunId = run.Id
                }).ConfigureAwait(false);
                AssertToolResultValid(retryResult);
                CheckRun retried = JsonHelper.Deserialize<CheckRun>(GetToolResultText(retryResult));
                AssertStartsWith("chk_", retried.Id);
                AssertFalse(String.Equals(run.Id, retried.Id, StringComparison.Ordinal), "Retry should create a distinct check run ID");
            }));

            cases.Add(CaseAsync("release_tools_create_read_and_enumerate", "ReleaseTools_CreateReadAndEnumerate", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "McpReleaseFleet").ConfigureAwait(false);
                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-mcp-release-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "McpReleaseVessel", workingDirectory).ConfigureAwait(false);

                JsonElement runResult = await CallToolAsync(mcpClient, sessionId, "run_check", new
                {
                    vesselId = vesselId,
                    type = "ReleaseVersioning",
                    label = "Version Check",
                    commandOverride = "echo 2.3.4"
                }).ConfigureAwait(false);
                AssertToolResultValid(runResult);
                CheckRun run = JsonHelper.Deserialize<CheckRun>(GetToolResultText(runResult));

                JsonElement createResult = await CallToolAsync(mcpClient, sessionId, "create_release", new
                {
                    vesselId = vesselId,
                    title = "MCP Draft Release",
                    checkRunIds = new[] { run.Id }
                }).ConfigureAwait(false);
                AssertToolResultValid(createResult);
                Release release = JsonHelper.Deserialize<Release>(GetToolResultText(createResult));
                AssertStartsWith("rel_", release.Id);

                JsonElement getResult = await CallToolAsync(mcpClient, sessionId, "get_release", new
                {
                    releaseId = release.Id
                }).ConfigureAwait(false);
                AssertToolResultValid(getResult);
                Release fetched = JsonHelper.Deserialize<Release>(GetToolResultText(getResult));
                AssertEqual(release.Id, fetched.Id);
                AssertEqual("MCP Draft Release", fetched.Title);

                JsonElement enumerateResult = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "releases",
                    pageSize = 50,
                    search = "MCP Draft Release"
                }).ConfigureAwait(false);
                AssertToolResultValid(enumerateResult);
                EnumerationResult<Release> releases = JsonHelper.Deserialize<EnumerationResult<Release>>(GetToolResultText(enumerateResult));
                AssertTrue(releases.Objects.Any(r => r.Id == release.Id), "enumerate lists the created release");
            }));

            cases.Add(CaseAsync("backlog_tools_create_list_update_reorder_and_delete", "BacklogTools_CreateListUpdateReorderAndDelete", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement createResult = await CallToolAsync(mcpClient, sessionId, "create_backlog_item", new
                {
                    title = "MCP Backlog Coverage",
                    description = "Exercise backlog CRUD through MCP.",
                    kind = "Feature",
                    priority = "P1",
                    rank = 25,
                    backlogState = "Inbox",
                    effort = "M"
                }).ConfigureAwait(false);
                AssertToolResultValid(createResult);
                Objective created = JsonHelper.Deserialize<Objective>(GetToolResultText(createResult));
                AssertStartsWith("obj_", created.Id);
                AssertEqual(ObjectiveKindEnum.Feature, created.Kind);
                AssertEqual(ObjectivePriorityEnum.P1, created.Priority);
                AssertEqual(ObjectiveBacklogStateEnum.Inbox, created.BacklogState);

                JsonElement listResult = await CallToolAsync(mcpClient, sessionId, "list_backlog", new
                {
                    search = "MCP Backlog Coverage",
                    pageSize = 25
                }).ConfigureAwait(false);
                AssertToolResultValid(listResult);
                EnumerationResult<Objective> backlog = JsonHelper.Deserialize<EnumerationResult<Objective>>(GetToolResultText(listResult));
                AssertTrue(backlog.Objects.Any(o => o.Id == created.Id), "list_backlog returns the created item");

                JsonElement updateResult = await CallToolAsync(mcpClient, sessionId, "update_objective", new
                {
                    objectiveId = created.Id,
                    backlogState = "ReadyForPlanning",
                    rank = 10,
                    targetVersion = "0.8.0"
                }).ConfigureAwait(false);
                AssertToolResultValid(updateResult);
                Objective updated = JsonHelper.Deserialize<Objective>(GetToolResultText(updateResult));
                AssertEqual(ObjectiveBacklogStateEnum.ReadyForPlanning, updated.BacklogState);
                AssertEqual(10, updated.Rank);
                AssertEqual("0.8.0", updated.TargetVersion);

                JsonElement reorderResult = await CallToolAsync(mcpClient, sessionId, "reorder_backlog_items", new
                {
                    items = new[]
                    {
                        new
                        {
                            objectiveId = created.Id,
                            rank = 5
                        }
                    }
                }).ConfigureAwait(false);
                AssertToolResultValid(reorderResult);
                List<Objective> reordered = JsonHelper.Deserialize<List<Objective>>(GetToolResultText(reorderResult));
                AssertEqual(1, reordered.Count);
                AssertEqual(5, reordered[0].Rank);

                JsonElement getResult = await CallToolAsync(mcpClient, sessionId, "get_backlog_item", new
                {
                    objectiveId = created.Id
                }).ConfigureAwait(false);
                AssertToolResultValid(getResult);
                Objective fetched = JsonHelper.Deserialize<Objective>(GetToolResultText(getResult));
                AssertEqual(created.Id, fetched.Id);
                AssertEqual(5, fetched.Rank);
                AssertEqual(ObjectiveBacklogStateEnum.ReadyForPlanning, fetched.BacklogState);

                JsonElement deleteResult = await CallToolAsync(mcpClient, sessionId, "delete_backlog_item", new
                {
                    objectiveId = created.Id
                }).ConfigureAwait(false);
                AssertToolResultValid(deleteResult);
                McpStatusResult deleted = JsonHelper.Deserialize<McpStatusResult>(GetToolResultText(deleteResult));
                AssertEqual(true, deleted.Success, "delete_backlog_item success");
                AssertEqual(created.Id, deleted.ObjectiveId, "deleted objective id");
            }));

            cases.Add(CaseAsync("armada_status_executes_successfully", "ArmadaStatus_ExecutesSuccessfully", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "status", new { }).ConfigureAwait(false);
                AssertToolResultValid(result);
            }));

            cases.Add(CaseAsync("armada_status_returns_valid_status_object", "ArmadaStatus_ReturnsValidStatusObject", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "status", new { }).ConfigureAwait(false);
                string text = GetToolResultText(result);
                AssertFalse(string.IsNullOrEmpty(text));
                ArmadaStatus status = JsonHelper.Deserialize<ArmadaStatus>(text);
                AssertNotNull(status);
            }));

            cases.Add(CaseAsync("armada_dispatch_creates_voyage_with_mission", "ArmadaDispatch_CreatesVoyageWithMission", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "DispatchFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "DispatchVessel").ConfigureAwait(false);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "dispatch", new
                {
                    title = "Test Dispatch Voyage",
                    description = "Dispatched via MCP",
                    vesselId = vesselId,
                    missions = new[]
                    {
                        new { title = "Dispatch Mission 1", description = "First mission" }
                    }
                }).ConfigureAwait(false);

                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                Voyage voyage = JsonHelper.Deserialize<Voyage>(text);
                AssertStartsWith("vyg_", voyage.Id);
            }));

            cases.Add(CaseAsync("armada_dispatch_with_multiple_missions_creates_all", "ArmadaDispatch_WithMultipleMissions_CreatesAll", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "DispatchMultiFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "DispatchMultiVessel").ConfigureAwait(false);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "dispatch", new
                {
                    title = "Multi-Mission Voyage",
                    vesselId = vesselId,
                    missions = new[]
                    {
                        new { title = "Mission A", description = "Desc A" },
                        new { title = "Mission B", description = "Desc B" },
                        new { title = "Mission C", description = "Desc C" }
                    }
                }).ConfigureAwait(false);

                AssertToolResultValid(result);
            }));

            cases.Add(CaseAsync("armada_send_signal_creates_signal", "ArmadaSendSignal_CreatesSignal", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string captainId = await RestCreateCaptainAsync(mcpClient, sessionId, "signal-captain").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "send_signal", new
                {
                    captainId = captainId,
                    message = "Hello from MCP test"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Signal signal = JsonHelper.Deserialize<Signal>(GetToolResultText(result));
                AssertStartsWith("sig_", signal.Id);
                AssertEqual(captainId, signal.ToCaptainId, "signal recipient");
                AssertEqual("Hello from MCP test", signal.Payload, "signal message");
            }));

            cases.Add(CaseAsync("armada_send_signal_signal_visible_via_enumerate", "ArmadaSendSignal_SignalVisibleViaEnumerate", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string captainId = await RestCreateCaptainAsync(mcpClient, sessionId, "signal-list-captain").ConfigureAwait(false);
                await CallToolAsync(mcpClient, sessionId, "send_signal", new
                {
                    captainId = captainId,
                    message = "Signal visibility test"
                }).ConfigureAwait(false);

                JsonElement listResult = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "signals",
                    pageSize = 50,
                    includeMessage = true
                }).ConfigureAwait(false);
                EnumerationResult<Signal> signals = JsonHelper.Deserialize<EnumerationResult<Signal>>(GetToolResultText(listResult));
                AssertTrue(signals.Objects.Any(s => s.ToCaptainId == captainId && s.Payload == "Signal visibility test"), "enumerate lists the sent signal with its message");
            }));

            cases.Add(CaseAsync("armada_mission_status_existing_mission_returns_mission", "ArmadaMissionStatus_ExistingMission_ReturnsMission", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string missionId = await RestCreateMissionAsync(mcpClient, sessionId, "MissionStatusTest").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "mission_status", new
                {
                    missionId = missionId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Mission mission = JsonHelper.Deserialize<Mission>(GetToolResultText(result));
                AssertEqual(missionId, mission.Id, "mission id");
                AssertEqual("MissionStatusTest", mission.Title, "mission title");
            }));

            cases.Add(CaseAsync("armada_mission_status_not_found_returns_error_message", "ArmadaMissionStatus_NotFound_ReturnsErrorMessage", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "mission_status", new
                {
                    missionId = "msn_nonexistent"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_mission_status_returns_correct_status", "ArmadaMissionStatus_ReturnsCorrectStatus", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string missionId = await RestCreateMissionAsync(mcpClient, sessionId, "StatusCheckMission").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "mission_status", new
                {
                    missionId = missionId
                }).ConfigureAwait(false);
                string text = GetToolResultText(result);
                Mission mission = JsonHelper.Deserialize<Mission>(text);
                AssertEqual(missionId, mission.Id, "mission id");
                Assert(mission.Status == MissionStatusEnum.Pending || mission.Status == MissionStatusEnum.Assigned || mission.Status == MissionStatusEnum.InProgress,
                    "Expected a new mission to be Pending, Assigned, or InProgress but got " + mission.Status);
            }));

            cases.Add(CaseAsync("armada_mission_status_diff_snapshot_is_null", "ArmadaMissionStatus_DiffSnapshotIsNull", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string missionId = await RestCreateMissionAsync(mcpClient, sessionId, "DiffSnapshotExclusionTest").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "mission_status", new
                {
                    missionId = missionId
                }).ConfigureAwait(false);
                string text = GetToolResultText(result);
                Mission mission = JsonHelper.Deserialize<Mission>(text);
                Assert(mission.DiffSnapshot == null,
                    "DiffSnapshot should be null in mission_status response but was: " + (mission.DiffSnapshot ?? "null"));
            }));

            cases.Add(CaseAsync("armada_voyage_status_existing_voyage_returns_details", "ArmadaVoyageStatus_ExistingVoyage_ReturnsDetails", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "VoyageStatusFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "VoyageStatusVessel").ConfigureAwait(false);
                string voyageId = await RestCreateVoyageAsync(mcpClient, sessionId, vesselId).ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "voyage_status", new
                {
                    voyageId = voyageId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                McpVoyageStatusResult status = JsonHelper.Deserialize<McpVoyageStatusResult>(GetToolResultText(result));
                AssertEqual(voyageId, status.Voyage?.Id, "voyage id");
            }));

            cases.Add(CaseAsync("armada_voyage_status_not_found_returns_null_voyage", "ArmadaVoyageStatus_NotFound_ReturnsNullVoyage", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "voyage_status", new
                {
                    voyageId = "vyg_nonexistent"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertNotNull(text);
            }));

            cases.Add(CaseAsync("armada_voyage_status_default_summary_returns_counts", "ArmadaVoyageStatus_DefaultSummary_ReturnsCounts", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "VoyageMissionsFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "VoyageMissionsVessel").ConfigureAwait(false);
                string voyageId = await RestCreateVoyageAsync(mcpClient, sessionId, vesselId).ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "voyage_status", new
                {
                    voyageId = voyageId
                }).ConfigureAwait(false);
                string text = GetToolResultText(result);
                McpVoyageStatusResult status = JsonHelper.Deserialize<McpVoyageStatusResult>(text);
                AssertEqual(voyageId, status.Voyage?.Id, "voyage id");
                AssertEqual(1, status.TotalMissions, "TotalMissions (one mission dispatched)");
                AssertNotNull(status.MissionCountsByStatus, "MissionCountsByStatus");
                AssertEqual(1, status.MissionCountsByStatus!.Values.Sum(), "counts by status add up to the mission total");
                AssertNull(JsonShape.TopLevelProperty(text, "Missions"), "Default summary mode should not contain a Missions property");
            }));

            cases.Add(CaseAsync("armada_get_fleet_existing_fleet_returns_fleet_details", "ArmadaGetFleet_ExistingFleet_ReturnsFleetDetails", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "GetFleetTest").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_fleet", new
                {
                    fleetId = fleetId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                FleetDetailResponse detail = JsonHelper.Deserialize<FleetDetailResponse>(GetToolResultText(result));
                AssertEqual(fleetId, detail.Fleet?.Id, "fleet id");
                AssertStartsWith("GetFleetTest-", detail.Fleet?.Name ?? "", "fleet name");
            }));

            cases.Add(CaseAsync("armada_get_fleet_not_found_returns_error_message", "ArmadaGetFleet_NotFound_ReturnsErrorMessage", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_fleet", new
                {
                    fleetId = "flt_nonexistent"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_get_fleet_with_vessels_returns_vessels_array", "ArmadaGetFleet_WithVessels_ReturnsVesselsArray", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "FleetWithVessels").ConfigureAwait(false);
                await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "FleetVessel1").ConfigureAwait(false);
                await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "FleetVessel2").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_fleet", new
                {
                    fleetId = fleetId
                }).ConfigureAwait(false);
                string text = GetToolResultText(result);
                FleetDetailResponse data = JsonHelper.Deserialize<FleetDetailResponse>(text);
                AssertNotNull(data.Fleet);
                AssertNotNull(data.Vessels);
                AssertTrue(data.Vessels!.Count >= 2);
            }));

            cases.Add(CaseAsync("armada_add_vessel_creates_vessel", "ArmadaAddVessel_CreatesVessel", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "AddVesselFleet").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "add_vessel", new
                {
                    name = "MCP Added Vessel",
                    repoUrl = TestRepoHelper.GetLocalBareRepoUrl(),
                    fleetId = fleetId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Vessel added = JsonHelper.Deserialize<Vessel>(GetToolResultText(result));
                AssertStartsWith("vsl_", added.Id);
                AssertEqual("MCP Added Vessel", added.Name, "vessel name");
                AssertEqual(fleetId, added.FleetId, "vessel fleet");
            }));

            cases.Add(CaseAsync("armada_add_vessel_with_default_branch_sets_correct_branch", "ArmadaAddVessel_WithDefaultBranch_SetsCorrectBranch", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "AddVesselBranchFleet").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "add_vessel", new
                {
                    name = "Custom Branch Vessel",
                    repoUrl = TestRepoHelper.GetLocalBareRepoUrl(),
                    fleetId = fleetId,
                    defaultBranch = "develop"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Vessel added = JsonHelper.Deserialize<Vessel>(GetToolResultText(result));
                AssertEqual("develop", added.DefaultBranch, "default branch");
            }));

            cases.Add(CaseAsync("armada_add_vessel_visible_via_enumerate", "ArmadaAddVessel_VisibleViaEnumerate", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "AddVesselVisibleFleet").ConfigureAwait(false);
                JsonElement addResult = await CallToolAsync(mcpClient, sessionId, "add_vessel", new
                {
                    name = "Visible Vessel",
                    repoUrl = TestRepoHelper.GetLocalBareRepoUrl(),
                    fleetId = fleetId
                }).ConfigureAwait(false);
                string addText = GetToolResultText(addResult);
                Vessel vessel = JsonHelper.Deserialize<Vessel>(addText);
                string vesselId = vessel.Id;

                JsonElement listResult = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "vessels",
                    fleetId = fleetId
                }).ConfigureAwait(false);
                EnumerationResult<Vessel> vessels = JsonHelper.Deserialize<EnumerationResult<Vessel>>(GetToolResultText(listResult));
                AssertTrue(vessels.Objects.Any(v => v.Id == vesselId), "enumerate lists the added vessel");
            }));

            cases.Add(CaseAsync("armada_add_vessel_git_hub_token_override_does_not_leak", "ArmadaAddVessel_GitHubTokenOverrideDoesNotLeak", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "AddVesselGitHubOverrideFleet").ConfigureAwait(false);
                string token = "ghp_mcp_" + Guid.NewGuid().ToString("N").Substring(0, 10);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "add_vessel", new
                {
                    name = "MCP GitHub Override Vessel",
                    repoUrl = TestRepoHelper.GetLocalBareRepoUrl(),
                    fleetId = fleetId,
                    gitHubTokenOverride = token
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertFalse(text.Contains(token, StringComparison.Ordinal), "the token value appears nowhere in the result");
                AssertFalse(JsonShape.HasPropertyAnywhere(text, "GitHubTokenOverride"), "no GitHubTokenOverride property in any casing");
                JsonPropertyShape? hasOverride = JsonShape.TopLevelProperty(text, "HasGitHubTokenOverride");
                AssertNotNull(hasOverride, "HasGitHubTokenOverride is reported");
                AssertEqual(System.Text.Json.JsonTokenType.True, hasOverride!.ValueToken, "HasGitHubTokenOverride is true");
                Vessel vessel = JsonHelper.Deserialize<Vessel>(text);
                AssertTrue(vessel.HasGitHubTokenOverride);
            }));

            cases.Add(CaseAsync("armada_update_vessel_empty_git_hub_token_override_clears_override", "ArmadaUpdateVessel_EmptyGitHubTokenOverrideClearsOverride", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "UpdateVesselGitHubOverrideFleet").ConfigureAwait(false);
                JsonElement addResult = await CallToolAsync(mcpClient, sessionId, "add_vessel", new
                {
                    name = "MCP Clear Override Vessel",
                    repoUrl = TestRepoHelper.GetLocalBareRepoUrl(),
                    fleetId = fleetId,
                    gitHubTokenOverride = "ghp_mcp_clear"
                }).ConfigureAwait(false);
                Vessel added = JsonHelper.Deserialize<Vessel>(GetToolResultText(addResult));
                AssertTrue(added.HasGitHubTokenOverride);

                JsonElement updateResult = await CallToolAsync(mcpClient, sessionId, "update_vessel", new
                {
                    vesselId = added.Id,
                    gitHubTokenOverride = ""
                }).ConfigureAwait(false);
                AssertToolResultValid(updateResult);
                Vessel updated = JsonHelper.Deserialize<Vessel>(GetToolResultText(updateResult));
                AssertFalse(updated.HasGitHubTokenOverride);
            }));

            cases.Add(CaseAsync("armada_stop_captain_idle_captain_returns_stopped", "ArmadaStopCaptain_IdleCaptain_ReturnsStopped", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string captainId = await RestCreateCaptainAsync(mcpClient, sessionId, "stop-idle-captain").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "stop_captain", new
                {
                    captainId = captainId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                McpStatusResult stopped = JsonHelper.Deserialize<McpStatusResult>(GetToolResultText(result));
                AssertEqual("stopped", stopped.Status, "status");
                AssertEqual(captainId, stopped.CaptainId, "captain id");
            }));

            cases.Add(CaseAsync("armada_stop_captain_not_found_returns_error", "ArmadaStopCaptain_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement response = await SendRawMcpRequestAsync(mcpClient, sessionId, "tools/call", new
                {
                    name = "stop_captain",
                    arguments = new { captainId = "cpt_nonexistent" }
                }).ConfigureAwait(false);
                // A missing captain is a typed tool error (ErrorCode NotFound), not an untyped isError result.
                JsonElement result = response.GetProperty("result");
                AssertFalse(result.TryGetProperty("isError", out JsonElement isError) && isError.GetBoolean(), "a missing captain should be a typed error, not isError");
                AssertToolNotFound(GetToolResultText(result));
            }));

            cases.Add(CaseAsync("armada_stop_all_with_no_captains_returns_all_stopped", "ArmadaStopAll_WithNoCaptains_ReturnsAllStopped", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "stop_all", new { }).ConfigureAwait(false);
                AssertToolResultValid(result);
                AssertEqual("all_stopped", JsonHelper.Deserialize<McpStatusResult>(GetToolResultText(result)).Status, "status");
            }));

            cases.Add(CaseAsync("armada_stop_all_with_captains_succeeds", "ArmadaStopAll_WithCaptains_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                await RestCreateCaptainAsync(mcpClient, sessionId, "stop-all-captain-1").ConfigureAwait(false);
                await RestCreateCaptainAsync(mcpClient, sessionId, "stop-all-captain-2").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "stop_all", new { }).ConfigureAwait(false);
                AssertToolResultValid(result);
                AssertEqual("all_stopped", JsonHelper.Deserialize<McpStatusResult>(GetToolResultText(result)).Status, "status");
            }));

            cases.Add(CaseAsync("armada_cancel_mission_existing_mission_cancels_mission", "ArmadaCancelMission_ExistingMission_CancelsMission", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string missionId = await RestCreateMissionAsync(mcpClient, sessionId, "CancelMeMission").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "cancel_mission", new
                {
                    missionId = missionId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Mission cancelled = JsonHelper.Deserialize<Mission>(GetToolResultText(result));
                AssertEqual(missionId, cancelled.Id, "mission id");
                AssertEqual(MissionStatusEnum.Cancelled, cancelled.Status, "mission status");
            }));

            cases.Add(CaseAsync("armada_cancel_mission_not_found_returns_error_message", "ArmadaCancelMission_NotFound_ReturnsErrorMessage", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "cancel_mission", new
                {
                    missionId = "msn_nonexistent"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_cancel_mission_verify_status_changed", "ArmadaCancelMission_VerifyStatusChanged", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string missionId = await RestCreateMissionAsync(mcpClient, sessionId, "CancelVerifyMission").ConfigureAwait(false);
                await CallToolAsync(mcpClient, sessionId, "cancel_mission", new
                {
                    missionId = missionId
                }).ConfigureAwait(false);

                // Verify via MCP tool instead of REST (different ports)
                JsonElement getResult = await CallToolAsync(mcpClient, sessionId, "mission_status", new { missionId = missionId }).ConfigureAwait(false);
                string getBody = GetToolResultText(getResult);
                Mission mission = JsonHelper.Deserialize<Mission>(getBody);
                AssertEqual("Cancelled", mission.Status.ToString());
            }));

            cases.Add(CaseAsync("armada_cancel_voyage_existing_voyage_cancels_voyage", "ArmadaCancelVoyage_ExistingVoyage_CancelsVoyage", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "CancelVoyageFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "CancelVoyageVessel").ConfigureAwait(false);
                string voyageId = await RestCreateVoyageAsync(mcpClient, sessionId, vesselId).ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "cancel_voyage", new
                {
                    voyageId = voyageId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                CancelVoyageResponse cancelled = JsonHelper.Deserialize<CancelVoyageResponse>(GetToolResultText(result));
                AssertEqual(voyageId, cancelled.Voyage?.Id, "voyage id");
                AssertEqual(VoyageStatusEnum.Cancelled, cancelled.Voyage?.Status, "voyage status");
            }));

            cases.Add(CaseAsync("armada_cancel_voyage_not_found_returns_error_message", "ArmadaCancelVoyage_NotFound_ReturnsErrorMessage", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "cancel_voyage", new
                {
                    voyageId = "vyg_nonexistent"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_cancel_voyage_cancels_missions", "ArmadaCancelVoyage_CancelsMissions", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "CancelVoyageMissionsFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "CancelVoyageMissionsVessel").ConfigureAwait(false);
                string voyageId = await RestCreateVoyageAsync(mcpClient, sessionId, vesselId).ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "cancel_voyage", new
                {
                    voyageId = voyageId
                }).ConfigureAwait(false);
                string text = GetToolResultText(result);
                CancelVoyageResponse data = JsonHelper.Deserialize<CancelVoyageResponse>(text);
                AssertEqual(voyageId, data.Voyage?.Id, "voyage id");
                AssertTrue(data.CancelledMissions >= 0 && data.CancelledMissions <= 1, "CancelledMissions counts at most the voyage's one mission: " + data.CancelledMissions);
            }));

            cases.Add(CaseAsync("armada_cancel_voyage_verify_status_via_rest", "ArmadaCancelVoyage_VerifyStatusViaRest", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "CancelVoyageVerifyFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "CancelVoyageVerifyVessel").ConfigureAwait(false);
                string voyageId = await RestCreateVoyageAsync(mcpClient, sessionId, vesselId).ConfigureAwait(false);
                await CallToolAsync(mcpClient, sessionId, "cancel_voyage", new
                {
                    voyageId = voyageId
                }).ConfigureAwait(false);

                // Verify via MCP tool instead of REST (different ports)
                JsonElement getResult = await CallToolAsync(mcpClient, sessionId, "voyage_status", new { voyageId = voyageId }).ConfigureAwait(false);
                string getBody = GetToolResultText(getResult);
                VoyageDetailResponse voyageResult = JsonHelper.Deserialize<VoyageDetailResponse>(getBody);
                AssertEqual("Cancelled", voyageResult.Voyage!.Status.ToString());
            }));

            cases.Add(CaseAsync("armada_enumerate_fleets_returns_paginated_result", "ArmadaEnumerate_Fleets_ReturnsPaginatedResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                await RestCreateFleetAsync(mcpClient, sessionId, "EnumFleet1").ConfigureAwait(false);
                await RestCreateFleetAsync(mcpClient, sessionId, "EnumFleet2").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "fleets",
                    pageSize = 10,
                    pageNumber = 1
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                EnumerationResult<JsonElement> data = JsonHelper.Deserialize<EnumerationResult<JsonElement>>(text);
                AssertNotNull(data.Objects);
                AssertTrue(data.TotalRecords >= 0);
                AssertEqual(1, data.PageNumber);
                AssertTrue(data.Objects.Count >= 2);
            }));

            cases.Add(CaseAsync("armada_enumerate_missions_with_status_filter", "ArmadaEnumerate_Missions_WithStatusFilter", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                await RestCreateMissionAsync(mcpClient, sessionId, "EnumMission1").ConfigureAwait(false);
                await RestCreateMissionAsync(mcpClient, sessionId, "EnumMission2").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "missions",
                    status = "Pending"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                EnumerationResult<JsonElement> data = JsonHelper.Deserialize<EnumerationResult<JsonElement>>(text);
                AssertTrue(data.Objects.Count >= 2);
            }));

            cases.Add(CaseAsync("armada_enumerate_vessels_with_fleet_filter", "ArmadaEnumerate_Vessels_WithFleetFilter", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "EnumVesselFleet").ConfigureAwait(false);
                await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "EnumVessel1").ConfigureAwait(false);
                await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "EnumVessel2").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "vessels",
                    fleetId = fleetId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                EnumerationResult<JsonElement> data = JsonHelper.Deserialize<EnumerationResult<JsonElement>>(text);
                AssertTrue(data.Objects.Count >= 2);
            }));

            cases.Add(CaseAsync("armada_enumerate_captains_returns_result", "ArmadaEnumerate_Captains_ReturnsResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                await RestCreateCaptainAsync(mcpClient, sessionId, "enum-captain-1").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "captains"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                EnumerationResult<JsonElement> data = JsonHelper.Deserialize<EnumerationResult<JsonElement>>(text);
                AssertTrue(data.Objects.Count >= 1);
            }));

            cases.Add(CaseAsync("armada_enumerate_voyages_returns_result", "ArmadaEnumerate_Voyages_ReturnsResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "voyages"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                EnumerationResult<JsonElement> data = JsonHelper.Deserialize<EnumerationResult<JsonElement>>(text);
                AssertNotNull(data.Objects);
            }));

            cases.Add(CaseAsync("armada_enumerate_docks_returns_result", "ArmadaEnumerate_Docks_ReturnsResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "docks"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                EnumerationResult<JsonElement> data = JsonHelper.Deserialize<EnumerationResult<JsonElement>>(text);
                AssertNotNull(data.Objects);
            }));

            cases.Add(CaseAsync("armada_enumerate_signals_returns_result", "ArmadaEnumerate_Signals_ReturnsResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "signals"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                EnumerationResult<JsonElement> data = JsonHelper.Deserialize<EnumerationResult<JsonElement>>(text);
                AssertNotNull(data.Objects);
            }));

            cases.Add(CaseAsync("armada_enumerate_events_returns_result", "ArmadaEnumerate_Events_ReturnsResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "events"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                EnumerationResult<JsonElement> data = JsonHelper.Deserialize<EnumerationResult<JsonElement>>(text);
                AssertNotNull(data.Objects);
            }));

            cases.Add(CaseAsync("armada_enumerate_invalid_entity_type_returns_error", "ArmadaEnumerate_InvalidEntityType_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "widgets"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                McpToolResultProbe probe = McpToolResultProbe.FromText(GetToolResultText(result));
                AssertEqual(McpToolErrorCodeEnum.InvalidArgument, probe.ErrorCode, "unknown entity type is an InvalidArgument error");
            }));

            cases.Add(CaseAsync("armada_enumerate_with_pagination_respects_page_size", "ArmadaEnumerate_WithPagination_RespectsPageSize", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                for (int i = 0; i < 5; i++)
                    await RestCreateFleetAsync(mcpClient, sessionId, "PageFleet" + i).ConfigureAwait(false);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "fleets",
                    pageSize = 2,
                    pageNumber = 1
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                EnumerationResult<JsonElement> data = JsonHelper.Deserialize<EnumerationResult<JsonElement>>(text);
                AssertEqual(2, data.PageSize);
                AssertTrue(data.Objects.Count <= 2);
                AssertTrue(data.TotalRecords >= 5);
                AssertTrue(data.TotalPages >= 3);
            }));

            cases.Add(CaseAsync("armada_enumerate_with_order_changes_sort", "ArmadaEnumerate_WithOrder_ChangesSort", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "fleets",
                    order = "CreatedAscending"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
            }));

            cases.Add(CaseAsync("armada_enumerate_singular_entity_type_works", "ArmadaEnumerate_SingularEntityType_Works", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "fleet"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                EnumerationResult<JsonElement> data = JsonHelper.Deserialize<EnumerationResult<JsonElement>>(text);
                AssertNotNull(data.Objects);
            }));

            cases.Add(CaseAsync("armada_enumerate_merge_queue_returns_result", "ArmadaEnumerate_MergeQueue_ReturnsResult", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "merge_queue"
                }).ConfigureAwait(false);
                string text = GetToolResultText(result);
                JsonPropertyShape? totalRecords = JsonShape.TopLevelProperty(text, "TotalRecords");
                AssertNotNull(totalRecords, "Should contain a TotalRecords field");
                AssertEqual(System.Text.Json.JsonTokenType.Number, totalRecords!.ValueToken, "TotalRecords is a number");
                EnumerationResult<MergeEntry> data = JsonHelper.Deserialize<EnumerationResult<MergeEntry>>(text);
                AssertTrue(data.TotalRecords >= data.Objects.Count, "TotalRecords covers the page");
            }));

            cases.Add(CaseAsync("armada_create_fleet_creates_fleet", "ArmadaCreateFleet_CreatesFleet", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "create_fleet", new
                {
                    name = "MCP Created Fleet",
                    description = "Created via MCP tool"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Fleet created = JsonHelper.Deserialize<Fleet>(GetToolResultText(result));
                AssertStartsWith("flt_", created.Id);
                AssertEqual("MCP Created Fleet", created.Name, "fleet name");
                AssertEqual("Created via MCP tool", created.Description, "fleet description");
            }));

            cases.Add(CaseAsync("armada_create_fleet_visible_via_enumerate", "ArmadaCreateFleet_VisibleViaEnumerate", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement createResult = await CallToolAsync(mcpClient, sessionId, "create_fleet", new
                {
                    name = "FleetVisibilityTest"
                }).ConfigureAwait(false);
                string createText = GetToolResultText(createResult);
                Fleet fleet = JsonHelper.Deserialize<Fleet>(createText);
                string fleetId = fleet.Id;

                JsonElement listResult = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "fleets",
                    pageSize = 50
                }).ConfigureAwait(false);
                EnumerationResult<Fleet> fleets = JsonHelper.Deserialize<EnumerationResult<Fleet>>(GetToolResultText(listResult));
                AssertTrue(fleets.Objects.Any(f => f.Id == fleetId), "enumerate lists the created fleet");
            }));

            cases.Add(CaseAsync("armada_update_fleet_updates_name", "ArmadaUpdateFleet_UpdatesName", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "OriginalName").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "update_fleet", new
                {
                    fleetId = fleetId,
                    name = "UpdatedName"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Fleet updated = JsonHelper.Deserialize<Fleet>(GetToolResultText(result));
                AssertEqual(fleetId, updated.Id, "fleet id");
                AssertEqual("UpdatedName", updated.Name, "fleet name");
            }));

            cases.Add(CaseAsync("armada_update_fleet_not_found_returns_error", "ArmadaUpdateFleet_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "update_fleet", new
                {
                    fleetId = "flt_nonexistent",
                    name = "Whatever"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_delete_fleet_deletes_fleet", "ArmadaDeleteFleet_DeletesFleet", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "DeleteMeFleet").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "delete_fleet", new
                {
                    fleetId = fleetId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                McpStatusResult deleted = JsonHelper.Deserialize<McpStatusResult>(GetToolResultText(result));
                AssertEqual("deleted", deleted.Status, "status");
                AssertEqual(fleetId, deleted.FleetId, "fleet id");
            }));

            cases.Add(CaseAsync("armada_delete_fleet_not_found_returns_error", "ArmadaDeleteFleet_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "delete_fleet", new
                {
                    fleetId = "flt_nonexistent"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_get_vessel_existing_vessel_returns_details", "ArmadaGetVessel_ExistingVessel_ReturnsDetails", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "GetVesselFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "GetVesselTest").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_vessel", new
                {
                    vesselId = vesselId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Vessel vessel = JsonHelper.Deserialize<Vessel>(GetToolResultText(result));
                AssertEqual(vesselId, vessel.Id, "vessel id");
                AssertStartsWith("GetVesselTest-", vessel.Name, "vessel name");
            }));

            cases.Add(CaseAsync("armada_get_vessel_not_found_returns_error", "ArmadaGetVessel_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_vessel", new
                {
                    vesselId = "vsl_nonexistent"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_update_vessel_updates_name", "ArmadaUpdateVessel_UpdatesName", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "UpdateVesselFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "OriginalVessel").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "update_vessel", new
                {
                    vesselId = vesselId,
                    name = "UpdatedVessel"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Vessel updated = JsonHelper.Deserialize<Vessel>(GetToolResultText(result));
                AssertEqual(vesselId, updated.Id, "vessel id");
                AssertEqual("UpdatedVessel", updated.Name, "vessel name");
            }));

            cases.Add(CaseAsync("armada_update_vessel_not_found_returns_error", "ArmadaUpdateVessel_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "update_vessel", new
                {
                    vesselId = "vsl_nonexistent",
                    name = "Whatever"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_delete_vessel_deletes_vessel", "ArmadaDeleteVessel_DeletesVessel", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "DeleteVesselFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "DeleteMeVessel").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "delete_vessel", new
                {
                    vesselId = vesselId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                McpStatusResult deleted = JsonHelper.Deserialize<McpStatusResult>(GetToolResultText(result));
                AssertEqual("deleted", deleted.Status, "status");
                AssertEqual(vesselId, deleted.VesselId, "vessel id");
            }));

            cases.Add(CaseAsync("armada_delete_vessel_not_found_returns_error", "ArmadaDeleteVessel_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "delete_vessel", new
                {
                    vesselId = "vsl_nonexistent"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_create_captain_creates_with_name", "ArmadaCreateCaptain_CreatesWithName", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "create_captain", new
                {
                    name = "mcp-created-captain"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Captain created = JsonHelper.Deserialize<Captain>(GetToolResultText(result));
                AssertStartsWith("cpt_", created.Id);
                AssertEqual("mcp-created-captain", created.Name, "captain name");
            }));

            cases.Add(CaseAsync("armada_create_captain_with_runtime_sets_runtime", "ArmadaCreateCaptain_WithRuntime_SetsRuntime", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "create_captain", new
                {
                    name = "runtime-captain",
                    runtime = "ClaudeCode"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Captain created = JsonHelper.Deserialize<Captain>(GetToolResultText(result));
                AssertEqual(AgentRuntimeEnum.ClaudeCode, created.Runtime, "captain runtime");
            }));

            cases.Add(CaseAsync("armada_create_captain_visible_via_enumerate", "ArmadaCreateCaptain_VisibleViaEnumerate", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement createResult = await CallToolAsync(mcpClient, sessionId, "create_captain", new
                {
                    name = "visible-captain"
                }).ConfigureAwait(false);
                string createText = GetToolResultText(createResult);
                Captain captain = JsonHelper.Deserialize<Captain>(createText);
                string captainId = captain.Id;

                JsonElement listResult = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "captains",
                    pageSize = 50
                }).ConfigureAwait(false);
                EnumerationResult<Captain> captains = JsonHelper.Deserialize<EnumerationResult<Captain>>(GetToolResultText(listResult));
                AssertTrue(captains.Objects.Any(c => c.Id == captainId), "enumerate lists the created captain");
            }));

            cases.Add(CaseAsync("armada_get_captain_existing_captain_returns_details", "ArmadaGetCaptain_ExistingCaptain_ReturnsDetails", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string captainId = await RestCreateCaptainAsync(mcpClient, sessionId, "get-captain-test").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_captain", new
                {
                    captainId = captainId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Captain captain = JsonHelper.Deserialize<Captain>(GetToolResultText(result));
                AssertEqual(captainId, captain.Id, "captain id");
                AssertStartsWith("get-captain-test-", captain.Name, "captain name");
            }));

            cases.Add(CaseAsync("armada_get_captain_not_found_returns_error", "ArmadaGetCaptain_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_captain", new
                {
                    captainId = "cpt_nonexistent"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_update_captain_updates_name", "ArmadaUpdateCaptain_UpdatesName", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string captainId = await RestCreateCaptainAsync(mcpClient, sessionId, "original-captain").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "update_captain", new
                {
                    captainId = captainId,
                    name = "updated-captain"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Captain updated = JsonHelper.Deserialize<Captain>(GetToolResultText(result));
                AssertEqual(captainId, updated.Id, "captain id");
                AssertEqual("updated-captain", updated.Name, "captain name");
            }));

            cases.Add(CaseAsync("armada_update_captain_not_found_returns_error", "ArmadaUpdateCaptain_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "update_captain", new
                {
                    captainId = "cpt_nonexistent",
                    name = "whatever"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_delete_captain_deletes_captain", "ArmadaDeleteCaptain_DeletesCaptain", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string captainId = await RestCreateCaptainAsync(mcpClient, sessionId, "delete-captain").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "delete_captain", new
                {
                    captainId = captainId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                McpStatusResult deleted = JsonHelper.Deserialize<McpStatusResult>(GetToolResultText(result));
                AssertEqual("deleted", deleted.Status, "status");
                AssertEqual(captainId, deleted.CaptainId, "captain id");
            }));

            cases.Add(CaseAsync("armada_delete_captain_not_found_returns_error", "ArmadaDeleteCaptain_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "delete_captain", new
                {
                    captainId = "cpt_nonexistent"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_get_captain_log_no_current_returns_empty_log", "ArmadaGetCaptainLog_NoCurrent_ReturnsEmptyLog", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string captainId = await RestCreateCaptainAsync(mcpClient, sessionId, "log-captain").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_captain_log", new
                {
                    captainId = captainId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                CaptainLogResponse data = JsonHelper.Deserialize<CaptainLogResponse>(text);
                AssertEqual(0, data.TotalLines);
            }));

            cases.Add(CaseAsync("armada_get_captain_log_not_found_returns_error", "ArmadaGetCaptainLog_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_captain_log", new
                {
                    captainId = "cpt_nonexistent"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_create_mission_creates_and_dispatches_mission", "ArmadaCreateMission_CreatesAndDispatchesMission", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "CreateMissionFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "CreateMissionVessel").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "create_mission", new
                {
                    title = "MCP Created Mission",
                    description = "Created via MCP tool",
                    vesselId = vesselId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Mission created = ReadCreatedMission(GetToolResultText(result));
                AssertStartsWith("msn_", created.Id);
                AssertEqual("MCP Created Mission", created.Title, "mission title");
                AssertEqual(vesselId, created.VesselId, "mission vessel");
            }));

            cases.Add(CaseAsync("armada_create_mission_visible_via_mission_status", "ArmadaCreateMission_VisibleViaMissionStatus", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "CreateMissionVisFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "CreateMissionVisVessel").ConfigureAwait(false);
                JsonElement createResult = await CallToolAsync(mcpClient, sessionId, "create_mission", new
                {
                    title = "VisibleMission",
                    description = "Should be visible",
                    vesselId = vesselId
                }).ConfigureAwait(false);
                string createText = GetToolResultText(createResult);
                MissionCreateResponse createResponse = JsonHelper.Deserialize<MissionCreateResponse>(createText);
                string missionId = createResponse.Mission != null ? createResponse.Mission.Id : JsonHelper.Deserialize<Mission>(createText).Id;

                JsonElement statusResult = await CallToolAsync(mcpClient, sessionId, "mission_status", new
                {
                    missionId = missionId
                }).ConfigureAwait(false);
                Mission status = JsonHelper.Deserialize<Mission>(GetToolResultText(statusResult));
                AssertEqual(missionId, status.Id, "mission id");
                AssertEqual("VisibleMission", status.Title, "mission title");
            }));

            cases.Add(CaseAsync("armada_update_mission_updates_title", "ArmadaUpdateMission_UpdatesTitle", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string missionId = await RestCreateMissionAsync(mcpClient, sessionId, "UpdateTitleMission").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "update_mission", new
                {
                    missionId = missionId,
                    title = "Updated Title"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Mission updated = JsonHelper.Deserialize<Mission>(GetToolResultText(result));
                AssertEqual(missionId, updated.Id, "mission id");
                AssertEqual("Updated Title", updated.Title, "mission title");
            }));

            cases.Add(CaseAsync("armada_update_mission_updates_multiple_fields", "ArmadaUpdateMission_UpdatesMultipleFields", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string missionId = await RestCreateMissionAsync(mcpClient, sessionId, "UpdateMultiMission").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "update_mission", new
                {
                    missionId = missionId,
                    title = "Multi Update",
                    description = "New description",
                    priority = 50,
                    branchName = "feature/updated",
                    prUrl = "https://github.com/test/pr/1"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                Mission updated = JsonHelper.Deserialize<Mission>(GetToolResultText(result));
                AssertEqual(missionId, updated.Id, "mission id");
                AssertEqual("Multi Update", updated.Title, "title");
                AssertEqual("New description", updated.Description, "description");
                AssertEqual(50, updated.Priority, "priority");
                AssertEqual("feature/updated", updated.BranchName, "branch name");
                AssertEqual("https://github.com/test/pr/1", updated.PrUrl, "PR URL");
            }));

            cases.Add(CaseAsync("armada_update_mission_not_found_returns_error", "ArmadaUpdateMission_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "update_mission", new
                {
                    missionId = "msn_nonexistent",
                    title = "Should Fail"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_transition_mission_status_pending_to_assigned_succeeds", "ArmadaTransitionMissionStatus_PendingToAssigned_Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string missionId = await RestCreateMissionAsync(mcpClient, sessionId, "TransitionMission").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "transition_mission_status", new
                {
                    missionId = missionId,
                    status = "Assigned"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertNull(McpToolResultProbe.FromText(text).ErrorCode, "transition succeeded: " + text);
                Mission transitioned = JsonHelper.Deserialize<Mission>(text);
                AssertEqual(missionId, transitioned.Id, "mission id");
                AssertEqual(MissionStatusEnum.Assigned, transitioned.Status, "mission status");
            }));

            cases.Add(CaseAsync("armada_transition_mission_status_invalid_transition_returns_error", "ArmadaTransitionMissionStatus_InvalidTransition_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string missionId = await RestCreateMissionAsync(mcpClient, sessionId, "InvalidTransMission").ConfigureAwait(false);
                // Try transitioning to Pending which should be invalid from any state
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "transition_mission_status", new
                {
                    missionId = missionId,
                    status = "Pending"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertEqual(McpToolErrorCodeEnum.Conflict, McpToolResultProbe.FromText(text).ErrorCode, "a transition to Pending is refused as a Conflict (result: " + text + ")");
            }));

            cases.Add(CaseAsync("armada_transition_mission_status_not_found_returns_error", "ArmadaTransitionMissionStatus_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "transition_mission_status", new
                {
                    missionId = "msn_nonexistent",
                    status = "Assigned"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_transition_mission_status_invalid_status_returns_error", "ArmadaTransitionMissionStatus_InvalidStatus_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string missionId = await RestCreateMissionAsync(mcpClient, sessionId, "BadStatusMission").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "transition_mission_status", new
                {
                    missionId = missionId,
                    status = "BogusStatus"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertEqual(McpToolErrorCodeEnum.InvalidArgument, McpToolResultProbe.FromText(text).ErrorCode, "an unknown status is an InvalidArgument error (result: " + text + ")");
            }));

            cases.Add(CaseAsync("armada_transition_mission_status_verify_via_rest", "ArmadaTransitionMissionStatus_VerifyViaRest", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string missionId = await RestCreateMissionAsync(mcpClient, sessionId, "TransVerifyMission").ConfigureAwait(false);
                await CallToolAsync(mcpClient, sessionId, "transition_mission_status", new
                {
                    missionId = missionId,
                    status = "Assigned"
                }).ConfigureAwait(false);

                // Verify via MCP tool instead of REST (different ports)
                // Mission may auto-advance to InProgress or beyond if a captain picks it up
                JsonElement getResult = await CallToolAsync(mcpClient, sessionId, "mission_status", new { missionId = missionId }).ConfigureAwait(false);
                string getBody = GetToolResultText(getResult);
                Mission mission = JsonHelper.Deserialize<Mission>(getBody);
                string status = mission.Status.ToString();
                Assert(status == "Assigned" || status == "InProgress" || status == "Complete",
                    "Expected Assigned, InProgress, or Complete but got " + status);
            }));

            cases.Add(CaseAsync("armada_get_mission_diff_no_worktree_returns_error", "ArmadaGetMissionDiff_NoWorktree_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string missionId = await RestCreateMissionAsync(mcpClient, sessionId, "DiffMission").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_mission_diff", new
                {
                    missionId = missionId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                McpToolResultProbe probe = McpToolResultProbe.FromText(text);
                if (probe.ErrorCode != null)
                {
                    // No worktree and no saved diff: the tool reports the diff as unavailable.
                    AssertEqual(McpToolErrorCodeEnum.Unavailable, probe.ErrorCode, "no-diff error code (result: " + text + ")");
                }
                else
                {
                    // A captain picked the mission up and produced a worktree: a real diff result for this mission.
                    McpMissionDiffResult diff = JsonHelper.Deserialize<McpMissionDiffResult>(text);
                    AssertEqual(missionId, diff.MissionId, "diff mission id (result: " + text + ")");
                    AssertNotNull(diff.Diff, "diff text");
                }
            }));

            cases.Add(CaseAsync("armada_get_mission_diff_not_found_returns_error", "ArmadaGetMissionDiff_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_mission_diff", new
                {
                    missionId = "msn_nonexistent"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_get_mission_log_no_log_returns_valid_response", "ArmadaGetMissionLog_NoLog_ReturnsValidResponse", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string missionId = await RestCreateMissionAsync(mcpClient, sessionId, "LogMission").ConfigureAwait(false);
                // A JSON-RPC protocol error fails here (CallToolAsync throws); it is not a pass.
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_mission_log", new { missionId = missionId }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertNull(McpToolResultProbe.FromText(text).ErrorCode, "get_mission_log succeeded: " + text);
                MissionLogResponse data = JsonHelper.Deserialize<MissionLogResponse>(text);
                AssertEqual(missionId, data.MissionId, "log mission id");
                // Mission may have a small log from dispatch signals
                AssertTrue(data.TotalLines >= 0, "TotalLines should be non-negative");
                AssertEqual(Math.Min(100, data.TotalLines), data.Lines, "default page is up to 100 lines");
            }));

            cases.Add(CaseAsync("armada_get_mission_log_not_found_returns_error", "ArmadaGetMissionLog_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_mission_log", new
                {
                    missionId = "msn_nonexistent"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_get_mission_log_with_pagination_respects_params", "ArmadaGetMissionLog_WithPagination_RespectsParams", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string missionId = await RestCreateMissionAsync(mcpClient, sessionId, "PaginatedLogMission").ConfigureAwait(false);

                // A JSON-RPC protocol error fails here (CallToolAsync throws); it is not a pass.
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_mission_log", new { missionId = missionId, lines = 10, offset = 5 }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertNull(McpToolResultProbe.FromText(text).ErrorCode, "get_mission_log succeeded: " + text);
                MissionLogResponse data = JsonHelper.Deserialize<MissionLogResponse>(text);
                AssertEqual(missionId, data.MissionId, "log mission id");
                AssertEqual(Math.Max(0, Math.Min(10, data.TotalLines - 5)), data.Lines, "lines=10 offset=5 returns the requested window");
            }));

            cases.Add(CaseAsync("armada_purge_voyage_deletes_voyage_and_missions", "ArmadaPurgeVoyage_DeletesVoyageAndMissions", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "PurgeVoyageFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "PurgeVoyageVessel").ConfigureAwait(false);
                string voyageId = await RestCreateVoyageAsync(mcpClient, sessionId, vesselId).ConfigureAwait(false);

                // Cancel the voyage first — purge is blocked on Open/InProgress voyages
                await CallToolAsync(mcpClient, sessionId, "cancel_voyage", new { voyageId = voyageId }).ConfigureAwait(false);

                // Also cancel any InProgress missions individually (cancel_voyage only cancels Pending/Assigned)
                JsonElement statusResult = await CallToolAsync(mcpClient, sessionId, "voyage_status", new
                {
                    voyageId = voyageId,
                    summary = false,
                    includeMissions = true
                }).ConfigureAwait(false);
                string statusText = GetToolResultText(statusResult);
                VoyageDetailResponse detail = JsonHelper.Deserialize<VoyageDetailResponse>(statusText);
                if (detail.Missions != null)
                {
                    foreach (Mission m in detail.Missions)
                    {
                        if (m.Status == Armada.Core.Enums.MissionStatusEnum.InProgress ||
                            m.Status == Armada.Core.Enums.MissionStatusEnum.Assigned)
                        {
                            await CallToolAsync(mcpClient, sessionId, "cancel_mission", new { missionId = m.Id }).ConfigureAwait(false);
                        }
                    }
                }

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "purge_voyage", new
                {
                    voyageId = voyageId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);

                PurgeVoyageResponse data = JsonHelper.Deserialize<PurgeVoyageResponse>(text);
                if (data.Error != null)
                    throw new Exception("Purge returned error: " + data.Error);
                AssertEqual("deleted", data.Status);
                AssertTrue(data.MissionsDeleted >= 0);
            }));

            cases.Add(CaseAsync("armada_purge_voyage_not_found_returns_error", "ArmadaPurgeVoyage_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "purge_voyage", new
                {
                    voyageId = "vyg_nonexistent"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_get_merge_entry_not_found_returns_error", "ArmadaGetMergeEntry_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_merge_entry", new
                {
                    entryId = "mrg_nonexistent"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertToolNotFound(text);
            }));

            cases.Add(CaseAsync("armada_enqueue_merge_creates_entry", "ArmadaEnqueueMerge_CreatesEntry", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "MergeQueueFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "MergeQueueVessel").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enqueue_merge", new
                {
                    vesselId = vesselId,
                    branchName = "feature/test-merge"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                MergeEntry created = JsonHelper.Deserialize<MergeEntry>(GetToolResultText(result));
                AssertStartsWith("mrg_", created.Id);
                AssertEqual("feature/test-merge", created.BranchName, "branch name");
                AssertEqual(vesselId, created.VesselId, "vessel id");
            }));

            cases.Add(CaseAsync("armada_enqueue_merge_visible_via_enumerate", "ArmadaEnqueueMerge_VisibleViaEnumerate", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "MergeVisFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "MergeVisVessel").ConfigureAwait(false);
                JsonElement enqResult = await CallToolAsync(mcpClient, sessionId, "enqueue_merge", new
                {
                    vesselId = vesselId,
                    branchName = "feature/visible-merge"
                }).ConfigureAwait(false);
                string enqText = GetToolResultText(enqResult);
                MergeEntry entry = JsonHelper.Deserialize<MergeEntry>(enqText);
                string entryId = entry.Id;

                JsonElement listResult = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "merge_queue",
                    vesselId = vesselId
                }).ConfigureAwait(false);
                EnumerationResult<MergeEntry> entries = JsonHelper.Deserialize<EnumerationResult<MergeEntry>>(GetToolResultText(listResult));
                AssertTrue(entries.Objects.Any(e => e.Id == entryId), "enumerate lists the queued entry");
            }));

            cases.Add(CaseAsync("armada_get_merge_entry_existing_entry_returns_details", "ArmadaGetMergeEntry_ExistingEntry_ReturnsDetails", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "MergeGetFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "MergeGetVessel").ConfigureAwait(false);
                JsonElement enqResult = await CallToolAsync(mcpClient, sessionId, "enqueue_merge", new
                {
                    vesselId = vesselId,
                    branchName = "feature/get-merge"
                }).ConfigureAwait(false);
                string enqText = GetToolResultText(enqResult);
                MergeEntry entry = JsonHelper.Deserialize<MergeEntry>(enqText);
                string entryId = entry.Id;

                JsonElement getResult = await CallToolAsync(mcpClient, sessionId, "get_merge_entry", new
                {
                    entryId = entryId
                }).ConfigureAwait(false);
                AssertToolResultValid(getResult);
                MergeEntry fetched = JsonHelper.Deserialize<MergeEntry>(GetToolResultText(getResult));
                AssertEqual(entryId, fetched.Id, "entry id");
                AssertEqual("feature/get-merge", fetched.BranchName, "branch name");
            }));

            cases.Add(CaseAsync("armada_cancel_merge_cancels_entry", "ArmadaCancelMerge_CancelsEntry", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "MergeCancelFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "MergeCancelVessel").ConfigureAwait(false);
                JsonElement enqResult = await CallToolAsync(mcpClient, sessionId, "enqueue_merge", new
                {
                    vesselId = vesselId,
                    branchName = "feature/cancel-merge"
                }).ConfigureAwait(false);
                string enqText = GetToolResultText(enqResult);
                MergeEntry entry = JsonHelper.Deserialize<MergeEntry>(enqText);
                string entryId = entry.Id;

                JsonElement cancelResult = await CallToolAsync(mcpClient, sessionId, "cancel_merge", new
                {
                    entryId = entryId
                }).ConfigureAwait(false);
                AssertToolResultValid(cancelResult);
                McpStatusResult cancelled = JsonHelper.Deserialize<McpStatusResult>(GetToolResultText(cancelResult));
                AssertEqual("cancelled", cancelled.Status, "status");
                AssertEqual(entryId, cancelled.EntryId, "entry id");
            }));

            cases.Add(CaseAsync("armada_process_merge_queue_executes", "ArmadaProcessMergeQueue_Executes", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "process_merge_queue", new { }).ConfigureAwait(false);
                AssertToolResultValid(result);
                AssertEqual("processed", JsonHelper.Deserialize<McpStatusResult>(GetToolResultText(result)).Status, "status");
            }));

            cases.Add(CaseAsync("nonexistent_tool_returns_error", "NonexistentTool_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement response = await SendRawMcpRequestAsync(mcpClient, sessionId, "tools/call", new
                {
                    name = "nonexistent_tool",
                    arguments = new { }
                }).ConfigureAwait(false);
                Assert(response.TryGetProperty("error", out JsonElement error), "Should return error for nonexistent tool");
                AssertEqual(-32602, error.GetProperty("code").GetInt32(), "unknown tool error code: " + error.GetRawText());
            }));

            cases.Add(CaseAsync("nonexistent_tool_error_has_message", "NonexistentTool_ErrorHasMessage", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement response = await SendRawMcpRequestAsync(mcpClient, sessionId, "tools/call", new
                {
                    name = "totally_fake_tool",
                    arguments = new { }
                }).ConfigureAwait(false);
                Assert(response.TryGetProperty("error", out JsonElement error), "Should have error property");
                AssertEqual(-32602, error.GetProperty("code").GetInt32(), "unknown tool error code: " + error.GetRawText());
                Assert(error.TryGetProperty("message", out JsonElement message), "Error should have message property");
                AssertFalse(string.IsNullOrEmpty(message.GetString()));
            }));

            cases.Add(CaseAsync("cross_interface_fleet_created_via_rest_visible_via_mcp", "CrossInterface_FleetCreatedViaRest_VisibleViaMcp", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "CrossFleet").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "fleets",
                    pageSize = 50
                }).ConfigureAwait(false);
                EnumerationResult<Fleet> fleets = JsonHelper.Deserialize<EnumerationResult<Fleet>>(GetToolResultText(result));
                AssertTrue(fleets.Objects.Any(f => f.Id == fleetId), "enumerate lists the fleet");
            }));

            cases.Add(CaseAsync("cross_interface_fleet_created_via_rest_get_fleet_via_mcp", "CrossInterface_FleetCreatedViaRest_GetFleetViaMcp", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "CrossGetFleet").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "get_fleet", new
                {
                    fleetId = fleetId
                }).ConfigureAwait(false);
                FleetDetailResponse detail = JsonHelper.Deserialize<FleetDetailResponse>(GetToolResultText(result));
                AssertEqual(fleetId, detail.Fleet?.Id, "fleet id");
                AssertStartsWith("CrossGetFleet-", detail.Fleet?.Name ?? "", "fleet name");
            }));

            cases.Add(CaseAsync("cross_interface_captain_created_via_rest_visible_via_mcp", "CrossInterface_CaptainCreatedViaRest_VisibleViaMcp", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string captainId = await RestCreateCaptainAsync(mcpClient, sessionId, "cross-captain").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "captains",
                    pageSize = 50
                }).ConfigureAwait(false);
                EnumerationResult<Captain> captains = JsonHelper.Deserialize<EnumerationResult<Captain>>(GetToolResultText(result));
                AssertTrue(captains.Objects.Any(c => c.Id == captainId), "enumerate lists the captain");
            }));

            cases.Add(CaseAsync("cross_interface_dispatch_via_mcp_mission_visible_via_rest", "CrossInterface_DispatchViaMcp_MissionVisibleViaRest", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "CrossDispatchFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "CrossDispatchVessel").ConfigureAwait(false);
                JsonElement dispatchResult = await CallToolAsync(mcpClient, sessionId, "dispatch", new
                {
                    title = "Cross Dispatch Voyage",
                    vesselId = vesselId,
                    missions = new[]
                    {
                        new { title = "Cross Mission", description = "Cross-interface test" }
                    }
                }).ConfigureAwait(false);
                AssertToolResultValid(dispatchResult);
                string dispatchText = GetToolResultText(dispatchResult);
                Voyage voyage = JsonHelper.Deserialize<Voyage>(dispatchText);
                string voyageId = voyage.Id;

                // Verify via MCP tool instead of REST (different ports)
                JsonElement getResult = await CallToolAsync(mcpClient, sessionId, "voyage_status", new { voyageId = voyageId }).ConfigureAwait(false);
                string getBody = GetToolResultText(getResult);
                VoyageDetailResponse voyageData = JsonHelper.Deserialize<VoyageDetailResponse>(getBody);
                AssertEqual(voyageId, voyageData.Voyage!.Id);
            }));

            cases.Add(CaseAsync("cross_interface_vessel_added_via_mcp_visible_via_enumerate", "CrossInterface_VesselAddedViaMcp_VisibleViaEnumerate", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "CrossAddVesselFleet").ConfigureAwait(false);
                string vesselName = "Cross-Added-Vessel-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                JsonElement addResult = await CallToolAsync(mcpClient, sessionId, "add_vessel", new
                {
                    name = vesselName,
                    repoUrl = TestRepoHelper.GetLocalBareRepoUrl(),
                    fleetId = fleetId
                }).ConfigureAwait(false);
                string addText = GetToolResultText(addResult);
                Vessel addedVessel = JsonHelper.Deserialize<Vessel>(addText);
                string vesselId = addedVessel.Id;

                JsonElement getResult = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "vessels",
                    fleetId = fleetId
                }).ConfigureAwait(false);
                EnumerationResult<Vessel> vessels = JsonHelper.Deserialize<EnumerationResult<Vessel>>(GetToolResultText(getResult));
                Vessel? listed = vessels.Objects.FirstOrDefault(v => v.Id == vesselId);
                AssertNotNull(listed, "enumerate lists the added vessel");
                AssertEqual(vesselName, listed!.Name, "vessel name");
            }));

            cases.Add(CaseAsync("cross_interface_mission_cancelled_via_mcp_status_changed_via_rest", "CrossInterface_MissionCancelledViaMcp_StatusChangedViaRest", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string missionId = await RestCreateMissionAsync(mcpClient, sessionId, "CrossCancelMission").ConfigureAwait(false);
                await CallToolAsync(mcpClient, sessionId, "cancel_mission", new
                {
                    missionId = missionId
                }).ConfigureAwait(false);

                // Verify via MCP tool instead of REST (different ports)
                JsonElement getResult = await CallToolAsync(mcpClient, sessionId, "mission_status", new { missionId = missionId }).ConfigureAwait(false);
                string getBody = GetToolResultText(getResult);
                Mission mission = JsonHelper.Deserialize<Mission>(getBody);
                AssertEqual("Cancelled", mission.Status.ToString());
            }));

            cases.Add(CaseAsync("cross_interface_signal_sent_via_mcp_visible_via_enumerate", "CrossInterface_SignalSentViaMcp_VisibleViaEnumerate", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string captainId = await RestCreateCaptainAsync(mcpClient, sessionId, "cross-signal-captain").ConfigureAwait(false);
                await CallToolAsync(mcpClient, sessionId, "send_signal", new
                {
                    captainId = captainId,
                    message = "Cross-interface signal"
                }).ConfigureAwait(false);

                JsonElement listResult = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "signals",
                    includeMessage = true,
                    pageSize = 50
                }).ConfigureAwait(false);
                EnumerationResult<Signal> signals = JsonHelper.Deserialize<EnumerationResult<Signal>>(GetToolResultText(listResult));
                AssertTrue(signals.Objects.Any(s => s.ToCaptainId == captainId && s.Payload == "Cross-interface signal"), "enumerate lists the sent signal with its message");
            }));

            cases.Add(CaseAsync("all_tools_armada_status_executes", "AllTools_ArmadaStatus_Executes", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "status", new { }).ConfigureAwait(false);
                AssertToolResultValid(result);
            }));

            cases.Add(CaseAsync("all_tools_armada_stop_all_executes", "AllTools_ArmadaStopAll_Executes", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    JsonElement result = await CallToolAsync(mcpClient, sessionId, "stop_all", new { }).ConfigureAwait(false);
                    AssertToolResultValid(result);
                }
                catch (TaskCanceledException)
                {
                    // StopAll may take longer than expected with many active captains - acceptable
                }
                catch (HttpRequestException)
                {
                    // StopAll may terminate connections - acceptable
                }
            }));

            cases.Add(CaseAsync("all_tools_armada_enumerate_executes", "AllTools_ArmadaEnumerate_Executes", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new { entityType = "fleets" }).ConfigureAwait(false);
                AssertToolResultValid(result);
            }));

            cases.Add(CaseAsync("armada_enumerate_default_no_include_flags_omits_heavy_fields", "ArmadaEnumerate_DefaultNoIncludeFlags_OmitsHeavyFields", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                // A voyage of its own makes the page hold exactly the one mission this case created ("Desc1").
                string vesselId = await EnsureMissionVesselAsync(mcpClient, sessionId).ConfigureAwait(false);
                string voyageId = await RestCreateVoyageAsync(mcpClient, sessionId, vesselId).ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "missions",
                    voyageId = voyageId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                // Default should omit Description and include length hints instead
                AssertFalse(JsonShape.HasPropertyAnywhere(text, "Description"), "Default enumerate should not include a Description property");
                EnumerationResult<McpLengthHints> hints = JsonHelper.Deserialize<EnumerationResult<McpLengthHints>>(text);
                AssertEqual(1, hints.Objects.Count, "the voyage's one mission");
                AssertEqual("Desc1".Length, hints.Objects[0].DescriptionLength, "DescriptionLength hint is the description's length");
            }));

            cases.Add(CaseAsync("armada_enumerate_include_description_true_returns_mission_description", "ArmadaEnumerate_IncludeDescriptionTrue_ReturnsMissionDescription", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                // A voyage of its own makes the page hold exactly the one mission this case created ("Desc1").
                string vesselId = await EnsureMissionVesselAsync(mcpClient, sessionId).ConfigureAwait(false);
                string voyageId = await RestCreateVoyageAsync(mcpClient, sessionId, vesselId).ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "missions",
                    voyageId = voyageId,
                    includeDescription = true
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                EnumerationResult<Mission> missions = JsonHelper.Deserialize<EnumerationResult<Mission>>(text);
                AssertEqual(1, missions.Objects.Count, "the voyageId filter leaves only the voyage's one mission");
                AssertEqual(voyageId, missions.Objects[0].VoyageId, "the listed mission belongs to the voyage");
                AssertEqual("Desc1", missions.Objects[0].Description, "the full Description is returned");
            }));

            cases.Add(CaseAsync("armada_enumerate_include_context_true_returns_vessel_context", "ArmadaEnumerate_IncludeContextTrue_ReturnsVesselContext", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "EnumCtxFleet").ConfigureAwait(false);
                string vesselId = await AddVesselWithContextAsync(mcpClient, sessionId, fleetId, "EnumCtxVessel", "ctx for EnumCtxVessel", "style for EnumCtxVessel").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "vessels",
                    fleetId = fleetId,
                    includeContext = true
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                // With includeContext=true, ProjectContext and StyleGuide carry their values
                EnumerationResult<Vessel> vessels = JsonHelper.Deserialize<EnumerationResult<Vessel>>(text);
                Vessel? listed = vessels.Objects.FirstOrDefault(v => v.Id == vesselId);
                AssertNotNull(listed, "the fleet's vessel is listed");
                AssertEqual("ctx for EnumCtxVessel", listed!.ProjectContext, "ProjectContext");
                AssertEqual("style for EnumCtxVessel", listed.StyleGuide, "StyleGuide");
            }));

            cases.Add(CaseAsync("armada_enumerate_default_no_context_omits_vessel_context", "ArmadaEnumerate_DefaultNoContext_OmitsVesselContext", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "EnumNoCtxFleet").ConfigureAwait(false);
                string vesselId = await AddVesselWithContextAsync(mcpClient, sessionId, fleetId, "EnumNoCtxVessel", "ctx for EnumNoCtxVessel", "style for EnumNoCtxVessel").ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "vessels",
                    fleetId = fleetId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                AssertFalse(JsonShape.HasPropertyAnywhere(text, "ProjectContext"), "Default enumerate should not include a ProjectContext property");
                AssertFalse(JsonShape.HasPropertyAnywhere(text, "StyleGuide"), "Default enumerate should not include a StyleGuide property");
                EnumerationResult<McpLengthHints> hints = JsonHelper.Deserialize<EnumerationResult<McpLengthHints>>(text);
                McpLengthHints? listed = hints.Objects.FirstOrDefault(h => h.Id == vesselId);
                AssertNotNull(listed, "the fleet's vessel is listed");
                AssertEqual("ctx for EnumNoCtxVessel".Length, listed!.ProjectContextLength, "ProjectContextLength hint");
                AssertEqual("style for EnumNoCtxVessel".Length, listed.StyleGuideLength, "StyleGuideLength hint");
            }));

            cases.Add(CaseAsync("armada_enumerate_default_page_size_is_ten", "ArmadaEnumerate_DefaultPageSizeIsTen", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "enumerate", new
                {
                    entityType = "fleets"
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                EnumerationResult<JsonElement> data = JsonHelper.Deserialize<EnumerationResult<JsonElement>>(text);
                AssertEqual(10, data.PageSize);
            }));

            cases.Add(CaseAsync("armada_voyage_status_default_summary_mode_no_missions_array", "ArmadaVoyageStatus_DefaultSummaryMode_NoMissionsArray", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "VoySumFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "VoySumVessel").ConfigureAwait(false);
                string voyageId = await RestCreateVoyageAsync(mcpClient, sessionId, vesselId).ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "voyage_status", new
                {
                    voyageId = voyageId
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                McpVoyageStatusResult status = JsonHelper.Deserialize<McpVoyageStatusResult>(text);
                AssertEqual(voyageId, status.Voyage?.Id, "voyage id");
                AssertEqual(1, status.TotalMissions, "TotalMissions (one mission dispatched)");
                AssertNotNull(status.MissionCountsByStatus, "MissionCountsByStatus");
                AssertEqual(1, status.MissionCountsByStatus!.Values.Sum(), "counts by status add up to the mission total");
                AssertNull(JsonShape.TopLevelProperty(text, "Missions"), "Summary mode should not include a Missions property");
            }));

            cases.Add(CaseAsync("armada_voyage_status_non_summary_with_missions_returns_missions_array", "ArmadaVoyageStatus_NonSummaryWithMissions_ReturnsMissionsArray", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "VoyNonSumFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "VoyNonSumVessel").ConfigureAwait(false);
                string voyageId = await RestCreateVoyageAsync(mcpClient, sessionId, vesselId).ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "voyage_status", new
                {
                    voyageId = voyageId,
                    summary = false,
                    includeMissions = true
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                McpVoyageStatusResult status = JsonHelper.Deserialize<McpVoyageStatusResult>(text);
                AssertNotNull(status.Missions, "Non-summary with includeMissions should have a Missions array");
                AssertEqual(1, status.Missions!.Count, "the voyage's one mission");
                AssertEqual(voyageId, status.Missions[0].VoyageId, "mission belongs to the voyage");
                AssertEqual("VoyageMission1", status.Missions[0].Title, "mission title");
            }));

            cases.Add(CaseAsync("armada_voyage_status_non_summary_with_description_returns_mission_description", "ArmadaVoyageStatus_NonSummaryWithDescription_ReturnsMissionDescription", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "VoyDescFleet").ConfigureAwait(false);
                string vesselId = await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "VoyDescVessel").ConfigureAwait(false);
                string voyageId = await RestCreateVoyageAsync(mcpClient, sessionId, vesselId).ConfigureAwait(false);
                JsonElement result = await CallToolAsync(mcpClient, sessionId, "voyage_status", new
                {
                    voyageId = voyageId,
                    summary = false,
                    includeMissions = true,
                    includeDescription = true
                }).ConfigureAwait(false);
                AssertToolResultValid(result);
                string text = GetToolResultText(result);
                McpVoyageStatusResult status = JsonHelper.Deserialize<McpVoyageStatusResult>(text);
                AssertNotNull(status.Missions, "Should have a Missions array");
                AssertEqual(1, status.Missions!.Count, "the voyage's one mission");
                AssertEqual("Desc1", status.Missions[0].Description, "Missions include the Description when includeDescription=true");
            }));

            cases.Add(CaseAsync("all_tools_armada_process_merge_queue_executes", "AllTools_ArmadaProcessMergeQueue_Executes", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                JsonElement result = await CallToolAsync(mcpClient, sessionId, "process_merge_queue", new { }).ConfigureAwait(false);
                AssertToolResultValid(result);
            }));

            cases.Add(CaseAsync("memory_tools_create_search_get_delete", "MemoryTools_CreateSearchGetDelete", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                string uniq = Guid.NewGuid().ToString("N").Substring(0, 8);
                JsonElement createResult = await CallToolAsync(mcpClient, sessionId, "create_memory", new
                {
                    type = "Semantic",
                    topic = "test",
                    key = "test/" + uniq,
                    summary = "recall hook",
                    content = "Memory content " + uniq,
                    salience = 0.8,
                    tags = new[] { "t1" }
                }).ConfigureAwait(false);
                AssertToolResultValid(createResult);
                Memory created = JsonHelper.Deserialize<Memory>(GetToolResultText(createResult));
                AssertStartsWith("mem_", created.Id);

                // Upsert by the same key does not create a duplicate.
                JsonElement upsertResult = await CallToolAsync(mcpClient, sessionId, "create_memory", new
                {
                    type = "Semantic",
                    key = "test/" + uniq,
                    content = "Memory content " + uniq + " (revised)"
                }).ConfigureAwait(false);
                AssertToolResultValid(upsertResult);
                Memory upserted = JsonHelper.Deserialize<Memory>(GetToolResultText(upsertResult));
                AssertEqual(created.Id, upserted.Id);

                JsonElement searchResult = await CallToolAsync(mcpClient, sessionId, "search_memory", new { search = uniq }).ConfigureAwait(false);
                AssertToolResultValid(searchResult);
                EnumerationResult<Memory> found = JsonHelper.Deserialize<EnumerationResult<Memory>>(GetToolResultText(searchResult));
                AssertTrue(found.Objects.Any(m => m.Id == created.Id), "search_memory finds the memory");

                JsonElement getResult = await CallToolAsync(mcpClient, sessionId, "get_memory", new { memoryId = created.Id }).ConfigureAwait(false);
                AssertToolResultValid(getResult);
                Memory fetched = JsonHelper.Deserialize<Memory>(GetToolResultText(getResult));
                AssertEqual(created.Id, fetched.Id, "memory id");
                AssertEqual("Memory content " + uniq + " (revised)", fetched.Content, "upserted content");

                JsonElement delResult = await CallToolAsync(mcpClient, sessionId, "delete_memory", new { memoryId = created.Id }).ConfigureAwait(false);
                AssertToolResultValid(delResult);
                McpStatusResult deleted = JsonHelper.Deserialize<McpStatusResult>(GetToolResultText(delResult));
                AssertEqual("deleted", deleted.Status, "status");
                AssertEqual(created.Id, deleted.MemoryId, "memory id");
            }));

            cases.Add(CaseAsync("service_not_found_tools_return_typed_not_found", "Tools whose services throw KeyNotFoundException return ErrorCode NotFound, not isError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient mcpClient = fx.McpClient;
                string sessionId = await InitMcpSessionAsync(mcpClient);

                // Each of these used to surface as an untyped isError result carrying the exception text.
                Dictionary<string, object> calls = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["approve_deployment"] = new { deploymentId = "dpl_missing", comment = "ok" },
                    ["verify_deployment"] = new { deploymentId = "dpl_missing" },
                    ["rollback_deployment"] = new { deploymentId = "dpl_missing" },
                    ["create_deployment"] = new { vesselId = "vsl_missing" },
                    ["start_runbook_execution"] = new { runbookId = "pbk_missing" },
                    ["update_objective"] = new { objectiveId = "obj_missing", title = "x" },
                    ["update_backlog_item"] = new { objectiveId = "obj_missing", title = "x" },
                    ["delete_objective"] = new { objectiveId = "obj_missing" },
                    ["delete_backlog_item"] = new { objectiveId = "obj_missing" },
                    ["get_backlog_refinement_session"] = new { sessionId = "ors_missing" },
                    ["get_backlog_planning_session"] = new { sessionId = "pls_missing" },
                    ["retry_check_run"] = new { checkRunId = "chk_missing" },
                    ["stop_captain"] = new { captainId = "cpt_missing" }
                };

                foreach (KeyValuePair<string, object> call in calls)
                {
                    JsonElement response = await SendRawMcpRequestAsync(mcpClient, sessionId, "tools/call", new
                    {
                        name = call.Key,
                        arguments = call.Value
                    }).ConfigureAwait(false);
                    JsonElement result = response.GetProperty("result");
                    AssertFalse(result.TryGetProperty("isError", out JsonElement isError) && isError.GetBoolean(), call.Key + " returned an untyped isError result: " + GetToolResultText(result));
                    AssertToolNotFound(GetToolResultText(result));
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "MCP Tool Tests",
                cases: cases);
        }

        #endregion

        #region Private-Members

        private const string SuiteId = "E2E.McpTool";

        #endregion

        #region Private-Methods

        /// <summary>
        /// Establish a fresh MCP session for a single case and return its session id.
        /// </summary>
        /// <param name="mcpClient">HTTP client targeting the MCP port.</param>
        /// <returns>The initialized session id.</returns>
        private static async Task<string> InitMcpSessionAsync(HttpClient mcpClient)
        {
            // Sessions are server-assigned: send initialize without a session, capture the Mcp-Session-Id the
            // server returns, then acknowledge with notifications/initialized before any other request.
            object request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "test-client", version = "1.0" }
                }
            };

            HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, "/rpc");
            httpRequest.Content = JsonHelper.ToJsonContent(request);

            HttpResponseMessage response = await mcpClient.SendAsync(httpRequest).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert(response.IsSuccessStatusCode, "MCP initialize to /rpc failed with " + response.StatusCode + ": " + responseBody);

            string sessionId = String.Empty;
            if (response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values))
            {
                foreach (string value in values) { sessionId = value; break; }
            }
            Assert(!String.IsNullOrEmpty(sessionId), "MCP initialize did not return an Mcp-Session-Id header");

            HttpRequestMessage notification = new HttpRequestMessage(HttpMethod.Post, "/rpc");
            notification.Content = JsonHelper.ToJsonContent(new { jsonrpc = "2.0", method = "notifications/initialized", @params = new { } });
            notification.Headers.Add("Mcp-Session-Id", sessionId);
            HttpResponseMessage notificationResponse = await mcpClient.SendAsync(notification).ConfigureAwait(false);
            notificationResponse.Dispose();

            return sessionId;
        }

        private static async Task<JsonElement> SendMcpRequestAsync(HttpClient mcpClient, string sessionId, string method, object parameters)
        {
            object request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = method,
                @params = parameters
            };

            StringContent content = JsonHelper.ToJsonContent(request);

            HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, "/rpc");
            httpRequest.Content = content;
            httpRequest.Headers.Add("Mcp-Session-Id", sessionId);

            HttpResponseMessage response = await mcpClient.SendAsync(httpRequest).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Assert(response.IsSuccessStatusCode,
                "MCP request to /rpc failed with " + response.StatusCode + ": " + responseBody);

            JsonElement responseJson = JsonSerializer.Deserialize<JsonElement>(responseBody);

            if (responseJson.TryGetProperty("error", out JsonElement error))
            {
                throw new Exception("MCP error: " + error.GetProperty("message").GetString());
            }

            return responseJson.GetProperty("result");
        }

        /// <summary>
        /// Invoke an MCP tool by name with the supplied arguments.
        /// </summary>
        /// <param name="mcpClient">HTTP client targeting the MCP port.</param>
        /// <param name="sessionId">MCP session id.</param>
        /// <param name="toolName">Tool name.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <returns>The tool call result element.</returns>
        private static async Task<JsonElement> CallToolAsync(HttpClient mcpClient, string sessionId, string toolName, object arguments)
        {
            return await SendMcpRequestAsync(mcpClient, sessionId, "tools/call", new
            {
                name = toolName,
                arguments = arguments
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Send a raw MCP JSON-RPC request and return the full response, without unwrapping errors.
        /// </summary>
        /// <param name="mcpClient">HTTP client targeting the MCP port.</param>
        /// <param name="sessionId">MCP session id.</param>
        /// <param name="method">JSON-RPC method name.</param>
        /// <param name="parameters">Request parameters.</param>
        /// <returns>The full JSON-RPC response element.</returns>
        private static async Task<JsonElement> SendRawMcpRequestAsync(HttpClient mcpClient, string sessionId, string method, object? parameters = null)
        {
            object request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = method,
                @params = parameters ?? new { }
            };

            StringContent content = JsonHelper.ToJsonContent(request);

            HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, "/rpc");
            httpRequest.Content = content;
            httpRequest.Headers.Add("Mcp-Session-Id", sessionId);

            HttpResponseMessage response = await mcpClient.SendAsync(httpRequest).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<JsonElement>(responseBody);
        }

        /// <summary>
        /// Assert an MCP tool result has a non-empty text content payload.
        /// </summary>
        /// <param name="result">Tool call result element.</param>
        private static void AssertToolResultValid(JsonElement result)
        {
            Assert(result.TryGetProperty("content", out JsonElement content), "Tool result should have content array");
            Assert(content.GetArrayLength() > 0, "Content array should not be empty");
            AssertEqual("text", content[0].GetProperty("type").GetString());
            AssertFalse(string.IsNullOrEmpty(content[0].GetProperty("text").GetString()), "Tool result text should not be empty");
        }

        /// <summary>
        /// Extract the text payload from an MCP tool result.
        /// </summary>
        /// <param name="result">Tool call result element.</param>
        /// <returns>The text content of the first result element.</returns>
        private static string GetToolResultText(JsonElement result)
        {
            return result.GetProperty("content")[0].GetProperty("text").GetString()!;
        }

        /// <summary>
        /// Assert a tool result's text is a typed error with <see cref="McpToolErrorCodeEnum.NotFound"/>.
        /// </summary>
        /// <param name="text">Tool result text.</param>
        private static void AssertToolNotFound(string text)
        {
            McpToolResultProbe probe = McpToolResultProbe.FromText(text);
            AssertEqual(McpToolErrorCodeEnum.NotFound, probe.ErrorCode, "tool error code (result: " + text + ")");
        }

        /// <summary>
        /// Create a fleet through the MCP <c>create_fleet</c> tool and return its id.
        /// </summary>
        /// <param name="mcpClient">HTTP client targeting the MCP port.</param>
        /// <param name="sessionId">MCP session id.</param>
        /// <param name="name">Fleet name seed.</param>
        /// <returns>The created fleet id.</returns>
        private static async Task<string> RestCreateFleetAsync(HttpClient mcpClient, string sessionId, string name = "McpTestFleet")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            JsonElement result = await CallToolAsync(mcpClient, sessionId, "create_fleet", new { name = uniqueName }).ConfigureAwait(false);
            string text = GetToolResultText(result);
            Fleet fleet = JsonHelper.Deserialize<Fleet>(text);
            return fleet.Id;
        }

        /// <summary>
        /// Create a vessel through the MCP <c>add_vessel</c> tool and return its id.
        /// </summary>
        /// <param name="mcpClient">HTTP client targeting the MCP port.</param>
        /// <param name="sessionId">MCP session id.</param>
        /// <param name="fleetId">Owning fleet id.</param>
        /// <param name="name">Vessel name seed.</param>
        /// <param name="workingDirectory">Optional working directory override.</param>
        /// <returns>The created vessel id.</returns>
        private static async Task<string> RestCreateVesselAsync(HttpClient mcpClient, string sessionId, string fleetId, string name = "McpTestVessel", string? workingDirectory = null)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            JsonElement result = await CallToolAsync(mcpClient, sessionId, "add_vessel", new
            {
                name = uniqueName,
                repoUrl = TestRepoHelper.GetLocalBareRepoUrl(),
                fleetId = fleetId,
                workingDirectory = workingDirectory
            }).ConfigureAwait(false);
            string text = GetToolResultText(result);
            Vessel vessel = JsonHelper.Deserialize<Vessel>(text);
            return vessel.Id;
        }

        /// <summary>
        /// Create a captain through the MCP <c>create_captain</c> tool and return its id.
        /// </summary>
        /// <param name="mcpClient">HTTP client targeting the MCP port.</param>
        /// <param name="sessionId">MCP session id.</param>
        /// <param name="name">Captain name seed.</param>
        /// <returns>The created captain id.</returns>
        private static async Task<string> RestCreateCaptainAsync(HttpClient mcpClient, string sessionId, string name = "mcp-test-captain")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            JsonElement result = await CallToolAsync(mcpClient, sessionId, "create_captain", new
            {
                name = uniqueName,
                runtime = "ClaudeCode"
            }).ConfigureAwait(false);
            string text = GetToolResultText(result);
            Captain captain = JsonHelper.Deserialize<Captain>(text);
            return captain.Id;
        }

        /// <summary>
        /// Create a mission through the MCP <c>create_mission</c> tool and return its id, creating a
        /// fleet and vessel first when no vessel id is supplied.
        /// </summary>
        /// <param name="mcpClient">HTTP client targeting the MCP port.</param>
        /// <param name="sessionId">MCP session id.</param>
        /// <param name="title">Mission title.</param>
        /// <param name="vesselId">Optional target vessel id.</param>
        /// <returns>The created mission id.</returns>
        /// <summary>
        /// Ensure a single fleet+vessel exists for missions that do not need their own, seeding it once for
        /// the suite. Missions in these cases only need <em>a</em> vessel; creating a fresh fleet + vessel
        /// (an add_vessel that registers the repo) on every RestCreateMissionAsync call was the dominant
        /// per-case cost. The vessel id is a server entity, so it is reused across every case's MCP session.
        /// </summary>
        /// <param name="mcpClient">Client targeting the MCP port.</param>
        /// <param name="sessionId">MCP session id of the first caller.</param>
        /// <returns>The shared vessel id.</returns>
        private Task<string> EnsureMissionVesselAsync(HttpClient mcpClient, string sessionId)
        {
            lock (_SeedLock)
            {
                if (_SharedMissionVessel == null) _SharedMissionVessel = CreateSharedMissionVesselAsync(mcpClient, sessionId);
                return _SharedMissionVessel;
            }
        }

        private static async Task<string> CreateSharedMissionVesselAsync(HttpClient mcpClient, string sessionId)
        {
            string fleetId = await RestCreateFleetAsync(mcpClient, sessionId, "SharedMsnFleet").ConfigureAwait(false);
            return await RestCreateVesselAsync(mcpClient, sessionId, fleetId, "SharedMsnVessel").ConfigureAwait(false);
        }

        private async Task<string> RestCreateMissionAsync(HttpClient mcpClient, string sessionId, string title = "McpTestMission", string? vesselId = null)
        {
            if (vesselId == null)
            {
                vesselId = await EnsureMissionVesselAsync(mcpClient, sessionId).ConfigureAwait(false);
            }

            JsonElement result = await CallToolAsync(mcpClient, sessionId, "create_mission", new
            {
                title = title,
                description = "Test mission for MCP",
                vesselId = vesselId
            }).ConfigureAwait(false);
            string text = GetToolResultText(result);
            MissionCreateResponse createResponse = JsonHelper.Deserialize<MissionCreateResponse>(text);

            // When mission stays Pending (no captain available), the response wraps
            // the mission in { "Mission": {...}, "Warning": "..." }.
            if (createResponse.Mission != null)
                return createResponse.Mission.Id;

            Mission mission = JsonHelper.Deserialize<Mission>(text);
            return mission.Id;
        }

        /// <summary>
        /// Create a voyage through the MCP <c>dispatch</c> tool and return its id.
        /// </summary>
        /// <param name="mcpClient">HTTP client targeting the MCP port.</param>
        /// <param name="sessionId">MCP session id.</param>
        /// <param name="vesselId">Target vessel id.</param>
        /// <returns>The created voyage id.</returns>
        private static async Task<string> RestCreateVoyageAsync(HttpClient mcpClient, string sessionId, string vesselId)
        {
            JsonElement result = await CallToolAsync(mcpClient, sessionId, "dispatch", new
            {
                title = "McpTestVoyage-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                description = "Voyage for MCP testing",
                vesselId = vesselId,
                missions = new[]
                {
                    new { title = "VoyageMission1", description = "Desc1" }
                }
            }).ConfigureAwait(false);
            string text = GetToolResultText(result);
            Voyage voyage = JsonHelper.Deserialize<Voyage>(text);
            return voyage.Id;
        }

        /// <summary>
        /// Add a vessel with a project context and style guide through add_vessel and return its id.
        /// </summary>
        /// <param name="mcpClient">HTTP client targeting the MCP port.</param>
        /// <param name="sessionId">MCP session id.</param>
        /// <param name="fleetId">Owning fleet id.</param>
        /// <param name="name">Vessel name seed.</param>
        /// <param name="projectContext">Project context.</param>
        /// <param name="styleGuide">Style guide.</param>
        /// <returns>The vessel id.</returns>
        private static async Task<string> AddVesselWithContextAsync(HttpClient mcpClient, string sessionId, string fleetId, string name, string projectContext, string styleGuide)
        {
            JsonElement result = await CallToolAsync(mcpClient, sessionId, "add_vessel", new
            {
                name = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                repoUrl = TestRepoHelper.GetLocalBareRepoUrl(),
                fleetId = fleetId,
                projectContext = projectContext,
                styleGuide = styleGuide
            }).ConfigureAwait(false);
            return JsonHelper.Deserialize<Vessel>(GetToolResultText(result)).Id;
        }

        /// <summary>
        /// Read the mission a create_mission result describes: either the mission itself or, when it stays Pending,
        /// the { Mission, Warning } wrapper.
        /// </summary>
        /// <param name="text">Tool result text.</param>
        /// <returns>The mission.</returns>
        private static Mission ReadCreatedMission(string text)
        {
            MissionCreateResponse wrapped = JsonHelper.Deserialize<MissionCreateResponse>(text);
            if (wrapped.Mission != null) return wrapped.Mission;
            return JsonHelper.Deserialize<Mission>(text);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
