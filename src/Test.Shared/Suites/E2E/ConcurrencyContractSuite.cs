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
