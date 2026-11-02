namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Runtimes.Mcp;
    using System.Text.Json;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Live HTTP, SQLite, git worktree and process coverage for vessel concurrency.</summary>
    public sealed class ConcurrencySuite : IArmadaTestSuite
    {
        /// <summary>Build a model-free end-to-end scenario using real owned agent processes.</summary>
        public TestSuiteDescriptor Build()
        {
            const string suite = "E2E.Concurrency";
            return new TestSuiteDescriptor(suite, "Concurrency end to end", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(suite, "vessel_admission_lifecycle", "HTTP limits control real worktree/process admission and freed slots refill", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                    HttpClient client = fx.AuthClient;
                    string suffix = Guid.NewGuid().ToString("N")[..8];
                    List<string> voyageIds = new List<string>();
                    try
                    {
                        for (int i = 0; i < 4; i++)
                            await Post<Captain>(client, "/api/v1/captains", new { Name = "concurrency-" + suffix + "-" + i });
                        Vessel vessel = await Post<Vessel>(client, "/api/v1/vessels", new
                        {
                            Name = "concurrency-" + suffix, RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(),
                            MaxParallelVoyages = 1, MaxParallelMissionsPerVoyage = 2
                        });
                        AssertEqual(1, vessel.MaxParallelVoyages);
                        AssertEqual(2, vessel.MaxParallelMissionsPerVoyage);
                        Vessel reread = await Get<Vessel>(client, "/api/v1/vessels/" + vessel.Id);
                        AssertEqual(2, reread.MaxParallelMissionsPerVoyage, "limits survive database persistence");
                        using McpToolClient mcp = new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", null,
                            new Dictionary<string, string> { ["X-Api-Key"] = fx.ApiKey });
                        await mcp.InitializeAsync();
                        string mcpResult = await mcp.CallToolAsync("update_vessel", JsonSerializer.Serialize(new
                        {
                            vesselId = vessel.Id, maxParallelVoyages = 1, maxParallelMissionsPerVoyage = 2
                        }));
                        AssertContains(vessel.Id, mcpResult, "MCP accepts the new numeric inputs");
                        AssertEqual(2, (await Get<Vessel>(client, "/api/v1/vessels/" + vessel.Id)).MaxParallelMissionsPerVoyage);
                        Voyage first = await Post<Voyage>(client, "/api/v1/voyages", new
                        {
                            Title = "Parallel voyage " + suffix, VesselId = vessel.Id,
                            Missions = Enumerable.Range(0, 3).Select(i => new { Title = "Task " + i, Description = "Concurrency smoke test; no provider calls" }).ToArray()
                        });
                        voyageIds.Add(first.Id);
                        EnumerationResult<Mission> firstMissions = await Get<EnumerationResult<Mission>>(client, "/api/v1/missions?voyageId=" + first.Id);
                        AssertEqual(2, firstMissions.Objects.Count(m => m.Status == MissionStatusEnum.InProgress), "two actual agent processes run concurrently");
                        Mission waiting = firstMissions.Objects.Single(m => m.Status == MissionStatusEnum.Pending);
                        waiting = await Get<Mission>(client, "/api/v1/missions/" + waiting.Id);
                        AssertEqual(MissionAssignmentBlockerReasonEnum.VoyageMissionLimit, waiting.AssignmentBlocker!.Reason);
                        AssertEqual(2, waiting.AssignmentBlocker.BlockingMissionCount);
                        Mission running = firstMissions.Objects.First(m => m.Status == MissionStatusEnum.InProgress);
                        using (HttpResponseMessage cancelled = await client.DeleteAsync("/api/v1/missions/" + running.Id, ct))
                            cancelled.EnsureSuccessStatusCode();
                        await fx.Server.Admiral.HealthCheckAsync(ct);
                        waiting = await Get<Mission>(client, "/api/v1/missions/" + waiting.Id);
                        AssertEqual(MissionStatusEnum.InProgress, waiting.Status, "cancelling one running mission frees its slot for the waiting mission");
                        firstMissions = await Get<EnumerationResult<Mission>>(client, "/api/v1/missions?voyageId=" + first.Id);
                        AssertEqual(2, firstMissions.Objects.Count(m => m.Status == MissionStatusEnum.InProgress), "refill preserves the configured cap");
                        Voyage second = await Post<Voyage>(client, "/api/v1/voyages", new
                        {
                            Title = "Waiting voyage " + suffix, VesselId = vessel.Id,
                            Missions = new[] { new { Title = "Other voyage", Description = "Wait for a voyage slot" } }
                        });
                        voyageIds.Add(second.Id);
                        Mission secondMission = (await Get<EnumerationResult<Mission>>(client, "/api/v1/missions?voyageId=" + second.Id)).Objects.Single();
                        secondMission = await Get<Mission>(client, "/api/v1/missions/" + secondMission.Id);
                        AssertEqual(MissionStatusEnum.Pending, secondMission.Status);
                        AssertEqual(MissionAssignmentBlockerReasonEnum.VesselVoyageLimit, secondMission.AssignmentBlocker!.Reason);

                        // Increase admission live, through the same PUT the dashboard editor uses.
                        vessel.MaxParallelVoyages = 2;
                        using HttpResponseMessage updated = await client.PutAsync("/api/v1/vessels/" + vessel.Id, JsonHelper.ToJsonContent(vessel), ct);
                        updated.EnsureSuccessStatusCode();
                        await fx.Server.Admiral.HealthCheckAsync(ct);
                        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
                        do
                        {
                            secondMission = await Get<Mission>(client, "/api/v1/missions/" + secondMission.Id);
                            if (secondMission.Status == MissionStatusEnum.InProgress) break;
                            await Task.Delay(100, ct);
                        } while (DateTime.UtcNow < deadline);
                        AssertEqual(MissionStatusEnum.InProgress, secondMission.Status, "live limit edit admits the waiting voyage on a dispatch tick");
                        AssertTrue(fx.StubAgents.StartedProcessIds.Count >= 3, "OS processes were actually launched");
                    }
                    finally
                    {
                        foreach (string id in voyageIds) await client.DeleteAsync("/api/v1/voyages/" + id);
                        fx.StubAgents.StopAll();
                    }
                }, tags: new List<string> { TestTags.Positive }),
                new TestCaseDescriptor(suite, "vessel_defaults_and_clamps", "REST respects configured creation defaults and clamps explicit admission values", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                    int oldVoyages = fx.Settings.Concurrency.DefaultVesselMaxParallelVoyages;
                    int oldMissions = fx.Settings.Concurrency.DefaultVesselMaxParallelMissionsPerVoyage;
                    try
                    {
                        fx.Settings.Concurrency.DefaultVesselMaxParallelVoyages = 2;
                        fx.Settings.Concurrency.DefaultVesselMaxParallelMissionsPerVoyage = 5;
                        Vessel defaults = await Post<Vessel>(fx.AuthClient, "/api/v1/vessels", new
                        {
                            Name = "defaults-" + Guid.NewGuid().ToString("N"), RepoUrl = TestRepoHelper.GetLocalBareRepoUrl()
                        });
                        AssertEqual(2, defaults.MaxParallelVoyages);
                        AssertEqual(5, defaults.MaxParallelMissionsPerVoyage);
                        Vessel explicitLimits = await Post<Vessel>(fx.AuthClient, "/api/v1/vessels", new
                        {
                            name = "bounds-" + Guid.NewGuid().ToString("N"), repoUrl = TestRepoHelper.GetLocalBareRepoUrl(),
                            maxParallelVoyages = -1, maxParallelMissionsPerVoyage = int.MaxValue
                        });
                        AssertEqual(1, explicitLimits.MaxParallelVoyages);
                        AssertEqual(1000, explicitLimits.MaxParallelMissionsPerVoyage);
                        AssertEqual(1000, (await Get<Vessel>(fx.AuthClient, "/api/v1/vessels/" + explicitLimits.Id)).MaxParallelMissionsPerVoyage);
                    }
                    finally
                    {
                        fx.Settings.Concurrency.DefaultVesselMaxParallelVoyages = oldVoyages;
                        fx.Settings.Concurrency.DefaultVesselMaxParallelMissionsPerVoyage = oldMissions;
                    }
                }, tags: new List<string> { TestTags.Positive })
            });
        }

        private static async Task<T> Post<T>(HttpClient client, string path, object payload)
        {
            using HttpResponseMessage response = await client.PostAsync(path, JsonHelper.ToJsonContent(payload));
            response.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<T>(response);
        }

        private static async Task<T> Get<T>(HttpClient client, string path)
        {
            using HttpResponseMessage response = await client.GetAsync(path);
            response.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<T>(response);
        }
    }
}
