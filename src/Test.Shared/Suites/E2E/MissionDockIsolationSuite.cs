namespace Test.Shared.Suites.E2E
{
    using System.Net;
    using System.Diagnostics;
    using System.Text.Json;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Runtimes.Mcp;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>REST/MCP cannot borrow a sibling mission's instructions or live diff.</summary>
    public sealed class MissionDockIsolationSuite : IArmadaTestSuite
    {
        public TestSuiteDescriptor Build()
        {
            const string suite = "E2E.MissionDockIsolation";
            List<TestCaseDescriptor> cases = new();
            foreach (string mode in new[] { "own", "missing", "deleted", "wrong-vessel", "saved-diff", "saved-instructions" })
                cases.Add(new(suite, mode, "Mission dock isolation: " + mode, async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                    LoggingModule log = new(); log.Settings.EnableConsole = false;
                    using DatabaseDriver db = DatabaseDriverFactory.Create(new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") }, log);
                    await db.InitializeAsync();
                    string suffix = Guid.NewGuid().ToString("N");
                    string ownPath = TestGitRepoHelper.CreateWorkingRepoCopy(), otherPath = TestGitRepoHelper.CreateWorkingRepoCopy();
                    try
                    {
                        await File.WriteAllTextAsync(Path.Combine(ownPath, "README.md"), "OWN_ONLY\n");
                        await File.WriteAllTextAsync(Path.Combine(otherPath, "README.md"), "SIBLING_ONLY\n");
                        await File.WriteAllTextAsync(Path.Combine(ownPath, "CLAUDE.md"), "OWN_INSTRUCTIONS");
                        await File.WriteAllTextAsync(Path.Combine(otherPath, "CLAUDE.md"), "SIBLING_INSTRUCTIONS");
                        foreach (string path in new[] { ownPath, otherPath })
                        {
                            await Git(path, "checkout", "-b", "mission"); await Git(path, "add", "-A");
                            await Git(path, "-c", "commit.gpgsign=false", "commit", "-m", "Fixture evidence");
                        }
                        Vessel vessel = await db.Vessels.CreateAsync(new Vessel("isolation-" + suffix, "https://example.invalid/repo") { Active = false });
                        Vessel otherVessel = await db.Vessels.CreateAsync(new Vessel("different-" + suffix, "https://example.invalid/other") { Active = false });
                        Dock own = await db.Docks.CreateAsync(new Dock(vessel.Id) { WorktreePath = ownPath, BranchName = "mission" });
                        Dock other = await db.Docks.CreateAsync(new Dock(mode == "wrong-vessel" ? otherVessel.Id : vessel.Id) { WorktreePath = otherPath, BranchName = "mission" });
                        Captain captain = await CaptainAssignmentFixture.CreateAsync(db, new Captain("multi-mission-" + suffix) { Active = false, State = CaptainStateEnum.Planning,
                            CurrentSessionId = "layout-reservation", MaxConcurrency = 3 , Assignment = new Mission("fixture assignment") { DockId = other.Id }});
                        Mission mission = await db.Missions.CreateAsync(new Mission("target") { VesselId = vessel.Id, CaptainId = captain.Id,
                            Status = MissionStatusEnum.Complete, BranchName = "mission", DockId = mode == "own" ? own.Id : mode == "deleted" ? "dck_deleted" : mode == "wrong-vessel" ? other.Id : null,
                            DiffSnapshot = mode == "saved-diff" ? "SAVED_OWN" : null });
                        if (mode == "saved-instructions")
                        {
                            string snapshots = Path.Combine(fx.Settings.LogDirectory, "instructions"); Directory.CreateDirectory(snapshots);
                            await File.WriteAllTextAsync(Path.Combine(snapshots, mission.Id + ".CLAUDE.md"), "SAVED_INSTRUCTIONS");
                        }
                        using HttpResponseMessage instructions = await fx.AuthClient.GetAsync("/api/v1/missions/" + mission.Id + "/instructions", ct);
                        AssertStatusCode(mode == "own" || mode == "saved-instructions" ? HttpStatusCode.OK : HttpStatusCode.NotFound, instructions);
                        string instructionBody = await instructions.Content.ReadAsStringAsync(ct);
                        AssertFalse(instructionBody.Contains("SIBLING_INSTRUCTIONS"));
                        if (mode == "own") AssertContains("OWN_INSTRUCTIONS", instructionBody);
                        if (mode == "saved-instructions") AssertContains("SAVED_INSTRUCTIONS", instructionBody);
                        using HttpResponseMessage diff = await fx.AuthClient.GetAsync("/api/v1/missions/" + mission.Id + "/diff", ct);
                        bool hasDiff = mode == "own" || mode == "saved-diff";
                        AssertStatusCode(hasDiff ? HttpStatusCode.OK : HttpStatusCode.NotFound, diff);
                        string diffBody = await diff.Content.ReadAsStringAsync(ct); AssertFalse(diffBody.Contains("SIBLING_ONLY"));
                        if (hasDiff) AssertContains(mode == "own" ? "OWN_ONLY" : "SAVED_OWN", diffBody);
                        using McpToolClient mcp = new("http://127.0.0.1:" + fx.McpPort + "/mcp", null, new Dictionary<string,string> { ["X-Api-Key"] = fx.ApiKey }); await mcp.InitializeAsync();
                        var result = await mcp.CallToolResultAsync("get_mission_diff", JsonHelper.Serialize(new { missionId = mission.Id }));
                        AssertFalse(result.Text.Contains("SIBLING_ONLY"));
                        if (hasDiff) AssertContains(mode == "own" ? "OWN_ONLY" : "SAVED_OWN", result.Text);
                        else AssertEqual(McpToolErrorCodeEnum.Unavailable, JsonHelper.Deserialize<McpToolError>(result.Text).ErrorCode);
                    }
                    finally { TestTemp.TryDelete(ownPath); TestTemp.TryDelete(otherPath); }
                }));
            return new(suite, "Mission dock isolation (HTTP/git/MCP)", cases);
        }
        private static async Task Git(string directory, params string[] args)
        {
            ProcessStartInfo info = new("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            using Process process = Process.Start(info)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(); await output;
            AssertEqual(0, process.ExitCode, await error);
        }
    }
}
