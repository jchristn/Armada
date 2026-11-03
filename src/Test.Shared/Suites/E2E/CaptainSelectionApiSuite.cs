namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Server;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Real authenticated HTTP contracts for live policy and durable scoped selection explanations.</summary>
    public sealed class CaptainSelectionApiSuite : IArmadaTestSuite
    {
        private const string _SuiteId = "E2E.CaptainSelectionApi";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(_SuiteId, "Captain selection API", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(_SuiteId, "live_strategy_settings_revision_and_authorization", "Live configurable strategy survives reload and rejects stale and unauthorized updates", async token =>
                {
                    string directory = TestTemp.NewDirectory("selection-settings-api");
                    using InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(directory,
                        new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(directory, "armada.db") }).ConfigureAwait(false);
                    UsageSelectionConfiguration initial = new UsageSelectionConfiguration();
                    using (HttpResponseMessage response = await server.Client.GetAsync("/api/v1/settings", token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        Armada.Client.Models.SettingsData settings = await JsonHelper.DeserializeAsync<Armada.Client.Models.SettingsData>(response).ConfigureAwait(false);
                        initial.Revision = settings.UsageSelectionRevision;
                        AssertEqual(CaptainSelectionStrategyEnum.FirstAvailable, settings.UsageSelection!.Strategy);
                    }
                    UsageSelectionSettings policy = new UsageSelectionSettings { Strategy = CaptainSelectionStrategyEnum.MostUsedAllowance };
                    using (HttpResponseMessage response = await server.Client.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { UsageSelection = policy, ExpectedUsageSelectionRevision = initial.Revision }), token).ConfigureAwait(false))
                        AssertStatusCode(HttpStatusCode.OK, response);
                    using HttpClient mcp = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + server.Settings.McpPort) };
                    mcp.DefaultRequestHeaders.Add("X-Api-Key", server.Settings.ApiKey);
                    string configuration = await McpTestCalls.CallToolAsync(mcp, "get_usage_selection_settings", new { }).ConfigureAwait(false);
                    AssertContains("MostUsedAllowance", configuration);
                    AssertContains("MostRemainingAllowance", configuration);
                    string conflict = await McpTestCalls.CallToolAsync(mcp, "update_usage_selection_settings", new { settings = policy, expectedRevision = initial.Revision }).ConfigureAwait(false);
                    AssertContains("RevisionConflict", conflict);
                    AssertEqual(CaptainSelectionStrategyEnum.MostUsedAllowance, server.Settings.EffectiveUsageSelection.Strategy);
                    AssertEqual(CaptainSelectionStrategyEnum.MostUsedAllowance, (await ArmadaSettings.LoadAsync(server.Settings.EffectiveSettingsFilePath).ConfigureAwait(false)).UsageSelection.Strategy);
                    using (HttpResponseMessage response = await server.Client.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { UsageSelection = policy, ExpectedUsageSelectionRevision = initial.Revision }), token).ConfigureAwait(false))
                        AssertStatusCode(HttpStatusCode.Conflict, response);
                    policy.Mode = UsageSelectionModeEnum.Enforce;
                    using (HttpResponseMessage response = await server.Client.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { UsageSelection = policy }), token).ConfigureAwait(false))
                        AssertStatusCode(HttpStatusCode.Conflict, response);
                    E2ETenantUser tenant = await E2ETenantUser.CreateAsync(server.Client, "selection-settings").ConfigureAwait(false);
                    using HttpClient caller = tenant.CreateClient(server.BaseUrl);
                    using (HttpResponseMessage response = await caller.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { UsageSelection = new UsageSelectionSettings() }), token).ConfigureAwait(false))
                        AssertStatusCode(HttpStatusCode.Forbidden, response);
                }),
                new TestCaseDescriptor(_SuiteId, "history_scope_redaction_and_preview_has_no_writes", "History scopes counts and redacts account keys; preview never writes or claims", async token =>
                {
                    string directory = TestTemp.NewDirectory("selection-history-api");
                    using InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(directory,
                        new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(directory, "armada.db") }).ConfigureAwait(false);
                    ArmadaServer runtime = (ArmadaServer)typeof(InProcessArmadaServer).GetField("_Server", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(server)!;
                    DatabaseDriver database = (DatabaseDriver)typeof(ArmadaServer).GetField("_Database", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runtime)!;
                    E2ETenantUser owner = await E2ETenantUser.CreateAsync(server.Client, "selection-owner", false).ConfigureAwait(false);
                    E2ETenantUser peer = await E2ETenantUser.CreateAsync(server.Client, "selection-peer", false, owner.TenantId).ConfigureAwait(false);
                    E2ETenantUser foreign = await E2ETenantUser.CreateAsync(server.Client, "selection-foreign").ConfigureAwait(false);
                    using HttpClient caller = owner.CreateClient(server.BaseUrl);
                    using HttpClient peerClient = peer.CreateClient(server.BaseUrl);
                    using HttpClient foreignClient = foreign.CreateClient(server.BaseUrl);
                    Mission mission = await database.Missions.CreateAsync(new Mission("selection history") { TenantId = owner.TenantId, UserId = owner.UserId }, token).ConfigureAwait(false);
                    CaptainSelectionRecordSet records = new CaptainSelectionRecordSet { Policy = CaptainSelectionPolicyRevision.FromSettings(owner.TenantId, new UsageSelectionSettings { Strategy = CaptainSelectionStrategyEnum.MostRemainingAllowance }) };
                    records.Decision.TenantId = owner.TenantId; records.Decision.UserId = owner.UserId; records.Decision.MissionId = mission.Id;
                    records.Decision.ExecutionAttemptId = records.Decision.Id; records.Decision.PolicyRevisionId = records.Policy.Id;
                    records.Decision.Mode = UsageSelectionModeEnum.Observe; records.Decision.Outcome = "Waiting"; records.Decision.Trigger = "Waiting";
                    records.Decision.EvaluatedUtc = DateTime.UtcNow; records.Decision.AlgorithmVersion = UsageSelectionPolicy.AlgorithmVersion; records.Decision.PrimaryReasonCode = "UsageUnknown";
                    records.Decision.CandidateCount = 2;
                    records.Candidates.Add(new CaptainSelectionCandidateRecord { TenantId = owner.TenantId, UserId = owner.UserId, DecisionId = records.Decision.Id, CaptainId = "visible", CaptainName = "Historical captain", AccountKey = "private-account-key", Rank = 1 });
                    records.Candidates.Add(new CaptainSelectionCandidateRecord { TenantId = owner.TenantId, UserId = peer.UserId, DecisionId = records.Decision.Id, CaptainId = "hidden", CaptainName = "Hidden peer", Rank = 2 });
                    await database.CaptainSelections.CreateDecisionAsync(records, token).ConfigureAwait(false);
                    string path = "/api/v1/missions/" + mission.Id + "/captain-selection";
                    using Armada.Client.ArmadaClient sdk = new Armada.Client.ArmadaClient(new Armada.Client.ArmadaClientOptions(server.BaseUrl) { BearerToken = owner.BearerToken });
                    AssertTrue((await sdk.GetCaptainSelectionAsync(mission.Id, token).ConfigureAwait(false))!.HistoryAvailable);
                    AssertEqual(1L, (await sdk.GetCaptainSelectionHistoryAsync(mission.Id, token: token).ConfigureAwait(false))!.TotalRecords);
                    AssertEqual(1L, (await sdk.GetCaptainSelectionCandidatesAsync(records.Decision.Id, token: token).ConfigureAwait(false))!.TotalRecords);

                    using (HttpResponseMessage response = await caller.GetAsync(path, token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        CaptainSelectionSummary summary = await JsonHelper.DeserializeAsync<CaptainSelectionSummary>(response).ConfigureAwait(false);
                        AssertTrue(summary.HistoryAvailable); AssertNull(summary.CurrentAttempt);
                        AssertEqual(CaptainSelectionStrategyEnum.MostRemainingAllowance, summary.LatestDecision!.Strategy);
                    }
                    string detailPath = "/api/v1/captain-selection-decisions/" + records.Decision.Id;
                    using (HttpResponseMessage response = await caller.GetAsync(detailPath, token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                        AssertFalse(body.Contains("private-account-key", StringComparison.Ordinal)); AssertFalse(body.Contains("Hidden peer", StringComparison.Ordinal));
                        CaptainSelectionDecisionDetail detail = JsonHelper.Deserialize<CaptainSelectionDecisionDetail>(body);
                        AssertEqual(1L, detail.Candidates.TotalRecords);
                    }
                    foreach (HttpClient denied in new[] { peerClient, foreignClient })
                    {
                        using HttpResponseMessage response = await denied.GetAsync(path, token).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.NotFound, response);
                        using HttpResponseMessage detail = await denied.GetAsync(detailPath + "?tenantId=" + owner.TenantId, token).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.NotFound, detail);
                    }
                    using (HttpResponseMessage response = await caller.PostAsync(path + "/preview", JsonHelper.ToJsonContent(new { }), token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        AssertEqual("VesselMissing", (await JsonHelper.DeserializeAsync<CaptainSelectionPreview>(response).ConfigureAwait(false)).ReasonCode);
                    }
                    AssertEqual(1L, (await database.CaptainSelections.EnumerateByMissionAsync(owner.TenantId, mission.Id, owner.UserId, token: token).ConfigureAwait(false)).TotalRecords);
                    AssertEqual(MissionStatusEnum.Pending, (await database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false))!.Status);
                })
            });
        }
    }
}
