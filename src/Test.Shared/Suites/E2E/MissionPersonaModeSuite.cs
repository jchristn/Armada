namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Runtimes.Mcp;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// RELIABILITY_SIMPLICITY_PLAN.md RS-4.2: a voyage request's missions carry <c>Persona</c> and <c>Mode</c> over REST,
    /// MCP, and the .NET SDK, and every surface creates the same missions from them. MCP advertised <c>persona</c> before
    /// this but dropped it on deserialization.
    /// </summary>
    public sealed class MissionPersonaModeSuite : IArmadaTestSuite
    {
        private const string SuiteId = "E2E.MissionPersonaMode";

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(SuiteId, "rest_mcp_sdk_persona_and_mode", "REST, MCP, and the SDK dispatch missions with the persona and mode each names, as single stages when no pipeline is given", async (CancellationToken ct) =>
            {
                using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
                await fx.StartAsync().ConfigureAwait(false);
                using HttpClient rest = fx.CreateRestClient(true);
                using HttpResponseMessage vesselCreated = await rest.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new { Name = "persona-mode", RepoUrl = "https://example.invalid/persona-mode", Active = false }), ct).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, vesselCreated);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(vesselCreated).ConfigureAwait(false);

                // REST: both missions name a persona, so the default pipeline is skipped and each is one stage.
                using HttpResponseMessage restCreated = await rest.PostAsync("/api/v1/voyages", JsonHelper.ToJsonContent(new
                {
                    Title = "rest",
                    VesselId = vessel.Id,
                    Missions = new object[]
                    {
                        new { Title = "audit", Description = "a", Persona = " Judge ", Mode = "Audit" },
                        new { Title = "research", Description = "r", Persona = "Worker", Mode = "Research" }
                    }
                }), ct).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, restCreated);
                Voyage restVoyage = await JsonHelper.DeserializeAsync<Voyage>(restCreated).ConfigureAwait(false);
                List<Mission> restMissions = await MissionsAsync(rest, restVoyage.Id, ct).ConfigureAwait(false);
                AssertEqual(2, restMissions.Count, "one stage per mission");
                Mission audit = restMissions.Single(m => m.Title == "audit");
                AssertEqual("Judge", audit.Persona);
                AssertEqual(MissionModeEnum.Audit, audit.Mode);
                Mission research = restMissions.Single(m => m.Title == "research");
                AssertEqual("Worker", research.Persona);
                AssertEqual(MissionModeEnum.Research, research.Mode);

                using HttpResponseMessage badMode = await rest.PostAsync("/api/v1/voyages", JsonHelper.ToJsonContent(new
                {
                    Title = "bad",
                    VesselId = vessel.Id,
                    Missions = new object[] { new { Title = "x", Description = "x", Mode = "Sideways" } }
                }), ct).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.BadRequest, badMode);

                // MCP: the same fields on the dispatch tool.
                using McpToolClient mcp = new McpToolClient(fx.McpUrl + "/mcp", null, new Dictionary<string, string> { ["X-Api-Key"] = fx.ApiKey });
                await mcp.InitializeAsync().ConfigureAwait(false);
                Armada.Runtimes.Mcp.McpToolCallResult dispatched = await mcp.CallToolResultAsync("dispatch", JsonHelper.Serialize(new
                {
                    title = "mcp",
                    vesselId = vessel.Id,
                    missions = new object[] { new { title = "mcp audit", description = "a", persona = "Judge", mode = "Audit" } }
                })).ConfigureAwait(false);
                AssertFalse(dispatched.IsError, dispatched.Text);
                Voyage mcpVoyage = JsonHelper.Deserialize<Voyage>(dispatched.Text);
                Mission mcpMission = (await MissionsAsync(rest, mcpVoyage.Id, ct).ConfigureAwait(false)).Single();
                AssertEqual("Judge", mcpMission.Persona, "MCP used to drop the persona");
                AssertEqual(MissionModeEnum.Audit, mcpMission.Mode);

                // SDK: DispatchRequest carries the fields.
                ArmadaClientOptions options = new ArmadaClientOptions(fx.BaseUrl);
                options.ApiKey = fx.ApiKey;
                using ArmadaClient client = new ArmadaClient(options);
                VoyageCreateRequest sdkRequest = new VoyageCreateRequest();
                sdkRequest.Title = "sdk";
                sdkRequest.VesselId = vessel.Id;
                sdkRequest.Missions = new List<DispatchRequest> { new DispatchRequest { Title = "sdk research", Description = "r", VesselId = vessel.Id, Persona = "Judge", Mode = MissionModeEnum.Research } };
                Voyage? sdkVoyage = await client.CreateVoyageAsync(sdkRequest, ct).ConfigureAwait(false);
                AssertNotNull(sdkVoyage, "sdk voyage");
                Mission sdkMission = (await MissionsAsync(rest, sdkVoyage!.Id, ct).ConfigureAwait(false)).Single();
                AssertEqual("Judge", sdkMission.Persona);
                AssertEqual(MissionModeEnum.Research, sdkMission.Mode);
            }, tags: new List<string> { TestTags.Positive }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Mission persona and mode on every dispatch surface", cases: cases);
        }

        private static async Task<List<Mission>> MissionsAsync(HttpClient rest, string voyageId, CancellationToken token)
        {
            using HttpResponseMessage response = await rest.GetAsync("/api/v1/missions?voyageId=" + voyageId + "&pageSize=100", token).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.OK, response);
            return (await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response).ConfigureAwait(false)).Objects;
        }
    }
}
