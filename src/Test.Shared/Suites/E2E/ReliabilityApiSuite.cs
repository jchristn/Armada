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
    /// Pipeline reliability over REST and MCP (RS-12.1, RS-12.2): mission attempt history follows the mission read rules
    /// (tenant, owner, global admin), the reliability summary is tenant scoped, gate reports are uploaded by a global
    /// admin and read by admins, and update_model_profile edits the administrator's tier.
    /// </summary>
    public sealed class ReliabilityApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.Reliability";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("rest_scoping", "REST: attempts follow the mission read rules; the summary is tenant scoped; gate reports need an admin", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser adminA = await E2ETenantUser.CreateAsync(fx.AuthClient, "rel-admin-a", true).ConfigureAwait(false);
                E2ETenantUser userA = await E2ETenantUser.CreateAsync(fx.AuthClient, "rel-user-a", false, adminA.TenantId).ConfigureAwait(false);
                E2ETenantUser otherA = await E2ETenantUser.CreateAsync(fx.AuthClient, "rel-other-a", false, adminA.TenantId).ConfigureAwait(false);
                E2ETenantUser adminB = await E2ETenantUser.CreateAsync(fx.AuthClient, "rel-admin-b", true).ConfigureAwait(false);
                string model = "qwen-rel-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Mission mission = await SeedAsync(fx, adminA.TenantId, userA.UserId, model).ConfigureAwait(false);

                using HttpClient a = adminA.CreateClient(fx.BaseUrl);
                using HttpClient ua = userA.CreateClient(fx.BaseUrl);
                using HttpClient oa = otherA.CreateClient(fx.BaseUrl);
                using HttpClient b = adminB.CreateClient(fx.BaseUrl);

                string path = "/api/v1/missions/" + mission.Id + "/attempts";
                MissionAttemptHistory own = await GetAsync<MissionAttemptHistory>(ua, path).ConfigureAwait(false);
                AssertEqual(2, own.Attempts.Count, "owner reads");
                AssertEqual(200, (int)(await a.GetAsync(path).ConfigureAwait(false)).StatusCode, "tenant admin reads");
                AssertEqual(200, (int)(await fx.AuthClient.GetAsync(path).ConfigureAwait(false)).StatusCode, "global admin reads");
                AssertStatusCode(HttpStatusCode.NotFound, await oa.GetAsync(path).ConfigureAwait(false), "another user of the tenant");
                AssertStatusCode(HttpStatusCode.NotFound, await b.GetAsync(path).ConfigureAwait(false), "another tenant");
                AssertStatusCode(HttpStatusCode.Unauthorized, await fx.UnauthClient.GetAsync(path).ConfigureAwait(false), "anonymous");

                ReliabilitySummary mine = await GetAsync<ReliabilitySummary>(ua, "/api/v1/reliability/summary?model=" + model).ConfigureAwait(false);
                AssertEqual(2, mine.Totals.Attempts, "same tenant counts the attempts");
                ReliabilitySummary theirs = await GetAsync<ReliabilitySummary>(b, "/api/v1/reliability/summary?model=" + model + "&tenantId=" + adminA.TenantId).ConfigureAwait(false);
                AssertEqual(0, theirs.Totals.Attempts, "another tenant sees nothing, even naming the tenant");
                ReliabilitySummary global = await GetAsync<ReliabilitySummary>(fx.AuthClient, "/api/v1/reliability/summary?model=" + model + "&tenantId=" + adminA.TenantId).ConfigureAwait(false);
                AssertEqual(2, global.Totals.Attempts, "a global admin filters by tenant");
                AssertStatusCode(HttpStatusCode.BadRequest, await ua.GetAsync("/api/v1/reliability/summary?sort=Bogus").ConfigureAwait(false), "unknown sort");
                AssertStatusCode(HttpStatusCode.BadRequest, await ua.GetAsync("/api/v1/reliability/summary?fromUtc=nope").ConfigureAwait(false), "bad date");

                StringContent report = JsonHelper.ToJsonContent(new GateReport { Measures = new List<GateMeasure> { new GateMeasure { Id = "G1", Status = GateMeasureStatusEnum.Pass } } });
                AssertStatusCode(HttpStatusCode.Forbidden, await a.PostAsync("/api/v1/reliability/gate-reports", report).ConfigureAwait(false), "a tenant admin cannot upload");
                HttpResponseMessage uploaded = await fx.AuthClient.PostAsync("/api/v1/reliability/gate-reports", JsonHelper.ToJsonContent(new GateReport { Measures = new List<GateMeasure> { new GateMeasure { Id = "G1", Status = GateMeasureStatusEnum.Pass } } })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Created, uploaded, "a global admin uploads");
                AssertStatusCode(HttpStatusCode.BadRequest, await fx.AuthClient.PostAsync("/api/v1/reliability/gate-reports", null).ConfigureAwait(false), "no body");
                AssertEqual(200, (int)(await a.GetAsync("/api/v1/reliability/gate-reports/latest").ConfigureAwait(false)).StatusCode, "a tenant admin reads");
                AssertStatusCode(HttpStatusCode.Forbidden, await ua.GetAsync("/api/v1/reliability/gate-reports").ConfigureAwait(false), "a user cannot list");
                AssertStatusCode(HttpStatusCode.NotFound, await a.GetAsync("/api/v1/reliability/gate-reports/gate-..%2Fsettings").ConfigureAwait(false), "traversal is not found");
            }));

            cases.Add(CaseAsync("mcp_tools", "MCP: get_mission_attempts, get_reliability_summary, gate report readers and update_model_profile tier", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser adminA = await E2ETenantUser.CreateAsync(fx.AuthClient, "rel-mcp-admin-a", true).ConfigureAwait(false);
                E2ETenantUser userA = await E2ETenantUser.CreateAsync(fx.AuthClient, "rel-mcp-user-a", false, adminA.TenantId).ConfigureAwait(false);
                E2ETenantUser adminB = await E2ETenantUser.CreateAsync(fx.AuthClient, "rel-mcp-admin-b", true).ConfigureAwait(false);
                string model = "qwen-relmcp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Mission mission = await SeedAsync(fx, adminA.TenantId, userA.UserId, model).ConfigureAwait(false);
                ModelProfile profile;
                using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
                {
                    profile = await db.ModelProfiles.CreateAsync(new ModelProfile { TenantId = adminA.TenantId, Identity = new ModelProfileIdentity { Runtime = "Mux", Model = model } }, "rel-" + model).ConfigureAwait(false);
                }

                using (McpToolClient admin = Mcp(fx, adminA))
                using (McpToolClient user = Mcp(fx, userA))
                using (McpToolClient b = Mcp(fx, adminB))
                {
                    await admin.InitializeAsync().ConfigureAwait(false);
                    await user.InitializeAsync().ConfigureAwait(false);
                    await b.InitializeAsync().ConfigureAwait(false);

                    MissionAttemptHistory history = await OkAsync<MissionAttemptHistory>(user, "get_mission_attempts", new { missionId = mission.Id }).ConfigureAwait(false);
                    AssertEqual(2, history.Attempts.Count, "attempts over MCP");
                    AssertTrue(history.Attempts[0].Escalated, "escalation flag");
                    await ErrorAsync(b, "get_mission_attempts", new { missionId = mission.Id }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);

                    ReliabilitySummary summary = await OkAsync<ReliabilitySummary>(user, "get_reliability_summary", new { model, sort = "Escalations" }).ConfigureAwait(false);
                    AssertEqual(1, summary.Rows.Single().Escalations, "summary over MCP");
                    await ErrorAsync(user, "list_gate_reports", new { }, McpToolErrorCodeEnum.Forbidden).ConfigureAwait(false);
                    await OkAsync<EnumerationResult<GateReport>>(admin, "list_gate_reports", new { pageSize = 5 }).ConfigureAwait(false);

                    ModelProfile updated = await OkAsync<ModelProfile>(admin, "update_model_profile", new { id = profile.Id, tiering = new { tier = "Premium" }, timeouts = new { stallThresholdMinutes = 20 } }).ConfigureAwait(false);
                    AssertEqual((CaptainTierEnum?)CaptainTierEnum.Premium, updated.Tiering.Tier, "tier set over MCP");
                    AssertEqual((int?)20, updated.Timeouts?.StallThresholdMinutes, "timeouts set over MCP");
                    await ErrorAsync(user, "update_model_profile", new { id = profile.Id, tiering = new { tier = "Economy" } }, McpToolErrorCodeEnum.Forbidden).ConfigureAwait(false);
                    await ErrorAsync(b, "update_model_profile", new { id = profile.Id, notes = "x" }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "E2E: pipeline reliability over REST and MCP", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Mission> SeedAsync(E2EServerFixture fx, string tenantId, string userId, string model)
        {
            using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
            {
                Mission mission = await db.Missions.CreateAsync(new Mission { TenantId = tenantId, UserId = userId, Title = "Reliability " + model, Persona = "Worker", Status = MissionStatusEnum.Complete, EscalationCount = 1 }).ConfigureAwait(false);
                DateTime start = DateTime.UtcNow.AddMinutes(-20);
                await db.StageOutcomes.CreateAsync(new StageOutcome { TenantId = tenantId, MissionId = mission.Id, Persona = "Worker", Model = model, Attempt = 1, Disposition = StageDispositionEnum.Escalated, StartedUtc = start, EndedUtc = start.AddMinutes(3) }).ConfigureAwait(false);
                await db.StageOutcomes.CreateAsync(new StageOutcome { TenantId = tenantId, MissionId = mission.Id, Persona = "Worker", Model = model, Attempt = 2, Disposition = StageDispositionEnum.Completed, StartedUtc = start.AddMinutes(5), EndedUtc = start.AddMinutes(9) }).ConfigureAwait(false);
                return mission;
            }
        }

        private static async Task<T> GetAsync<T>(HttpClient c, string path)
        {
            HttpResponseMessage response = await c.GetAsync(path).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.OK, response, path);
            return await JsonHelper.DeserializeAsync<T>(response).ConfigureAwait(false);
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
