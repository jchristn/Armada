namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
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
