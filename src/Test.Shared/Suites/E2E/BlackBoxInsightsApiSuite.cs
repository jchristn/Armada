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
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Black box insights over REST and MCP on a live server: authentication, dispatch overlap warnings and chaining on
    /// REST mission and voyage create and on MCP create_mission and dispatch, serializeOverlappingMissions on
    /// update_vessel, the read routes and tools with their 400 and 404 answers.
    /// </summary>
    public sealed class BlackBoxInsightsApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.BlackBoxInsightsApi";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("unauthenticated_rejected", "Insight routes need authentication", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Unauthorized, (await fx.UnauthClient.GetAsync("/api/v1/blackbox/recovery-stats").ConfigureAwait(false)).StatusCode);
                AssertEqual(HttpStatusCode.Unauthorized, (await fx.UnauthClient.PostAsync("/api/v1/missions/lint-brief", JsonHelper.ToJsonContent(new { Title = "x" })).ConfigureAwait(false)).StatusCode);
            }));

            cases.Add(CaseAsync("rest_dispatch_warns_and_chains", "REST mission create returns OverlapWarnings and chains with serializeOverlappingMissions; voyage create returns its missions' warnings; the preview agrees", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(fx).ConfigureAwait(false);
                Mission first = await CreateMissionAsync(fx, vessel.Id, "Touch only src/insight/a.cs").ConfigureAwait(false);
                Mission second = await CreateMissionAsync(fx, vessel.Id, "Touch only src/insight/a.cs").ConfigureAwait(false);
                AssertNotNull(second.OverlapWarnings, "warnings on the create response");
                AssertEqual(first.Id, second.OverlapWarnings!.Single().OverlappingMissionId);
                AssertNull(second.DependsOnMissionId, "not chained by default");

                vessel.SerializeOverlappingMissions = true;
                HttpResponseMessage put = await fx.AuthClient.PutAsync("/api/v1/vessels/" + vessel.Id, JsonHelper.ToJsonContent(vessel)).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, put.StatusCode);
                Mission third = await CreateMissionAsync(fx, vessel.Id, "Touch only src/insight/a.cs").ConfigureAwait(false);
                AssertNotNull(third.DependsOnMissionId, "chained after an overlapping mission");
                AssertTrue(third.OverlapWarnings!.Any(w => w.Serialized), "chained overlap marked");

                HttpResponseMessage voyage = await fx.AuthClient.PostAsync("/api/v1/voyages", JsonHelper.ToJsonContent(new
                {
                    Title = "Insight voyage",
                    VesselId = vessel.Id,
                    Missions = new[] { new { Title = "V1", Description = "Touch only src/insight/a.cs" } }
                })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, voyage.StatusCode);
                Voyage created = await JsonHelper.DeserializeAsync<Voyage>(voyage).ConfigureAwait(false);
                AssertNotNull(created.OverlapWarnings, "voyage warnings");

                HttpResponseMessage preview = await fx.AuthClient.PostAsync("/api/v1/vessels/" + vessel.Id + "/overlap-preview", JsonHelper.ToJsonContent(new { Description = "Touch only src/insight/a.cs" })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, preview.StatusCode);
                MissionOverlapPreview p = await JsonHelper.DeserializeAsync<MissionOverlapPreview>(preview).ConfigureAwait(false);
                AssertTrue(p.WarningCount >= 3, "every active overlapping mission"); AssertTrue(p.SerializeOverlappingMissions); AssertNotNull(p.SuggestedDependsOnMissionId);
            }));

            cases.Add(CaseAsync("rest_reads_and_errors", "Reservations, doctor, lint, stats, diff, nearby successes, timeline and cascade answer 200, 400 and 404 as documented", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(fx).ConfigureAwait(false);
                string v = "/api/v1/vessels/" + vessel.Id;
                AssertEqual(HttpStatusCode.OK, (await fx.AuthClient.GetAsync(v + "/reservations?pageSize=3").ConfigureAwait(false)).StatusCode, "step 1");
                AssertEqual(HttpStatusCode.OK, (await fx.AuthClient.PostAsync(v + "/reservations/enumerate", JsonHelper.ToJsonContent(new { PageSize = 2 })).ConfigureAwait(false)).StatusCode, "step 2");
                AssertEqual(HttpStatusCode.NotFound, (await fx.AuthClient.GetAsync("/api/v1/vessels/vsl_missing/reservations").ConfigureAwait(false)).StatusCode, "step 3");
                HttpResponseMessage doctor = await fx.AuthClient.GetAsync(v + "/blackbox-doctor?windowDays=10").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, doctor.StatusCode);
                AssertEqual(10, (await JsonHelper.DeserializeAsync<BlackBoxDoctorPage>(doctor).ConfigureAwait(false)).WindowDays);
                AssertEqual(HttpStatusCode.BadRequest, (await fx.AuthClient.GetAsync(v + "/blackbox-doctor?windowDays=0").ConfigureAwait(false)).StatusCode, "step 4");
                AssertEqual(HttpStatusCode.BadRequest, (await fx.AuthClient.GetAsync(v + "/blackbox-doctor?windowDays=abc").ConfigureAwait(false)).StatusCode, "step 5");
                AssertEqual(HttpStatusCode.OK, (await fx.AuthClient.PostAsync(v + "/blackbox-doctor/enumerate", JsonHelper.ToJsonContent(new { WindowDays = 5 })).ConfigureAwait(false)).StatusCode, "step 6");
                AssertEqual(HttpStatusCode.BadRequest, (await fx.AuthClient.PostAsync("/api/v1/missions/lint-brief", new StringContent("{ nope", System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(false)).StatusCode, "step 7");
                HttpResponseMessage lint = await fx.AuthClient.PostAsync("/api/v1/missions/lint-brief", JsonHelper.ToJsonContent(new { Description = "Verify that the tests pass." })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, lint.StatusCode);
                AssertEqual(MissionBriefLinter.NoOpRule, (await JsonHelper.DeserializeAsync<BriefLintResult>(lint).ConfigureAwait(false)).Findings.Single().RuleId);
                AssertEqual(HttpStatusCode.OK, (await fx.AuthClient.GetAsync("/api/v1/blackbox/recovery-stats?ruleId=BBX-LAND-001").ConfigureAwait(false)).StatusCode, "step 8");
                AssertEqual(HttpStatusCode.OK, (await fx.AuthClient.PostAsync("/api/v1/blackbox/recovery-stats/enumerate", JsonHelper.ToJsonContent(new { RuleId = "BBX-LAND-001" })).ConfigureAwait(false)).StatusCode, "step 9");

                string entryId = await SeedEntryAsync(fx, vessel).ConfigureAwait(false);
                string e = "/api/v1/blackbox/" + entryId;
                AssertEqual(HttpStatusCode.OK, (await fx.AuthClient.GetAsync(e + "/timeline").ConfigureAwait(false)).StatusCode, "step 10");
                AssertEqual(HttpStatusCode.OK, (await fx.AuthClient.PostAsync(e + "/timeline/enumerate", JsonHelper.ToJsonContent(new { PageSize = 2 })).ConfigureAwait(false)).StatusCode, "step 11");
                AssertEqual(HttpStatusCode.OK, (await fx.AuthClient.GetAsync(e + "/nearby-successes").ConfigureAwait(false)).StatusCode, "step 12");
                AssertEqual(HttpStatusCode.OK, (await fx.AuthClient.PostAsync(e + "/nearby-successes/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).StatusCode, "step 13");
                AssertEqual(HttpStatusCode.BadRequest, (await fx.AuthClient.GetAsync(e + "/diff/" + entryId).ConfigureAwait(false)).StatusCode, "step 14");
                AssertEqual(HttpStatusCode.NotFound, (await fx.AuthClient.GetAsync(e + "/diff/bbx_missing").ConfigureAwait(false)).StatusCode, "step 15");
                AssertEqual(HttpStatusCode.NotFound, (await fx.AuthClient.GetAsync("/api/v1/blackbox/bbx_missing/timeline").ConfigureAwait(false)).StatusCode, "step 16");
                AssertEqual(HttpStatusCode.NotFound, (await fx.AuthClient.GetAsync("/api/v1/voyages/vyg_missing/blackbox/cascade").ConfigureAwait(false)).StatusCode, "step 17");
            }));

            cases.Add(CaseAsync("mcp_tools", "The MCP tools answer, carry dispatch warnings on create_mission and dispatch, and update_vessel sets serializeOverlappingMissions", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(fx).ConfigureAwait(false);
                string updated = await ToolAsync(fx, "update_vessel", new { vesselId = vessel.Id, serializeOverlappingMissions = true }).ConfigureAwait(false);
                AssertContains("\"SerializeOverlappingMissions\":true", updated.Replace(" ", ""), "update_vessel");
                await ToolAsync(fx, "create_mission", new { title = "MCP one", description = "Touch only src/mcp/a.cs", vesselId = vessel.Id }).ConfigureAwait(false);
                string second = await ToolAsync(fx, "create_mission", new { title = "MCP two", description = "Touch only src/mcp/a.cs", vesselId = vessel.Id }).ConfigureAwait(false);
                AssertContains("OverlapWarnings", second, "create_mission warnings");
                AssertContains("\"Serialized\":true", second.Replace(" ", ""), "create_mission chained");
                string dispatched = await ToolAsync(fx, "dispatch", new { title = "MCP voyage", vesselId = vessel.Id, missions = new[] { new { title = "D1", description = "Touch only src/mcp/a.cs" } } }).ConfigureAwait(false);
                AssertContains("OverlapWarnings", dispatched, "dispatch warnings");

                AssertContains("SuggestedDependsOnMissionId", await ToolAsync(fx, "preview_mission_overlap", new { vesselId = vessel.Id, description = "Touch only src/mcp/a.cs" }).ConfigureAwait(false));
                AssertContains("NotFound", await ToolAsync(fx, "preview_mission_overlap", new { vesselId = "vsl_missing", title = "x" }).ConfigureAwait(false));
                AssertContains("InvalidArgument", await ToolAsync(fx, "preview_mission_overlap", new { vesselId = vessel.Id }).ConfigureAwait(false));
                AssertContains("Threshold", await ToolAsync(fx, "list_vessel_reservations", new { vesselId = vessel.Id }).ConfigureAwait(false));
                AssertContains(MissionBriefLinter.DependencyRule, await ToolAsync(fx, "lint_mission_brief", new { description = "This depends on mission 3." }).ConfigureAwait(false));
                AssertContains("WindowDays", await ToolAsync(fx, "get_vessel_blackbox_doctor", new { vesselId = vessel.Id, windowDays = 3 }).ConfigureAwait(false));
                AssertContains("InvalidArgument", await ToolAsync(fx, "get_vessel_blackbox_doctor", new { vesselId = vessel.Id, windowDays = 999 }).ConfigureAwait(false));
                AssertContains("TotalRecords", await ToolAsync(fx, "get_blackbox_recovery_stats", new { }).ConfigureAwait(false));
                string entryId = await SeedEntryAsync(fx, vessel).ConfigureAwait(false);
                AssertContains("entry.captured", await ToolAsync(fx, "get_blackbox_timeline", new { entryId = entryId, order = "CreatedDescending" }).ConfigureAwait(false));
                AssertContains("TotalRecords", await ToolAsync(fx, "list_blackbox_nearby_successes", new { entryId = entryId }).ConfigureAwait(false));
                AssertContains("NotFound", await ToolAsync(fx, "diff_blackbox_entries", new { entryId = entryId, otherEntryId = "bbx_missing" }).ConfigureAwait(false));
                AssertContains("NotFound", await ToolAsync(fx, "get_voyage_blackbox_cascade", new { voyageId = "vyg_missing" }).ConfigureAwait(false));
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box insights over REST and MCP", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Vessel> CreateVesselAsync(E2EServerFixture fx)
        {
            string name = "insight-e2e-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            HttpResponseMessage created = await fx.AuthClient.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new { Name = name, RepoUrl = "https://github.com/test/" + name })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, created.StatusCode, "vessel created");
            return await JsonHelper.DeserializeAsync<Vessel>(created).ConfigureAwait(false);
        }

        private static async Task<Mission> CreateMissionAsync(E2EServerFixture fx, string vesselId, string description)
        {
            HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/missions", JsonHelper.ToJsonContent(new { Title = "Insight " + Guid.NewGuid().ToString("N").Substring(0, 6), Description = description, VesselId = vesselId })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, response.StatusCode, "mission created");
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
            return wrapper.Mission ?? JsonHelper.Deserialize<Mission>(body);
        }

        private static async Task<string> SeedEntryAsync(E2EServerFixture fx, Vessel vessel)
        {
            BlackBoxRuntime runtime = fx.Server.BlackBox ?? throw new InvalidOperationException("no black box runtime");
            Mission mission;
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            using (DatabaseDriver db = DatabaseDriverFactory.Create(new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") }, quiet))
            {
                await db.InitializeAsync().ConfigureAwait(false);
                mission = new Mission("Insight failure " + Guid.NewGuid().ToString("N").Substring(0, 6), "Fix it");
                mission.VesselId = vessel.Id;
                mission.TenantId = vessel.TenantId;
                mission.UserId = vessel.UserId;
                mission.Status = MissionStatusEnum.Failed;
                mission.FailureKind = MissionFailureKindEnum.ScopeViolation;
                mission = await db.Missions.CreateAsync(mission).ConfigureAwait(false);
            }
            string id = (await runtime.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation, c => c.OutOfScopeFiles.Add("lib/x.cs")).ConfigureAwait(false))!;
            await runtime.Queue.DrainAsync().ConfigureAwait(false);
            return id;
        }

        private static async Task<string> ToolAsync(E2EServerFixture fx, string tool, object arguments)
        {
            using HttpClient mcp = new HttpClient();
            mcp.BaseAddress = fx.McpClient.BaseAddress;
            mcp.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
            string rpc = await McpTestCalls.CallToolAsync(mcp, tool, arguments).ConfigureAwait(false);
            E2eMcpToolEnvelope envelope = JsonHelper.Deserialize<E2eMcpToolEnvelope>(rpc);
            return envelope.Result?.Content.FirstOrDefault()?.Text ?? rpc;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag, TestTags.EndToEnd });
        }

        #endregion
    }
}
