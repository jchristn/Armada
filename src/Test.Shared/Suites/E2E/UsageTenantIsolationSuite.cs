namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Server.Mcp;
    using Armada.Server.Mcp.Tools;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using Voltaic.Core;
    using Voltaic.Mcp;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Independent REST and MCP coverage for account tenant ownership and explicit admin scope.</summary>
    public sealed class UsageTenantIsolationSuite : IArmadaTestSuite
    {
        private const string Suite = "E2E.UsageTenantIsolation";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Suite, "Usage tenant isolation", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(Suite, "rest_roles_and_explicit_tenant", "REST reads isolate A/B even with identical account identities and require explicit global-admin scope", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    E2ETenantUser a = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-a").ConfigureAwait(false);
                    E2ETenantUser b = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-b").ConfigureAwait(false);
                    E2ETenantUser regular = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-regular", false, a.TenantId).ConfigureAwait(false);
                    using DatabaseDriver db = await UsageTestData.OpenAsync(fx).ConfigureAwait(false);
                    Captain captain = await db.Captains.CreateAsync(new Captain { TenantId = a.TenantId, Name = "private-tenant-a" }).ConfigureAwait(false);
                    DateTime now = DateTime.UtcNow;
                    UsageSnapshot sa = await UsageTestData.SnapshotAsync(db, a.TenantId, now, "same-account", new List<string> { captain.Id }).ConfigureAwait(false);
                    UsageSnapshot sb = await UsageTestData.SnapshotAsync(db, b.TenantId, now, "same-account", new List<string>()).ConfigureAwait(false);
                    await db.UsageSnapshots.UpsertLatestStateAsync(new UsageLatestState { TenantId = a.TenantId, HostId = "host", AccountKey = "same-account", Provider = "anthropic", LastGoodSnapshotId = sa.Id, LastGoodCapturedUtc = now, LastGoodWindowsJson = sa.WindowsJson }).ConfigureAwait(false);
                    await db.UsageSnapshots.UpsertLatestStateAsync(new UsageLatestState { TenantId = b.TenantId, HostId = "host", AccountKey = "same-account", Provider = "anthropic", LastGoodSnapshotId = sb.Id, LastGoodCapturedUtc = now, LastGoodWindowsJson = sb.WindowsJson }).ConfigureAwait(false);
                    using HttpClient ac = a.CreateClient(fx.BaseUrl);
                    using HttpClient bc = b.CreateClient(fx.BaseUrl);
                    using HttpClient rc = regular.CreateClient(fx.BaseUrl);
                    foreach (string route in new[] { "snapshots", "latest" })
                    {
                        string path = "/api/v1/usage/" + route + "?accountKey=same-account";
                        AssertEqual(sa.Id, (await UsageTestData.GetAsync<EnumerationResult<UsageSnapshotView>>(ac, path).ConfigureAwait(false)).Objects.Single().Id);
                        AssertEqual(sb.Id, (await UsageTestData.GetAsync<EnumerationResult<UsageSnapshotView>>(bc, path).ConfigureAwait(false)).Objects.Single().Id);
                        AssertEqual(sa.Id, (await UsageTestData.GetAsync<EnumerationResult<UsageSnapshotView>>(rc, path).ConfigureAwait(false)).Objects.Single().Id);
                        EnumerationResult<UsageSnapshotView> implicitAdmin = await UsageTestData.GetAsync<EnumerationResult<UsageSnapshotView>>(fx.AuthClient, path).ConfigureAwait(false);
                        AssertEqual(0L, implicitAdmin.TotalRecords, "global admin defaults to its own tenant");
                        AssertEqual(sa.Id, (await UsageTestData.GetAsync<EnumerationResult<UsageSnapshotView>>(fx.AuthClient, path + "&tenantId=" + a.TenantId).ConfigureAwait(false)).Objects.Single().Id);
                        await UsageTestData.StatusAsync(bc, path + "&tenantId=" + a.TenantId, HttpStatusCode.Forbidden).ConfigureAwait(false);
                        await UsageTestData.StatusAsync(bc, path + "&captainId=" + captain.Id, HttpStatusCode.NotFound).ConfigureAwait(false);
                    }
                    using (HttpResponseMessage response = await rc.GetAsync("/api/v1/usage/snapshots").ConfigureAwait(false))
                        AssertFalse((await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Contains("tenant-admin-only-diagnostic", StringComparison.Ordinal), "ordinary readers never receive excerpts");
                    using (HttpResponseMessage response = await bc.PostAsync("/api/v1/captains/" + captain.Id + "/usage/capture", new StringContent("invalid-json")).ConfigureAwait(false))
                        AssertEqual(HttpStatusCode.NotFound, response.StatusCode, "foreign visibility is resolved before secondary body validation");
                    using (HttpResponseMessage response = await rc.PostAsync("/api/v1/captains/" + captain.Id + "/usage/capture", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false))
                        AssertEqual(HttpStatusCode.Forbidden, response.StatusCode, "regular users cannot launch captures");
                    using (HttpResponseMessage response = await ac.PostAsync("/api/v1/captains/" + captain.Id + "/usage/capture", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false))
                        AssertEqual(HttpStatusCode.Conflict, response.StatusCode, "disabled capture has a precise conflict");
                }),
                new TestCaseDescriptor(Suite, "mcp_missing_argument_is_protocol_error", "Required missing MCP captainId is a protocol error", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    using HttpClient admin = new HttpClient { BaseAddress = fx.McpClient.BaseAddress };
                    admin.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
                    string missing = await McpTestCalls.CallToolAsync(admin, "capture_captain_usage", new { }).ConfigureAwait(false);
                    AssertContains("-32602", missing, "missing required argument is JSON-RPC invalid params");
                    McpRpcEnvelope envelope = JsonHelper.Deserialize<McpRpcEnvelope>(missing);
                    AssertNotNull(envelope.Error, "protocol error object: " + missing);
                    AssertEqual(-32602, envelope.Error!.Code, "error.code: " + missing);
                    AssertNull(envelope.Result, "a protocol error carries no tool result: " + missing);
                    AssertContains("captainId", envelope.Error.Message ?? String.Empty, "error names the missing argument");
                    AssertFalse(missing.Contains("\"isError\"", StringComparison.Ordinal), "not a tool execution error: " + missing);
                    AssertContains("\"data\":{\"tool\":\"capture_captain_usage\",\"argument\":\"captainId\"}", missing, "error.data names the tool and argument");

                    string nullArguments = await McpTestCalls.CallToolAsync(admin, "capture_captain_usage", null!).ConfigureAwait(false);
                    AssertEqual(-32602, JsonHelper.Deserialize<McpRpcEnvelope>(nullArguments).Error?.Code ?? 0, "absent arguments object is also invalid params: " + nullArguments);
                    string onlyHarbor = await McpTestCalls.CallToolAsync(admin, "capture_captain_usage", new { harborId = "hbr_only" }).ConfigureAwait(false);
                    AssertEqual(-32602, JsonHelper.Deserialize<McpRpcEnvelope>(onlyHarbor).Error?.Code ?? 0, "optional arguments do not satisfy required captainId: " + onlyHarbor);
                }),
                new TestCaseDescriptor(Suite, "mcp_required_guard_keeps_default_dispatch", "Present captainId reaches the handler; type errors and other tools keep the library's tool-result validation", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    using HttpClient admin = new HttpClient { BaseAddress = fx.McpClient.BaseAddress };
                    admin.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
                    string unknown = await McpTestCalls.CallToolAsync(admin, "capture_captain_usage", new { captainId = "cpt_usage_guard_missing" }).ConfigureAwait(false);
                    AssertNull(JsonHelper.Deserialize<McpRpcEnvelope>(unknown).Error, "a present captainId is not a protocol error: " + unknown);
                    AssertContains("NotFound", unknown, "the handler ran and resolved the captain");
                    string wrongType = await McpTestCalls.CallToolAsync(admin, "capture_captain_usage", new { captainId = 42 }).ConfigureAwait(false);
                    AssertNull(JsonHelper.Deserialize<McpRpcEnvelope>(wrongType).Error, "type validation stays a tool result: " + wrongType);
                    AssertContains("\"isError\":true", wrongType, "type validation is an isError result");
                    string otherTool = await McpTestCalls.CallToolAsync(admin, "get_captain", new { }).ConfigureAwait(false);
                    AssertNull(JsonHelper.Deserialize<McpRpcEnvelope>(otherTool).Error, "tools outside the usage guard keep the default form: " + otherTool);
                    AssertContains("\"isError\":true", otherTool, "other tools still answer missing required arguments with isError");
                    string listed = await McpTestCalls.CallToolAsync(admin, "usage_status", new { }).ConfigureAwait(false);
                    AssertNull(JsonHelper.Deserialize<McpRpcEnvelope>(listed).Error, "usage_status has no required argument: " + listed);
                }),
                new TestCaseDescriptor(Suite, "mcp_stateless_required_argument_and_recovery", "Stateless MCP rejects omitted and mis-cased required arguments and still dispatches subsequent reads", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    using HttpClient admin = new HttpClient { BaseAddress = fx.McpClient.BaseAddress };
                    admin.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
                    admin.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2026-07-28");
                    admin.DefaultRequestHeaders.Add("Mcp-Method", "tools/call");
                    admin.DefaultRequestHeaders.Add("Mcp-Name", "capture_captain_usage");
                    string[] parameters = new[]
                    {
                        "{\"name\":\"capture_captain_usage\"}",
                        "{\"name\":\"capture_captain_usage\",\"arguments\":null}",
                        "{\"name\":\"capture_captain_usage\",\"arguments\":{}}",
                        "{\"name\":\"capture_captain_usage\",\"arguments\":{\"CaptainId\":\"cpt_wrong_case\"}}"
                    };
                    foreach (string arguments in parameters)
                    {
                        string statelessArguments = arguments.Insert(1, "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{}},");
                        using StringContent content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":37,\"method\":\"tools/call\",\"params\":" + statelessArguments + "}", System.Text.Encoding.UTF8, "application/json");
                        using HttpResponseMessage response = await admin.PostAsync("/mcp", content, ct).ConfigureAwait(false);
                        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        AssertTrue(response.IsSuccessStatusCode, "protocol error is an MCP response: " + body);
                        McpRpcEnvelope envelope = JsonHelper.Deserialize<McpRpcEnvelope>(body);
                        AssertEqual(-32602, envelope.Error?.Code ?? 0, "stateless missing argument: " + body);
                        AssertNull(envelope.Result, "protocol errors carry no result: " + body);
                        AssertContains("\"id\":37", body, "request correlation survives guard dispatch");
                        AssertContains("captainId", envelope.Error?.Message ?? String.Empty);
                    }
                    admin.DefaultRequestHeaders.Remove("Mcp-Name");
                    admin.DefaultRequestHeaders.Add("Mcp-Name", "usage_status");
                    using StringContent valid = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":38,\"method\":\"tools/call\",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{}},\"name\":\"usage_status\",\"arguments\":{}}}", System.Text.Encoding.UTF8, "application/json");
                    using HttpResponseMessage recovered = await admin.PostAsync("/mcp", valid, ct).ConfigureAwait(false);
                    string recoveredBody = await recovered.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    AssertTrue(recovered.IsSuccessStatusCode, recoveredBody);
                    McpRpcEnvelope recoveredEnvelope = JsonHelper.Deserialize<McpRpcEnvelope>(recoveredBody);
                    AssertNull(recoveredEnvelope.Error, "valid calls recover after invalid params: " + recoveredBody);
                    AssertNotNull(recoveredEnvelope.Result, "recovery must execute the original dispatcher");
                    AssertFalse(recoveredBody.Contains("\"isError\":true", StringComparison.Ordinal), recoveredBody);
                }),
                new TestCaseDescriptor(Suite, "mcp_required_guard_unit_contract", "Guard installs on the pinned MCP library and only flags absent declared arguments", ct =>
                {
                    IReadOnlyDictionary<string, IReadOnlyList<string>> map = McpUsageTools.ProtocolRequiredArguments;
                    AssertEqual("captainId", map["capture_captain_usage"].Single(), "guard list mirrors the capture schema");
                    AssertFalse(map.ContainsKey("usage_status"), "usage_status has no required argument");
                    string tool;
                    AssertEqual("captainId", McpRequiredArgumentGuard.FindMissing(new RpcParameters("{\"name\":\"capture_captain_usage\",\"arguments\":{}}"), map, out tool));
                    AssertEqual("capture_captain_usage", tool);
                    AssertEqual("captainId", McpRequiredArgumentGuard.FindMissing(new RpcParameters("{\"name\":\"capture_captain_usage\"}"), map, out tool), "absent arguments object");
                    AssertNull(McpRequiredArgumentGuard.FindMissing(new RpcParameters("{\"name\":\"capture_captain_usage\",\"arguments\":{\"captainId\":\"cpt_x\"}}"), map, out tool), "present argument");
                    AssertEqual("captainId", McpRequiredArgumentGuard.FindMissing(new RpcParameters("{\"name\":\"capture_captain_usage\",\"arguments\":{\"CaptainId\":\"cpt_x\"}}"), map, out tool), "required names are case-sensitive, as in JSON Schema");
                    AssertNull(McpRequiredArgumentGuard.FindMissing(new RpcParameters("{\"name\":\"get_captain\",\"arguments\":{}}"), map, out tool), "tools outside the map are untouched");
                    AssertNull(McpRequiredArgumentGuard.FindMissing(new RpcParameters("{\"name\":\"capture_captain_usage\",\"arguments\":[1]}"), map, out tool), "non-object arguments are left to library validation");
                    AssertNull(McpRequiredArgumentGuard.FindMissing(null, map, out tool), "null params");
                    McpHttpServer server = new McpHttpServer("127.0.0.1", 1);
                    AssertTrue(McpRequiredArgumentGuard.Install(server, map), "the pinned MCP library's tools/call dispatcher is captured; a dependency upgrade that hides it must fail here");
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(Suite, "mcp_tenant_scope", "MCP shares tenant isolation with REST", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    E2ETenantUser a = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-mcp-a").ConfigureAwait(false);
                    E2ETenantUser b = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-mcp-b").ConfigureAwait(false);
                    using DatabaseDriver db = await UsageTestData.OpenAsync(fx).ConfigureAwait(false);
                    Captain captain = await db.Captains.CreateAsync(new Captain { TenantId = a.TenantId, Name = "usage-mcp-captain" }).ConfigureAwait(false);
                    UsageSnapshot snapshot = await UsageTestData.SnapshotAsync(db, a.TenantId, DateTime.UtcNow, "mcp-account", new List<string> { captain.Id }).ConfigureAwait(false);
                    using HttpClient ac = a.CreateClient(fx.McpClient.BaseAddress!.ToString());
                    using HttpClient bc = b.CreateClient(fx.McpClient.BaseAddress!.ToString());
                    using HttpClient admin = new HttpClient { BaseAddress = fx.McpClient.BaseAddress };
                    admin.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
                    string own = await McpTestCalls.CallToolAsync(ac, "usage_status", new { captainId = captain.Id, runtime = "ClaudeCode", from = DateTime.UtcNow.AddHours(-1).ToString("O"), to = DateTime.UtcNow.AddHours(1).ToString("O") }).ConfigureAwait(false);
                    AssertContains(snapshot.Id, own, "known own request succeeds, proving MCP auth and tool execution");
                    AssertFalse(own.Contains("tenant-admin-only-diagnostic", StringComparison.Ordinal));
                    string foreign = await McpTestCalls.CallToolAsync(bc, "usage_status", new { captainId = captain.Id }).ConfigureAwait(false);
                    AssertContains("NotFound", foreign); AssertFalse(foreign.Contains(snapshot.Id, StringComparison.Ordinal));
                    string foreignCapture = await McpTestCalls.CallToolAsync(bc, "capture_captain_usage", new { captainId = captain.Id }).ConfigureAwait(false);
                    AssertContains("NotFound", foreignCapture);
                    string spoofed = await McpTestCalls.CallToolAsync(bc, "usage_status", new { tenantId = a.TenantId }).ConfigureAwait(false);
                    AssertContains("Forbidden", spoofed);
                    string explicitAdmin = await McpTestCalls.CallToolAsync(admin, "usage_status", new { tenantId = a.TenantId, from = DateTime.UtcNow.AddHours(-1).ToString("O"), to = DateTime.UtcNow.AddHours(1).ToString("O") }).ConfigureAwait(false);
                    AssertContains(snapshot.Id, explicitAdmin);
                })
            });
        }
    }
}
