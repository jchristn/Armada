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
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Runtimes.Mcp;
    using McpToolCallResult = Armada.Runtimes.Mcp.McpToolCallResult;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Fixture battery over REST and MCP against a real in-process Admiral, with a canned dispatcher in place of the
    /// captain: list_battery_packs, run_battery and get_battery_run, their authorization (tenant admin to run, tenant
    /// scoping to read, captain sessions and anonymous callers refused) and their 400/403/404 errors.
    /// </summary>
    public sealed class BatteryApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.Battery";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("rest_contract", "REST: list packs, run the C# pack, read the run; errors and tenant scoping", async () =>
            {
                E2EServerFixture fx = await AcquireAsync().ConfigureAwait(false);
                E2ETenantUser adminA = await E2ETenantUser.CreateAsync(fx.AuthClient, "bat-admin-a", true).ConfigureAwait(false);
                E2ETenantUser userA = await E2ETenantUser.CreateAsync(fx.AuthClient, "bat-user-a", false, adminA.TenantId).ConfigureAwait(false);
                E2ETenantUser adminB = await E2ETenantUser.CreateAsync(fx.AuthClient, "bat-admin-b", true).ConfigureAwait(false);
                Captain captainA = await SeedCaptainAsync(fx, adminA.TenantId).ConfigureAwait(false);
                using HttpClient a = adminA.CreateClient(fx.BaseUrl);
                using HttpClient ua = userA.CreateClient(fx.BaseUrl);
                using HttpClient b = adminB.CreateClient(fx.BaseUrl);

                EnumerationResult<BatteryPackInfo> packs = await GetAsync<EnumerationResult<BatteryPackInfo>>(ua, "/api/v1/battery/packs").ConfigureAwait(false);
                BatteryPackInfo csharp = packs.Objects.Single(p => p.Id == "csharp");
                AssertTrue(csharp.Valid, "valid: " + String.Join(" | ", csharp.Errors));
                AssertEqual(10, csharp.Tasks.Count);
                AssertEqual(BatteryGraderTypeEnum.ByteComparison, csharp.Tasks.Single(t => t.Id == "bom-crlf").Grader);
                EnumerationResult<BatteryPackInfo> page = await GetAsync<EnumerationResult<BatteryPackInfo>>(ua, "/api/v1/battery/packs?pageSize=1&pageNumber=1").ConfigureAwait(false);
                AssertEqual(1, page.Objects.Count, "page of one");
                AssertEqual(1, page.PageSize);
                AssertStatusCode(HttpStatusCode.BadRequest, await ua.GetAsync("/api/v1/battery/packs?pageSize=x").ConfigureAwait(false), "bad page size");
                AssertStatusCode(HttpStatusCode.Unauthorized, await fx.UnauthClient.GetAsync("/api/v1/battery/packs").ConfigureAwait(false), "anonymous");

                AssertStatusCode(HttpStatusCode.Forbidden, await ua.PostAsync("/api/v1/battery/runs", Body(captainA.Id, "csharp", true)).ConfigureAwait(false), "non-admin cannot run");
                AssertStatusCode(HttpStatusCode.NotFound, await b.PostAsync("/api/v1/battery/runs", Body(captainA.Id, "csharp", true)).ConfigureAwait(false), "other tenant's captain");
                AssertStatusCode(HttpStatusCode.NotFound, await a.PostAsync("/api/v1/battery/runs", Body("cpt_missing", "csharp", true)).ConfigureAwait(false), "unknown captain");
                AssertStatusCode(HttpStatusCode.BadRequest, await a.PostAsync("/api/v1/battery/runs", null).ConfigureAwait(false), "no body");
                AssertStatusCode(HttpStatusCode.BadRequest, await a.PostAsync("/api/v1/battery/runs", Body(captainA.Id, "nope", true)).ConfigureAwait(false), "unknown pack");
                AssertStatusCode(HttpStatusCode.BadRequest, await a.PostAsync("/api/v1/battery/runs", JsonHelper.ToJsonContent(new BatteryRunRequest { CaptainId = captainA.Id })).ConfigureAwait(false), "no packs");
                AssertStatusCode(HttpStatusCode.BadRequest, await a.PostAsync("/api/v1/battery/runs", JsonHelper.ToJsonContent(new BatteryRunRequest { CaptainId = captainA.Id, Packs = new List<string> { "csharp" }, RunCount = 11, CreateFixtureVessel = true })).ConfigureAwait(false), "run count");
                HttpResponseMessage noVessel = await a.PostAsync("/api/v1/battery/runs", Body(captainA.Id, "csharp", false)).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.BadRequest, noVessel, "fixture vessel missing");
                AssertContains("createFixtureVessel", await noVessel.Content.ReadAsStringAsync().ConfigureAwait(false));

                HttpResponseMessage created = await a.PostAsync("/api/v1/battery/runs", JsonHelper.ToJsonContent(new BatteryRunRequest
                {
                    CaptainId = captainA.Id, Packs = new List<string> { "csharp" }, RunCount = 1, Seed = 99, CreateFixtureVessel = true
                })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, created, "run started");
                BatteryRun queued = await JsonHelper.DeserializeAsync<BatteryRun>(created).ConfigureAwait(false);
                AssertTrue(queued.Id.StartsWith("bat_", StringComparison.Ordinal), queued.Id);
                AssertEqual(adminA.TenantId, queued.TenantId, "run in the captain's tenant");

                BatteryRun run = await WaitRestAsync(a, queued.Id).ConfigureAwait(false);
                AssertEqual(BatteryRunStatusEnum.Completed, run.Status, run.FailureReason);
                AssertEqual("csharp@1", run.BatteryVersion);
                AssertEqual((int?)99, run.Seed);
                AssertTrue(run.IsEvaluation, "evaluation");
                AssertEqual(16, run.ConfigurationRevision!.Length, "configuration revision recorded");
                AssertEqual(3, run.PassedCount, "both Judge controls and the linter pass");
                AssertEqual(2, run.PendingEvaluationCount, "plan and summary wait for the evaluator");
                AssertEqual(5, run.FailedCount, "cancelled worktree tasks fail");

                // The run read does not embed results; they are a paged enumeration (GET and POST twins).
                HttpResponseMessage rawRun = await a.GetAsync("/api/v1/battery/runs/" + run.Id).ConfigureAwait(false);
                string rawRunBody = await rawRun.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertFalse(rawRunBody.Contains("\"Results\"", StringComparison.Ordinal), "no embedded Results");
                HttpResponseMessage page1 = await a.GetAsync("/api/v1/battery/runs/" + run.Id + "/results?pageNumber=1&pageSize=4&order=CreatedAscending").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, page1, "results page 1");
                EnumerationResult<BatteryTaskResult> first = await JsonHelper.DeserializeAsync<EnumerationResult<BatteryTaskResult>>(page1).ConfigureAwait(false);
                AssertEqual(10L, first.TotalRecords, "one result per task");
                AssertEqual(3, first.TotalPages);
                AssertEqual(4, first.Objects.Count);
                List<BatteryTaskResult> all = new List<BatteryTaskResult>(first.Objects);
                for (int pageNumber = 2; pageNumber <= first.TotalPages; pageNumber++)
                {
                    HttpResponseMessage next = await a.PostAsync("/api/v1/battery/runs/" + run.Id + "/results/enumerate",
                        JsonHelper.ToJsonContent(new { pageNumber, pageSize = 4, order = "CreatedAscending" })).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, next, "results page " + pageNumber);
                    all.AddRange((await JsonHelper.DeserializeAsync<EnumerationResult<BatteryTaskResult>>(next).ConfigureAwait(false)).Objects);
                }

                AssertEqual(10, all.Select(r => r.Id).Distinct().Count(), "pages neither overlap nor skip");
                AssertTrue(all.All(r => r.FixtureCommit != null && r.VesselId != null), "fixture reset and vessel recorded");
                HttpResponseMessage past = await a.GetAsync("/api/v1/battery/runs/" + run.Id + "/results?pageNumber=9&pageSize=4").ConfigureAwait(false);
                EnumerationResult<BatteryTaskResult> empty = await JsonHelper.DeserializeAsync<EnumerationResult<BatteryTaskResult>>(past).ConfigureAwait(false);
                AssertEqual(0, empty.Objects.Count, "page past the end is empty");
                AssertEqual(10L, empty.TotalRecords, "with an accurate total");
                HttpResponseMessage passed = await a.GetAsync("/api/v1/battery/runs/" + run.Id + "/results?status=Passed&pageSize=1000").ConfigureAwait(false);
                AssertEqual(3L, (await JsonHelper.DeserializeAsync<EnumerationResult<BatteryTaskResult>>(passed).ConfigureAwait(false)).TotalRecords, "outcome filter counts in the database");
                AssertStatusCode(HttpStatusCode.BadRequest, await a.GetAsync("/api/v1/battery/runs/" + run.Id + "/results?status=Bogus").ConfigureAwait(false), "unknown outcome");
                HttpResponseMessage oversized = await a.GetAsync("/api/v1/battery/runs/" + run.Id + "/results?pageSize=5000").ConfigureAwait(false);
                AssertEqual(1000, (await JsonHelper.DeserializeAsync<EnumerationResult<BatteryTaskResult>>(oversized).ConfigureAwait(false)).PageSize, "oversized page size normalized");
                AssertStatusCode(HttpStatusCode.NotFound, await b.GetAsync("/api/v1/battery/runs/" + run.Id + "/results").ConfigureAwait(false), "other tenant cannot page results");

                AssertStatusCode(HttpStatusCode.OK, await ua.GetAsync("/api/v1/battery/runs/" + run.Id).ConfigureAwait(false), "same-tenant user reads");
                AssertStatusCode(HttpStatusCode.NotFound, await b.GetAsync("/api/v1/battery/runs/" + run.Id).ConfigureAwait(false), "other tenant");
                AssertStatusCode(HttpStatusCode.OK, await fx.AuthClient.GetAsync("/api/v1/battery/runs/" + run.Id).ConfigureAwait(false), "global admin");
                AssertStatusCode(HttpStatusCode.NotFound, await a.GetAsync("/api/v1/battery/runs/bat_missing").ConfigureAwait(false), "unknown run");
                AssertStatusCode(HttpStatusCode.Unauthorized, await fx.UnauthClient.GetAsync("/api/v1/battery/runs/" + run.Id).ConfigureAwait(false), "anonymous");

                using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
                {
                    Vessel? vessel = await db.Vessels.ReadByNameAsync(adminA.TenantId, "battery-csharp-" + adminA.TenantId).ConfigureAwait(false);
                    AssertNotNull(vessel, "fixture vessel created in the captain's tenant");
                    AssertEqual((LandingModeEnum?)LandingModeEnum.None, vessel!.LandingMode);
                    Pipeline? pipeline = await db.Pipelines.ReadAsync(vessel.DefaultPipelineId!).ConfigureAwait(false);
                    AssertEqual("ModelEvaluation", pipeline!.Name, "seeded built-in pipeline");
                    AssertTrue(pipeline.IsBuiltIn, "built in");
                }
            }));

            cases.Add(CaseAsync("mcp_contract", "MCP: list_battery_packs, run_battery and get_battery_run with the same rules as REST", async () =>
            {
                E2EServerFixture fx = await AcquireAsync().ConfigureAwait(false);
                E2ETenantUser adminA = await E2ETenantUser.CreateAsync(fx.AuthClient, "bat-mcp-admin-a", true).ConfigureAwait(false);
                E2ETenantUser userA = await E2ETenantUser.CreateAsync(fx.AuthClient, "bat-mcp-user-a", false, adminA.TenantId).ConfigureAwait(false);
                E2ETenantUser adminB = await E2ETenantUser.CreateAsync(fx.AuthClient, "bat-mcp-admin-b", true).ConfigureAwait(false);
                Captain captainA = await SeedCaptainAsync(fx, adminA.TenantId).ConfigureAwait(false);

                using (McpToolClient admin = Mcp(fx, adminA))
                using (McpToolClient user = Mcp(fx, userA))
                using (McpToolClient b = Mcp(fx, adminB))
                {
                    await admin.InitializeAsync().ConfigureAwait(false);
                    await user.InitializeAsync().ConfigureAwait(false);
                    await b.InitializeAsync().ConfigureAwait(false);

                    EnumerationResult<BatteryPackInfo> packs = await OkAsync<EnumerationResult<BatteryPackInfo>>(user, "list_battery_packs", new { }).ConfigureAwait(false);
                    AssertTrue(packs.Objects.Any(p => p.Id == "csharp" && p.Valid), "csharp listed");

                    await ErrorAsync(user, "run_battery", new { packs = new[] { "csharp" }, captainId = captainA.Id, createFixtureVessel = true }, McpToolErrorCodeEnum.Forbidden).ConfigureAwait(false);
                    await ErrorAsync(b, "run_battery", new { packs = new[] { "csharp" }, captainId = captainA.Id, createFixtureVessel = true }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    await ErrorAsync(admin, "run_battery", new { packs = new[] { "nope" }, captainId = captainA.Id }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);
                    await ErrorAsync(admin, "run_battery", new { packs = new string[0], captainId = captainA.Id }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);
                    await ErrorAsync(admin, "run_battery", new { packs = new[] { "csharp" }, captainId = captainA.Id, createFixtureVessel = false }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);

                    BatteryRun queued = await OkAsync<BatteryRun>(admin, "run_battery", new { packs = new[] { "csharp" }, captainId = captainA.Id, runCount = 1, createFixtureVessel = true }).ConfigureAwait(false);
                    AssertEqual(1, queued.RunCount);
                    AssertNotNull(queued.Seed, "a seed is picked");
                    await WaitRestAsync(fx.AuthClient, queued.Id).ConfigureAwait(false);

                    BatteryRun read = await OkAsync<BatteryRun>(user, "get_battery_run", new { id = queued.Id }).ConfigureAwait(false);
                    AssertEqual(BatteryRunStatusEnum.Completed, read.Status);
                    EnumerationResult<BatteryTaskResult> results = await OkAsync<EnumerationResult<BatteryTaskResult>>(user, "list_battery_results", new { runId = queued.Id, pageSize = 1000 }).ConfigureAwait(false);
                    AssertEqual(10L, results.TotalRecords);
                    AssertEqual(10, results.Objects.Count);
                    BatteryTaskResult judge = results.Objects.Single(r => r.TaskId == "judge-negative");
                    AssertEqual(3, judge.Details.Matched.Count, "seeded set evidence over MCP");
                    AssertNotNull(results.Objects.Single(r => r.TaskId == "architect-plan").Output, "raw output stored for the evaluator");
                    EnumerationResult<BatteryTaskResult> small = await OkAsync<EnumerationResult<BatteryTaskResult>>(user, "list_battery_results", new { runId = queued.Id, pageNumber = 2, pageSize = 3 }).ConfigureAwait(false);
                    AssertEqual(3, small.Objects.Count, "MCP pages");
                    AssertEqual(4, small.TotalPages);
                    await ErrorAsync(b, "list_battery_results", new { runId = queued.Id }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    await ErrorAsync(b, "get_battery_run", new { id = queued.Id }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    await ErrorAsync(admin, "get_battery_run", new { id = "bat_missing" }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("pack_management", "Pack CRUD, validation, global authorization and safe file access", async () =>
            {
                E2EServerFixture fx = await AcquireAsync().ConfigureAwait(false);
                string? original = fx.Settings.Evaluation.BatteryDirectory;
                foreach (string tool in new[] { "get_battery_pack", "read_battery_pack_file", "validate_battery_pack" })
                    AssertTrue(Armada.Core.Services.Ask.AskToolPolicy.IsReadOnly(tool), "Ask read-only: " + tool);
                foreach (string tool in new[] { "write_battery_pack_file", "delete_battery_pack_file", "create_battery_pack", "update_battery_pack_manifest", "delete_battery_pack" })
                    AssertFalse(Armada.Core.Services.Ask.AskToolPolicy.IsReadOnly(tool), "Ask proposal-gated: " + tool);
                string sourceBattery = new Armada.Core.Services.BatteryPackCatalog(() => original).ResolveDirectory()!;
                string directory = Path.Combine(fx.TempDir, "managed-battery");
                Directory.CreateDirectory(directory);
                fx.Settings.Evaluation.BatteryDirectory = directory;
                try
                {
                    E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "pack-user", false).ConfigureAwait(false);
                    E2ETenantUser tenantAdmin = await E2ETenantUser.CreateAsync(fx.AuthClient, "pack-admin", true).ConfigureAwait(false);
                    using HttpClient reader = user.CreateClient(fx.BaseUrl);
                    using HttpClient ta = tenantAdmin.CreateClient(fx.BaseUrl);
                    const string root = "/api/v1/battery/packs/demo";
                    BatteryPackEditRequest edit = new BatteryPackEditRequest { PackId = "demo", ManifestJson = "{}" };
                    AssertStatusCode(HttpStatusCode.Forbidden, await reader.PostAsync("/api/v1/battery/packs", JsonHelper.ToJsonContent(edit)).ConfigureAwait(false));
                    AssertStatusCode(HttpStatusCode.Forbidden, await ta.PostAsync("/api/v1/battery/packs", JsonHelper.ToJsonContent(edit)).ConfigureAwait(false));
                    HttpResponseMessage created = await fx.AuthClient.PostAsync("/api/v1/battery/packs", JsonHelper.ToJsonContent(edit)).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, created);
                    BatteryPackDetail pack = await JsonHelper.DeserializeAsync<BatteryPackDetail>(created).ConfigureAwait(false);
                    AssertFalse(pack.Info.Valid, "empty manifest remains editable");
                    AssertTrue(pack.Info.Errors.Count > 0, "validation errors returned");
                    AssertTrue(Directory.Exists(Path.Combine(directory, "demo", "fixture")), "fixture created");
                    AssertTrue(Directory.Exists(Path.Combine(directory, "demo", "tasks")), "tasks created");
                    AssertStatusCode(HttpStatusCode.Conflict, await fx.AuthClient.PostAsync("/api/v1/battery/packs", JsonHelper.ToJsonContent(edit)).ConfigureAwait(false));
                    AssertStatusCode(HttpStatusCode.OK, await reader.GetAsync(root).ConfigureAwait(false));
                    foreach (string invalidId in new[] { "BAD", "..", "a/b", "/tmp", "" })
                        AssertStatusCode(HttpStatusCode.BadRequest, await fx.AuthClient.PostAsync("/api/v1/battery/packs", JsonHelper.ToJsonContent(new BatteryPackEditRequest { PackId = invalidId, ManifestJson = "{}" })).ConfigureAwait(false));
                    string fileUrl = root + "/files?path=fixture/test.txt";
                    AssertStatusCode(HttpStatusCode.Forbidden, await reader.PutAsync(fileUrl, JsonHelper.ToJsonContent(new BatteryPackTextFile { Content = "hello" })).ConfigureAwait(false));
                    AssertStatusCode(HttpStatusCode.OK, await fx.AuthClient.PutAsync(fileUrl, JsonHelper.ToJsonContent(new BatteryPackTextFile { Content = "hello" })).ConfigureAwait(false));
                    BatteryPackTextFile file = await GetAsync<BatteryPackTextFile>(reader, fileUrl).ConfigureAwait(false);
                    AssertEqual("hello", file.Content);
                    foreach (string path in new[] { "../outside", "/etc/passwd", "fixture/../../outside", "fixture/../battery-pack.json", "C:\\secret" })
                    {
                        string url = root + "/files?path=" + Uri.EscapeDataString(path);
                        AssertStatusCode(HttpStatusCode.BadRequest, await reader.GetAsync(url).ConfigureAwait(false), path);
                        AssertStatusCode(HttpStatusCode.BadRequest, await fx.AuthClient.PutAsync(url, JsonHelper.ToJsonContent(new BatteryPackTextFile { Content = "x" })).ConfigureAwait(false), path);
                        AssertStatusCode(HttpStatusCode.BadRequest, await fx.AuthClient.DeleteAsync(url).ConfigureAwait(false), path);
                    }
                    string link = Path.Combine(directory, "demo", "fixture", "escape");
                    Directory.CreateSymbolicLink(link, fx.TempDir);
                    try
                    {
                        AssertStatusCode(HttpStatusCode.BadRequest, await reader.GetAsync(root + "/files?path=fixture/escape/armada.db").ConfigureAwait(false));
                        AssertStatusCode(HttpStatusCode.BadRequest, await fx.AuthClient.PutAsync(root + "/files?path=fixture/escape/new.txt", JsonHelper.ToJsonContent(new BatteryPackTextFile { Content = "x" })).ConfigureAwait(false));
                        AssertStatusCode(HttpStatusCode.BadRequest, await fx.AuthClient.DeleteAsync(root).ConfigureAwait(false));
                    }
                    finally { Directory.Delete(link); }
                    File.WriteAllBytes(Path.Combine(directory, "demo", "fixture", "binary"), new byte[] { 0xff, 0, 1 });
                    AssertStatusCode(HttpStatusCode.BadRequest, await reader.GetAsync(root + "/files?path=fixture/binary").ConfigureAwait(false));
                    AssertStatusCode(HttpStatusCode.BadRequest, await fx.AuthClient.PutAsync(fileUrl, JsonHelper.ToJsonContent(new BatteryPackTextFile { Content = new string('a', 1024 * 1024 + 1) })).ConfigureAwait(false));
                    AssertStatusCode(HttpStatusCode.BadRequest, await fx.AuthClient.DeleteAsync(root + "/files?path=battery-pack.json").ConfigureAwait(false));
                    edit.ManifestJson = "{";
                    AssertStatusCode(HttpStatusCode.BadRequest, await fx.AuthClient.PutAsync(root + "/manifest", JsonHelper.ToJsonContent(edit)).ConfigureAwait(false));
                    edit.ManifestJson = "{ \"id\": \"demo\" }";
                    AssertStatusCode(HttpStatusCode.OK, await fx.AuthClient.PutAsync(root + "/manifest", JsonHelper.ToJsonContent(edit)).ConfigureAwait(false));
                    pack = await GetAsync<BatteryPackDetail>(reader, root).ConfigureAwait(false);
                    AssertEqual(edit.ManifestJson, pack.ManifestJson, "raw JSON preserved");
                    AssertTrue(pack.Files.Any(f => f.Path == "fixture/test.txt" && f.Size == 5), "relative tree sizes");
                    BatteryPackInfo validated = await JsonHelper.DeserializeAsync<BatteryPackInfo>(await reader.PostAsync(root + "/validate", null).ConfigureAwait(false)).ConfigureAwait(false);
                    AssertFalse(validated.Valid, "validate without writing");
                    AssertStatusCode(HttpStatusCode.OK, await fx.AuthClient.DeleteAsync(fileUrl).ConfigureAwait(false));
                    AssertStatusCode(HttpStatusCode.NotFound, await reader.GetAsync(fileUrl).ConfigureAwait(false));
                    AssertStatusCode(HttpStatusCode.Forbidden, await reader.DeleteAsync(root).ConfigureAwait(false));
                    AssertStatusCode(HttpStatusCode.Forbidden, await reader.PutAsync(root + "/manifest", JsonHelper.ToJsonContent(edit)).ConfigureAwait(false));
                    AssertStatusCode(HttpStatusCode.Forbidden, await reader.DeleteAsync(fileUrl).ConfigureAwait(false));
                    using (McpToolClient global = new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", null,
                        new Dictionary<string, string> { ["X-Api-Key"] = fx.ApiKey }))
                    using (McpToolClient person = Mcp(fx, user))
                    {
                        await global.InitializeAsync().ConfigureAwait(false);
                        await person.InitializeAsync().ConfigureAwait(false);
                        await ErrorAsync(person, "create_battery_pack", new { packId = "mcp-demo", manifestJson = "{}" }, McpToolErrorCodeEnum.Forbidden).ConfigureAwait(false);
                        await OkAsync<BatteryPackDetail>(global, "create_battery_pack", new { packId = "mcp-demo", manifestJson = "{}" }).ConfigureAwait(false);
                        await OkAsync<BatteryPackDetail>(person, "get_battery_pack", new { packId = "mcp-demo" }).ConfigureAwait(false);
                        await ErrorAsync(person, "write_battery_pack_file", new { packId = "mcp-demo", path = "fixture/file.txt", content = "x" }, McpToolErrorCodeEnum.Forbidden).ConfigureAwait(false);
                        await OkAsync<BatteryPackDetail>(global, "write_battery_pack_file", new { packId = "mcp-demo", path = "fixture/file.txt", content = "mcp text" }).ConfigureAwait(false);
                        BatteryPackTextFile mcpFile = await OkAsync<BatteryPackTextFile>(person, "read_battery_pack_file", new { packId = "mcp-demo", path = "fixture/file.txt" }).ConfigureAwait(false);
                        AssertEqual("mcp text", mcpFile.Content);
                        await ErrorAsync(person, "read_battery_pack_file", new { packId = "mcp-demo", path = "../outside" }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);
                        await ErrorAsync(person, "update_battery_pack_manifest", new { packId = "mcp-demo", manifestJson = "{}" }, McpToolErrorCodeEnum.Forbidden).ConfigureAwait(false);
                        await ErrorAsync(global, "update_battery_pack_manifest", new { packId = "mcp-demo", manifestJson = "{" }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);
                        await OkAsync<BatteryPackDetail>(global, "update_battery_pack_manifest", new { packId = "mcp-demo", manifestJson = "null" }).ConfigureAwait(false);
                        BatteryPackInfo mcpValidation = await OkAsync<BatteryPackInfo>(person, "validate_battery_pack", new { packId = "mcp-demo" }).ConfigureAwait(false);
                        AssertFalse(mcpValidation.Valid);
                        await ErrorAsync(person, "delete_battery_pack_file", new { packId = "mcp-demo", path = "fixture/file.txt" }, McpToolErrorCodeEnum.Forbidden).ConfigureAwait(false);
                        await OkAsync<BatteryPackDetail>(global, "delete_battery_pack_file", new { packId = "mcp-demo", path = "fixture/file.txt" }).ConfigureAwait(false);
                        await ErrorAsync(person, "delete_battery_pack", new { packId = "mcp-demo" }, McpToolErrorCodeEnum.Forbidden).ConfigureAwait(false);
                        await OkAsync<object>(global, "delete_battery_pack", new { packId = "mcp-demo" }).ConfigureAwait(false);
                    }
                    AssertStatusCode(HttpStatusCode.OK, await fx.AuthClient.DeleteAsync(root).ConfigureAwait(false));
                    AssertStatusCode(HttpStatusCode.NotFound, await reader.GetAsync(root).ConfigureAwait(false));

                    // Start an isolated copy of the real pack; never mutate the repository's fixtures in a guard test.
                    string source = Path.Combine(sourceBattery, "csharp");
                    string busy = Path.Combine(directory, "busy");
                    Directory.CreateDirectory(busy);
                    foreach (string child in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                        Directory.CreateDirectory(Path.Combine(busy, Path.GetRelativePath(source, child)));
                    foreach (string child in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                        File.Copy(child, Path.Combine(busy, Path.GetRelativePath(source, child)));
                    string busyManifest = Path.Combine(busy, "battery-pack.json");
                    File.WriteAllText(busyManifest, File.ReadAllText(busyManifest).Replace("\"id\": \"csharp\"", "\"id\": \"busy\"", StringComparison.Ordinal));
                    Captain busyCaptain = await SeedCaptainAsync(fx, Armada.Core.Constants.DefaultTenantId).ConfigureAwait(false);
                    HttpResponseMessage startBusy = await fx.AuthClient.PostAsync("/api/v1/battery/runs", Body(busyCaptain.Id, "busy", true)).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.Created, startBusy);
                    BatteryRun busyRun = await JsonHelper.DeserializeAsync<BatteryRun>(startBusy).ConfigureAwait(false);
                    try
                    {
                        AssertStatusCode(HttpStatusCode.Conflict, await fx.AuthClient.PutAsync("/api/v1/battery/packs/busy/files?path=fixture/guard.txt", JsonHelper.ToJsonContent(new BatteryPackTextFile { Content = "blocked" })).ConfigureAwait(false));
                        AssertStatusCode(HttpStatusCode.Conflict, await fx.AuthClient.PutAsync("/api/v1/battery/packs/busy/manifest", JsonHelper.ToJsonContent(new BatteryPackEditRequest { ManifestJson = "{}" })).ConfigureAwait(false));
                        AssertStatusCode(HttpStatusCode.Conflict, await fx.AuthClient.DeleteAsync("/api/v1/battery/packs/busy").ConfigureAwait(false));
                        AssertFalse(File.Exists(Path.Combine(busy, "fixture", "guard.txt")), "blocked write has no side effect");
                    }
                    finally { await WaitRestAsync(fx.AuthClient, busyRun.Id).ConfigureAwait(false); }
                    AssertStatusCode(HttpStatusCode.OK, await fx.AuthClient.DeleteAsync("/api/v1/battery/packs/busy").ConfigureAwait(false));
                }
                finally { fx.Settings.Evaluation.BatteryDirectory = original; }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "E2E: fixture battery over REST and MCP",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private async Task<E2EServerFixture> AcquireAsync()
        {
            E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
            fx.Server.BatteryTaskDispatcherOverride = CannedBatteryTaskDispatcher.ForCSharpPack();
            return fx;
        }

        private static StringContent Body(string captainId, string pack, bool create)
        {
            return JsonHelper.ToJsonContent(new BatteryRunRequest { CaptainId = captainId, Packs = new List<string> { pack }, RunCount = 1, CreateFixtureVessel = create });
        }

        private static async Task<Captain> SeedCaptainAsync(E2EServerFixture fx, string tenantId)
        {
            using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
            {
                Captain captain = new Captain { Name = "battery-" + Guid.NewGuid().ToString("N").Substring(0, 8), Runtime = AgentRuntimeEnum.ClaudeCode, TenantId = tenantId };
                return await db.Captains.CreateAsync(captain).ConfigureAwait(false);
            }
        }

        private static async Task<T> GetAsync<T>(HttpClient c, string path)
        {
            HttpResponseMessage response = await c.GetAsync(path).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.OK, response, path);
            return await JsonHelper.DeserializeAsync<T>(response).ConfigureAwait(false);
        }

        private static async Task<BatteryRun> WaitRestAsync(HttpClient c, string runId)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(120);
            while (true)
            {
                BatteryRun run = await GetAsync<BatteryRun>(c, "/api/v1/battery/runs/" + runId).ConfigureAwait(false);
                if (run.Status == BatteryRunStatusEnum.Completed || run.Status == BatteryRunStatusEnum.Failed) return run;
                if (DateTime.UtcNow > deadline) throw new TimeoutException("run " + runId + " still " + run.Status);
                await Task.Delay(200).ConfigureAwait(false);
            }
        }

        private static McpToolClient Mcp(E2EServerFixture fx, E2ETenantUser user)
        {
            Dictionary<string, string> headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + user.BearerToken };
            return new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", null, headers);
        }

        private static async Task<T> OkAsync<T>(McpToolClient client, string tool, object args)
        {
            McpToolCallResult result = await client.CallToolResultAsync(tool, JsonHelper.Serialize(args)).ConfigureAwait(false);
            AssertFalse(result.IsError, tool + ": " + result.Text);
            AssertFalse(result.Text.Contains("\"errorCode\"", StringComparison.OrdinalIgnoreCase), tool + " returned an error: " + result.Text);
            return JsonHelper.Deserialize<T>(result.Text);
        }

        private static async Task ErrorAsync(McpToolClient client, string tool, object args, McpToolErrorCodeEnum expected)
        {
            McpToolCallResult result = await client.CallToolResultAsync(tool, JsonHelper.Serialize(args)).ConfigureAwait(false);
            AssertTrue(result.Text.TrimStart().StartsWith("{", StringComparison.Ordinal), tool + " returned no McpToolError: " + result.Text);
            McpToolError error = JsonHelper.Deserialize<McpToolError>(result.Text);
            AssertEqual(expected, error.ErrorCode, tool + ": " + result.Text);
        }

        private static async Task<DatabaseDriver> OpenDatabaseAsync(E2EServerFixture fx)
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
            DatabaseDriver db = DatabaseDriverFactory.Create(settings, quiet);
            await db.InitializeAsync().ConfigureAwait(false);
            return db;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.EndToEnd });
        }

        #endregion
    }
}
