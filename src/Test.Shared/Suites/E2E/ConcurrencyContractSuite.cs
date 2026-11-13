namespace Test.Shared.Suites.E2E
{
    using System.Net;
    using System.Text.Json;
    using Armada.Core.Models;
    using Armada.Runtimes.Mcp;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Exercise the explicitly approved v1.3 ownership contract over real REST/MCP transports.</summary>
    public sealed class ConcurrencyContractSuite : IArmadaTestSuite
    {
        public TestSuiteDescriptor Build()
        {
            const string suite = "E2E.ConcurrencyContract";
            return new(suite, "Final concurrency contract", new List<TestCaseDescriptor>
            {
                new(suite, "removed_vessel_field", "REST and MCP reject the removed vessel boolean with migration guidance", async ct =>
                {
                    using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null);
                    await fx.StartAsync();
                    using HttpClient rest = fx.CreateRestClient(true);
                    using HttpResponseMessage created = await rest.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new { Name="contract", RepoUrl="https://example.invalid/repo", Active=false }), ct);
                    AssertStatusCode(HttpStatusCode.Created, created);
                    Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(created);
                    foreach (HttpMethod method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch })
                    {
                        using HttpRequestMessage request = new(method, "/api/v1/vessels" + (method==HttpMethod.Post ? "" : "/"+vessel.Id+(method==HttpMethod.Patch ? "/context" : "")));
                        request.Content = JsonHelper.ToJsonContent(new { Name="old input", RepoUrl="https://example.invalid/repo", allowConcurrentMissions=true });
                        using HttpResponseMessage rejected = await rest.SendAsync(request, ct);
                        AssertStatusCode(HttpStatusCode.BadRequest, rejected);
                        AssertContains("MaxParallelVoyages", await rejected.Content.ReadAsStringAsync(ct));
                    }
                    using McpToolClient mcp = new(fx.McpUrl+"/mcp", null, new Dictionary<string,string> { ["X-Api-Key"]=fx.ApiKey });
                    await mcp.InitializeAsync();
                    foreach (string tool in new[] { "add_vessel", "update_vessel" })
                    {
                        var result = await mcp.CallToolResultAsync(tool, JsonHelper.Serialize(new { fleetId="flt_fixture", vesselId=vessel.Id, name="old input", repoUrl="https://example.invalid/repo", allowConcurrentMissions=true }));
                        AssertContains("MaxParallelVoyages", result.Text);
                    }
                }),
                new(suite, "mission_metadata_is_server_owned", "Mission creation cannot forge hierarchy provenance or assignment timestamps", async ct =>
                {
                    using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null);
                    await fx.StartAsync();
                    using HttpClient rest = fx.CreateRestClient(true);
                    using HttpResponseMessage created = await rest.PostAsync("/api/v1/missions", JsonHelper.ToJsonContent(new { Title="server metadata", RescueOfMissionId="forged", BaseBranchName="forged", Depth=99, ReadyUtc=DateTime.UtcNow, AssignedUtc=DateTime.UtcNow, OwnedPathsJson="[\"forged\"]" }), ct);
                    AssertStatusCode(HttpStatusCode.Created, created);
                    using JsonDocument json = JsonDocument.Parse(await created.Content.ReadAsStringAsync(ct));
                    JsonElement mission = json.RootElement.TryGetProperty("Mission", out JsonElement wrapped) ? wrapped : json.RootElement;
                    AssertEqual(0, mission.GetProperty("Depth").GetInt32());
                    foreach (string field in new[] { "RescueOfMissionId", "BaseBranchName", "ReadyUtc", "AssignedUtc", "OwnedPathsJson" })
                        AssertTrue(!mission.TryGetProperty(field, out JsonElement value) || value.ValueKind==JsonValueKind.Null, field+" is server owned");
                }),
                new(suite, "tree_waits_and_concurrency_endpoints", "Voyage tree, mission waits, vessel concurrency, metrics, the hierarchy filters and the Concurrency settings group over REST and MCP", async ct =>
                {
                    using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null);
                    await fx.StartAsync();
                    using HttpClient rest = fx.CreateRestClient(true);
                    using HttpResponseMessage vesselCreated = await rest.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new { Name="tree", RepoUrl="https://example.invalid/tree", MaxParallelVoyages=2 }), ct);
                    AssertStatusCode(HttpStatusCode.Created, vesselCreated);
                    Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(vesselCreated);

                    using HttpResponseMessage badKey = await rest.PostAsync("/api/v1/voyages", JsonHelper.ToJsonContent(new { Title="bad", VesselId=vessel.Id, Missions=new object[] { new { Title="orphan", Description="x", ParentKey="nope" } } }), ct);
                    AssertStatusCode(HttpStatusCode.BadRequest, badKey);
                    using HttpResponseMessage voyageCreated = await rest.PostAsync("/api/v1/voyages", JsonHelper.ToJsonContent(new
                    {
                        Title="tree", VesselId=vessel.Id,
                        Missions=new object[] { new { Title="parent", Description="p", Key="p" }, new { Title="child", Description="c", ParentKey="p" }, new { Title="other", Description="o" } }
                    }), ct);
                    AssertStatusCode(HttpStatusCode.Created, voyageCreated);
                    Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(voyageCreated);

                    using HttpResponseMessage treeResponse = await rest.GetAsync("/api/v1/voyages/" + voyage.Id + "/tree", ct);
                    AssertStatusCode(HttpStatusCode.OK, treeResponse);
                    VoyageTree tree = await JsonHelper.DeserializeAsync<VoyageTree>(treeResponse);
                    AssertEqual(3, tree.TotalMissions); AssertEqual(1, tree.MaxDepth);
                    MissionTreeNode parent = tree.Roots.Single(n => n.Mission.Title == "parent");
                    MissionSummary child = parent.Children.Single().Mission;
                    AssertEqual("child", child.Title);
                    using HttpResponseMessage missingTree = await rest.GetAsync("/api/v1/voyages/vyg_missing/tree", ct);
                    AssertStatusCode(HttpStatusCode.NotFound, missingTree);

                    using HttpResponseMessage roots = await rest.GetAsync("/api/v1/missions?voyageId=" + voyage.Id + "&rootsOnly=true", ct);
                    EnumerationResult<Mission> rootPage = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(roots);
                    AssertEqual(2L, (long)rootPage.TotalRecords, "rootsOnly leaves out the child");
                    using HttpResponseMessage children = await rest.GetAsync("/api/v1/missions?parentMissionId=" + parent.Mission.Id, ct);
                    AssertEqual(child.Id, (await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(children)).Objects.Single().Id);

                    using HttpResponseMessage waits = await rest.GetAsync("/api/v1/missions/" + child.Id + "/waits", ct);
                    AssertStatusCode(HttpStatusCode.OK, waits);
                    EnumerationResult<MissionWaitInterval> waitPage = await JsonHelper.DeserializeAsync<EnumerationResult<MissionWaitInterval>>(waits);
                    AssertEqual(100, waitPage.PageSize, "waits default to 100 per page");
                    using HttpResponseMessage concurrency = await rest.GetAsync("/api/v1/vessels/" + vessel.Id + "/concurrency", ct);
                    AssertStatusCode(HttpStatusCode.OK, concurrency);
                    VesselConcurrency report = await JsonHelper.DeserializeAsync<VesselConcurrency>(concurrency);
                    AssertEqual(vessel.Id, report.VesselId); AssertEqual(2, report.MaxParallelVoyages);
                    using HttpResponseMessage metrics = await rest.GetAsync("/api/v1/metrics/concurrency?vesselId=" + vessel.Id, ct);
                    AssertStatusCode(HttpStatusCode.OK, metrics);
                    AssertEqual(vessel.Id, (await JsonHelper.DeserializeAsync<ConcurrencyMetrics>(metrics)).VesselId);
                    using HttpResponseMessage captainCreated = await rest.PostAsync("/api/v1/captains", JsonHelper.ToJsonContent(new { Name="wide", Runtime="ClaudeCode", MaxConcurrency=1, Active=false }), ct);
                    AssertStatusCode(HttpStatusCode.Created, captainCreated);
                    Captain captain = await JsonHelper.DeserializeAsync<Captain>(captainCreated);
                    using HttpResponseMessage byCaptain = await rest.GetAsync("/api/v1/metrics/concurrency?captainId=" + captain.Id, ct);
                    AssertStatusCode(HttpStatusCode.OK, byCaptain);
                    AssertEqual(captain.Id, (await JsonHelper.DeserializeAsync<ConcurrencyMetrics>(byCaptain)).CaptainId);
                    using HttpResponseMessage unknownCaptain = await rest.GetAsync("/api/v1/metrics/concurrency?captainId=cpt_missing", ct);
                    AssertStatusCode(HttpStatusCode.NotFound, unknownCaptain);
                    using HttpResponseMessage serial = await rest.GetAsync("/api/v1/captains?maxConcurrency=" + captain.MaxConcurrency, ct);
                    AssertTrue((await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(serial)).Objects.Any(c => c.Id == captain.Id), "maxConcurrency matches the captain");
                    using HttpResponseMessage none = await rest.GetAsync("/api/v1/captains?maxConcurrency=" + (captain.MaxConcurrency + 7), ct);
                    AssertFalse((await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(none)).Objects.Any(c => c.Id == captain.Id), "a different slot count excludes it");
                    using HttpResponseMessage badTime = await rest.GetAsync("/api/v1/metrics/concurrency?from=yesterday", ct);
                    AssertStatusCode(HttpStatusCode.BadRequest, badTime);
                    using HttpResponseMessage inverted = await rest.GetAsync("/api/v1/metrics/concurrency?from=2026-01-02T00:00:00Z&to=2026-01-01T00:00:00Z", ct);
                    AssertStatusCode(HttpStatusCode.BadRequest, inverted);

                    int retention = fx.Settings.Concurrency.WaitIntervalRetentionDays;
                    using HttpResponseMessage patched = await rest.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { Concurrency=new { StarvationWarningMinutes=77, MaxHierarchyDepth=999 }, ExpectedConcurrencyRevision=0 }), ct);
                    AssertStatusCode(HttpStatusCode.OK, patched);
                    ConcurrencySettingsEnvelope applied = await JsonHelper.DeserializeAsync<ConcurrencySettingsEnvelope>(patched);
                    AssertEqual(77, applied.Concurrency.StarvationWarningMinutes);
                    AssertEqual(16, applied.Concurrency.MaxHierarchyDepth, "clamped to the ceiling");
                    AssertEqual(retention, applied.Concurrency.WaitIntervalRetentionDays, "an omitted member keeps its value");
                    AssertEqual(1L, applied.ConcurrencyRevision);
                    AssertEqual(77, fx.Settings.Concurrency.StarvationWarningMinutes, "applied live");
                    using HttpResponseMessage stale = await rest.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { Concurrency=new { StarvationWarningMinutes=5 }, ExpectedConcurrencyRevision=0 }), ct);
                    AssertStatusCode(HttpStatusCode.Conflict, stale);
                    AssertContains("StaleConcurrencyRevision", await stale.Content.ReadAsStringAsync(ct));
                    AssertEqual(77, fx.Settings.Concurrency.StarvationWarningMinutes, "a stale update changes nothing");

                    using McpToolClient mcp = new(fx.McpUrl+"/mcp", null, new Dictionary<string,string> { ["X-Api-Key"]=fx.ApiKey });
                    await mcp.InitializeAsync();
                    Armada.Runtimes.Mcp.McpToolCallResult mcpTree = await mcp.CallToolResultAsync("voyage_tree", JsonHelper.Serialize(new { voyageId=voyage.Id }));
                    AssertFalse(mcpTree.IsError, mcpTree.Text);
                    AssertEqual(3, JsonHelper.Deserialize<VoyageTree>(mcpTree.Text).TotalMissions);
                    Armada.Runtimes.Mcp.McpToolCallResult mcpVessel = await mcp.CallToolResultAsync("vessel_concurrency", JsonHelper.Serialize(new { vesselId=vessel.Id }));
                    AssertEqual(vessel.Id, JsonHelper.Deserialize<VesselConcurrency>(mcpVessel.Text).VesselId);
                    Armada.Runtimes.Mcp.McpToolCallResult mcpStatus = await mcp.CallToolResultAsync("mission_status", JsonHelper.Serialize(new { missionId=child.Id, includeWaits=true }));
                    AssertNotNull(JsonHelper.Deserialize<Mission>(mcpStatus.Text).Waits, "includeWaits returns Waits");
                    Armada.Runtimes.Mcp.McpToolCallResult mcpPlain = await mcp.CallToolResultAsync("mission_status", JsonHelper.Serialize(new { missionId=child.Id }));
                    AssertNull(JsonHelper.Deserialize<Mission>(mcpPlain.Text).Waits, "Waits is omitted by default");
                    Armada.Runtimes.Mcp.McpToolCallResult mcpChild = await mcp.CallToolResultAsync("create_mission", JsonHelper.Serialize(new { title="grandchild", description="g", vesselId=vessel.Id, voyageId=voyage.Id, parentMissionId=child.Id, branchStrategy="ForkFromParent" }));
                    AssertFalse(mcpChild.IsError, mcpChild.Text);
                    Armada.Runtimes.Mcp.McpToolCallResult badStrategy = await mcp.CallToolResultAsync("create_mission", JsonHelper.Serialize(new { title="bad", description="b", vesselId=vessel.Id, voyageId=voyage.Id, parentMissionId=child.Id, branchStrategy="Sideways" }));
                    AssertContains("branchStrategy must be", badStrategy.Text);
                    using HttpResponseMessage deeper = await rest.GetAsync("/api/v1/voyages/" + voyage.Id + "/tree", ct);
                    AssertEqual(2, (await JsonHelper.DeserializeAsync<VoyageTree>(deeper)).MaxDepth, "the MCP child sits under the REST child");
                }),
                new(suite, "captain_ownership_json", "Captain JSON exposes execution IDs and session ownership without representative aliases", async ct =>
                {
                    using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null);
                    await fx.StartAsync();
                    using HttpClient rest = fx.CreateRestClient(true);
                    using HttpResponseMessage created = await rest.PostAsync("/api/v1/captains", JsonHelper.ToJsonContent(new { Name="contract", Runtime="ClaudeCode", Active=false }), ct);
                    AssertStatusCode(HttpStatusCode.Created, created);
                    using JsonDocument json = JsonDocument.Parse(await created.Content.ReadAsStringAsync(ct));
                    AssertTrue(json.RootElement.TryGetProperty("ActiveMissionIds", out _));
                    AssertTrue(json.RootElement.TryGetProperty("ActiveMissionCount", out _));
                    AssertFalse(json.RootElement.TryGetProperty("CurrentMissionId", out _));
                    AssertFalse(json.RootElement.TryGetProperty("CurrentDockId", out _));
                    AssertFalse(json.RootElement.TryGetProperty("Assignment", out _));
                })
            });
        }
    }
}
