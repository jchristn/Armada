namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Armada.Runtimes.Mcp;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>One captain, multiple real processes/worktrees, isolated stops and durable ownership.</summary>
    public sealed class CaptainConcurrencySuite : IArmadaTestSuite
    {
        /// <summary>Build the credential-free HTTP scenario.</summary>
        public TestSuiteDescriptor Build()
        {
            const string suite = "E2E.CaptainConcurrency";
            return new TestSuiteDescriptor(suite, "Multi-mission captain end to end", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(suite, "one_captain_three_processes", "One captain runs three missions; cancellation, duplicate release and recall preserve slot ownership", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                    HttpClient client = fx.AuthClient;
                    List<string> missionIds = new List<string>();
                    string suffix = Guid.NewGuid().ToString("N")[..8];
                    try
                    {
                        Captain captain = await Post<Captain>(client, "/api/v1/captains", new { Name = "parallel-" + suffix, Runtime = "ClaudeCode", MaxConcurrency = 3 });
                        AssertEqual(3, captain.MaxConcurrency);
                        Vessel vessel = await Post<Vessel>(client, "/api/v1/vessels", new { Name = "slots-" + suffix,
                            RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(), MaxParallelVoyages = 0, MaxParallelMissionsPerVoyage = 0 });
                        List<Mission> missions = new List<Mission>();
                        for (int i = 0; i < 4; i++)
                        {
                            using HttpResponseMessage response = await client.PostAsync("/api/v1/missions", JsonHelper.ToJsonContent(new
                            {
                                Title = "Independent audit " + i, VesselId = vessel.Id, Mode = "Audit", RequestedCaptainId = captain.Id
                            }), ct);
                            response.EnsureSuccessStatusCode();
                            string body = await response.Content.ReadAsStringAsync(ct);
                            Mission mission = JsonHelper.Deserialize<MissionCreateResponse>(body).Mission ?? JsonHelper.Deserialize<Mission>(body);
                            missionIds.Add(mission.Id);
                            AssertTrue(mission.Status == MissionStatusEnum.Assigned || mission.Status == MissionStatusEnum.InProgress || mission.Status == MissionStatusEnum.Pending,
                                "create returns once the mission is admitted: " + mission.Status);
                        }
                        // Create returns after admission; dock provisioning and launch finish in the background.
                        for (int attempt = 0; attempt < 100; attempt++)
                        {
                            missions.Clear();
                            foreach (string id in missionIds) missions.Add(await Get<Mission>(client, "/api/v1/missions/" + id));
                            if (missions.Count(m => m.Status == MissionStatusEnum.InProgress) == 3) break;
                            await Task.Delay(100, ct);
                        }
                        AssertEqual(3, missions.Count(m => m.Status == MissionStatusEnum.InProgress));
                        AssertEqual(1, missions.Count(m => m.Status == MissionStatusEnum.Pending));
                        List<Mission> running = missions.Where(m => m.Status == MissionStatusEnum.InProgress).ToList();
                        AssertEqual(3, running.Select(m => m.ProcessId).Distinct().Count());
                        AssertEqual(3, running.Select(m => m.DockId).Distinct().Count());
                        AssertTrue(running.All(m => m.CaptainId == captain.Id));
                        Captain occupied = await Get<Captain>(client, "/api/v1/captains/" + captain.Id);
                        AssertEqual(3, occupied.ActiveMissionCount);
                        AssertEqual(3, occupied.ActiveMissionIds.Count);
                        Mission pending = missions.Single(m => m.Status == MissionStatusEnum.Pending);
                        AssertEqual(MissionAssignmentBlockerReasonEnum.CaptainConcurrencyLimit, pending.AssignmentBlocker!.Reason);

                        // Cancel a non-primary slot. The two sibling processes and docks must survive.
                        Mission cancelled = running[1];
                        using (HttpResponseMessage response = await client.DeleteAsync("/api/v1/missions/" + cancelled.Id, ct)) response.EnsureSuccessStatusCode();
                        occupied = await Get<Captain>(client, "/api/v1/captains/" + captain.Id);
                        AssertEqual(2, occupied.ActiveMissionCount);
                        AssertEqual(CaptainStateEnum.Working, occupied.State);
                        foreach (Mission sibling in running.Where(m => m.Id != cancelled.Id))
                        {
                            Mission stillRunning = await Get<Mission>(client, "/api/v1/missions/" + sibling.Id);
                            AssertEqual(MissionStatusEnum.InProgress, stillRunning.Status);
                            AssertEqual(sibling.ProcessId, stillRunning.ProcessId);
                            AssertTrue(StubAgentProcesses.IsRunning(sibling.ProcessId!.Value), "sibling OS process remains alive");
                        }
                        using (HttpResponseMessage response = await client.DeleteAsync("/api/v1/missions/" + cancelled.Id, ct)) response.EnsureSuccessStatusCode();
                        AssertEqual(2, (await Get<Captain>(client, "/api/v1/captains/" + captain.Id)).ActiveMissionCount, "duplicate cancel cannot release another slot");
                        await fx.Server.Admiral.HealthCheckAsync(ct);
                        await E2eBackgroundLaunch.SettleAsync(client, ct);
                        pending = await Get<Mission>(client, "/api/v1/missions/" + pending.Id);
                        AssertEqual(MissionStatusEnum.InProgress, pending.Status);
                        AssertEqual(captain.Id, pending.CaptainId);
                        AssertEqual(3, (await Get<Captain>(client, "/api/v1/captains/" + captain.Id)).ActiveMissionCount);

                        // A real process crash must settle only its slot. Restarting it rejoins the same captain.
                        Mission failed = running[0];
                        using (System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(failed.ProcessId!.Value))
                        {
                            process.Kill(entireProcessTree: true);
                            await process.WaitForExitAsync(ct);
                        }
                        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                        do
                        {
                            failed = await Get<Mission>(client, "/api/v1/missions/" + failed.Id);
                            if (failed.Status == MissionStatusEnum.Failed) break;
                            await Task.Delay(50, ct);
                        } while (DateTime.UtcNow < deadline);
                        AssertEqual(MissionStatusEnum.Failed, failed.Status, "only the crashed mission fails");
                        deadline = DateTime.UtcNow.AddSeconds(15);
                        do
                        {
                            occupied = await Get<Captain>(client, "/api/v1/captains/" + captain.Id);
                            if (occupied.ActiveMissionCount == 2) break;
                            await Task.Delay(50, ct);
                        } while (DateTime.UtcNow < deadline);
                        AssertEqual(2, occupied.ActiveMissionCount, "crash cleanup releases exactly its own slot");
                        AssertTrue(StubAgentProcesses.IsRunning(running[2].ProcessId!.Value));
                        AssertTrue(StubAgentProcesses.IsRunning(pending.ProcessId!.Value));
                        using (HttpResponseMessage response = await client.PostAsync("/api/v1/missions/" + failed.Id + "/restart", JsonHelper.ToJsonContent(new { }), ct)) response.EnsureSuccessStatusCode();
                        failed = await Get<Mission>(client, "/api/v1/missions/" + failed.Id);
                        AssertEqual(MissionStatusEnum.InProgress, failed.Status);
                        AssertEqual(captain.Id, failed.CaptainId);
                        AssertTrue(failed.ProcessId != running[0].ProcessId);
                        AssertEqual(3, (await Get<Captain>(client, "/api/v1/captains/" + captain.Id)).ActiveMissionCount);
                        using (HttpResponseMessage response = await client.PutAsync("/api/v1/captains/" + captain.Id,
                            JsonHelper.ToJsonContent(new { MaxConcurrency = 2 }), ct)) response.EnsureSuccessStatusCode();
                        occupied = await Get<Captain>(client, "/api/v1/captains/" + captain.Id);
                        AssertEqual(2, occupied.MaxConcurrency, "explicit REST capacity edit persists");
                        AssertEqual(3, occupied.ActiveMissionCount, "lowering capacity preserves existing missions");
                        using McpToolClient mcp = new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", null,
                            new Dictionary<string, string> { ["x-api-key"] = fx.ApiKey });
                        await mcp.InitializeAsync();
                        string mcpResult = await mcp.CallToolAsync("update_captain", JsonSerializer.Serialize(new { captainId = captain.Id, maxConcurrency = 3 }));
                        AssertContains(captain.Id, mcpResult, "MCP capacity edit persists");

                        // An unrelated partial edit must retain the configured capacity and server-owned counters.
                        using (HttpResponseMessage response = await client.PutAsync("/api/v1/captains/" + captain.Id,
                            JsonHelper.ToJsonContent(new { Name = "renamed-" + suffix, ActiveMissionCount = 0, ActiveMissionIds = Array.Empty<string>() }), ct)) response.EnsureSuccessStatusCode();
                        occupied = await Get<Captain>(client, "/api/v1/captains/" + captain.Id);
                        AssertEqual(3, occupied.MaxConcurrency);
                        AssertEqual(3, occupied.ActiveMissionCount);
                        using (HttpResponseMessage response = await client.PostAsync("/api/v1/captains/" + captain.Id + "/stop", null, ct)) response.EnsureSuccessStatusCode();
                        occupied = await Get<Captain>(client, "/api/v1/captains/" + captain.Id);
                        AssertEqual(0, occupied.ActiveMissionCount);
                        AssertEqual(0, occupied.ActiveMissionIds.Count);
                        AssertEqual(CaptainStateEnum.Idle, occupied.State);
                        foreach (string id in missionIds) AssertEqual(MissionStatusEnum.Cancelled, (await Get<Mission>(client, "/api/v1/missions/" + id)).Status);
                    }
                    finally
                    {
                        foreach (string id in missionIds) await client.DeleteAsync("/api/v1/missions/" + id);
                        fx.StubAgents.StopAll();
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
