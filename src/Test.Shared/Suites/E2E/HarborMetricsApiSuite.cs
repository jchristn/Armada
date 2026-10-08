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
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// GET /api/v1/harbors/{id}/metrics through a private Admiral with seeded Harbor records: the series come back for
    /// each range, a caller in another tenant gets 404, an unauthenticated caller 401, an unknown range 400, and token
    /// usage follows the token-usage scope (the Harbor's owner and a tenant admin see every record on the Harbor, another
    /// user only their own). The token-usage summary also filters by harborId.
    /// </summary>
    public sealed class HarborMetricsApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.HarborMetricsApi";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "metrics_over_rest",
                displayName: "Harbor metrics over REST: series, ranges, tenant scoping, token scoping, and errors",
                executeAsync: async (CancellationToken ct) =>
                {
                    string dataDir = TestTemp.NewDirectory("harbor_metrics_api");
                    DatabaseSettings dbSettings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(dataDir, "armada.db") };
                    using (InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(dataDir, dbSettings).ConfigureAwait(false))
                    {
                        E2ETenantUser tenantAdmin = await E2ETenantUser.CreateAsync(server.Client, "hm-admin", true).ConfigureAwait(false);
                        E2ETenantUser owner = await E2ETenantUser.CreateAsync(server.Client, "hm-owner", false, tenantAdmin.TenantId).ConfigureAwait(false);
                        E2ETenantUser member = await E2ETenantUser.CreateAsync(server.Client, "hm-member", false, tenantAdmin.TenantId).ConfigureAwait(false);
                        E2ETenantUser outsider = await E2ETenantUser.CreateAsync(server.Client, "hm-out", true).ConfigureAwait(false);

                        using HttpClient ownerClient = owner.CreateClient(server.BaseUrl);
                        using HttpClient memberClient = member.CreateClient(server.BaseUrl);
                        using HttpClient adminClient = tenantAdmin.CreateClient(server.BaseUrl);
                        using HttpClient outsiderClient = outsider.CreateClient(server.BaseUrl);
                        using HttpClient anonymous = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };

                        HttpResponseMessage created = await ownerClient.PostAsync("/api/v1/harbors", JsonHelper.ToJsonContent(new { Name = "Metrics Rig", MaxConcurrentJobs = 3 }), ct).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.Created, created, "owner registers a Harbor");
                        Harbor harbor = await JsonHelper.DeserializeAsync<Harbor>(created).ConfigureAwait(false);

                        DateTime now = DateTime.UtcNow;
                        await SeedAsync(server, harbor, owner, member, now).ConfigureAwait(false);

                        string path = "/api/v1/harbors/" + harbor.Id + "/metrics";
                        HttpResponseMessage hourResponse = await ownerClient.GetAsync(path + "?range=1h", ct).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.OK, hourResponse, "owner reads 1h");
                        HarborMetrics hour = await JsonHelper.DeserializeAsync<HarborMetrics>(hourResponse).ConfigureAwait(false);
                        AssertEqual(harbor.Id, hour.HarborId);
                        AssertEqual("1h", hour.Range);
                        AssertEqual(60, hour.BucketCount);
                        AssertEqual(60, hour.Jobs.Buckets.Count);
                        AssertEqual(1, hour.Jobs.MissionsFinished, "seeded mission finished");
                        AssertEqual(1, hour.Jobs.InteractiveFailed, "seeded Ask turn failed");
                        AssertEqual(1, hour.Jobs.Running, "seeded running job");
                        AssertEqual(3, hour.Slots.MaxConcurrentJobs);
                        AssertTrue(hour.Slots.Peak >= 1, "slot usage");
                        AssertEqual(1, hour.LaunchSpeed.Count, "one runtime ended");
                        AssertEqual("ClaudeCode", hour.LaunchSpeed[0].Runtime);
                        AssertEqual((long?)2500, hour.LaunchSpeed[0].FirstOutputMedianMs, "median of 2000 and 3000");
                        AssertEqual(HarborLinkSegmentStateEnum.Connected, hour.Link.Segments[hour.Link.Segments.Count - 1].State, "connected now");
                        AssertEqual((int?)1, hour.Link.ReconnectCount, "from the latest sample");
                        AssertTrue(hour.Link.RoundTrip.Any(b => b.AverageMs == 20.0), "seeded round trip");
                        AssertEqual(2, hour.Tokens.RecordCount, "the owner sees every token record on the Harbor");
                        AssertEqual(300L, hour.Tokens.TotalTokens);

                        HarborMetrics memberView = await JsonHelper.DeserializeAsync<HarborMetrics>(await memberClient.GetAsync(path + "?range=24h", ct).ConfigureAwait(false)).ConfigureAwait(false);
                        AssertEqual("24h", memberView.Range);
                        AssertEqual(48, memberView.BucketCount);
                        AssertEqual(1, memberView.Jobs.MissionsFinished, "jobs are visible to the tenant");
                        AssertEqual(1, memberView.Tokens.RecordCount, "another user sees only their own token records");
                        AssertEqual(100L, memberView.Tokens.TotalTokens);

                        HarborMetrics adminView = await JsonHelper.DeserializeAsync<HarborMetrics>(await adminClient.GetAsync(path + "?range=7d", ct).ConfigureAwait(false)).ConfigureAwait(false);
                        AssertEqual(56, adminView.BucketCount);
                        AssertEqual(180, adminView.BucketMinutes);
                        AssertEqual(2, adminView.Tokens.RecordCount, "a tenant admin sees every record on the Harbor");

                        HttpResponseMessage defaulted = await adminClient.GetAsync(path, ct).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.OK, defaulted, "range is optional");
                        AssertEqual("24h", (await JsonHelper.DeserializeAsync<HarborMetrics>(defaulted).ConfigureAwait(false)).Range, "24h by default");

                        AssertStatusCode(HttpStatusCode.NotFound, await outsiderClient.GetAsync(path + "?range=1h", ct).ConfigureAwait(false), "another tenant");
                        AssertStatusCode(HttpStatusCode.Unauthorized, await anonymous.GetAsync(path + "?range=1h", ct).ConfigureAwait(false), "unauthenticated");
                        AssertStatusCode(HttpStatusCode.BadRequest, await ownerClient.GetAsync(path + "?range=2w", ct).ConfigureAwait(false), "unknown range");
                        AssertStatusCode(HttpStatusCode.NotFound, await ownerClient.GetAsync("/api/v1/harbors/hbr_missing/metrics", ct).ConfigureAwait(false), "unknown Harbor");

                        HttpResponseMessage summary = await adminClient.GetAsync("/api/v1/token-usage/summary?harborId=" + harbor.Id + "&fromUtc=" + Uri.EscapeDataString(now.AddHours(-2).ToString("o")), ct).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.OK, summary, "token usage filtered by Harbor");
                        TokenUsageSummaryResult byHarbor = await JsonHelper.DeserializeAsync<TokenUsageSummaryResult>(summary).ConfigureAwait(false);
                        AssertEqual(2, byHarbor.RecordCount, "only the Harbor's records");
                    }
                },
                tags: new List<string> { TestTags.Positive, TestTags.EndToEnd }));

            return new TestSuiteDescriptor(SuiteId, "Harbor Metrics API", cases);
        }

        #endregion

        #region Private-Methods

        private static async Task SeedAsync(InProcessArmadaServer server, Harbor harbor, E2ETenantUser owner, E2ETenantUser member, DateTime now)
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            using (DatabaseDriver db = DatabaseDriverFactory.Create(server.Settings.Database, quiet))
            {
                await db.InitializeAsync().ConfigureAwait(false);

                await db.HarborJobs.CreateAsync(new HarborJobRecord
                {
                    JobId = "job_e2e_ok", HarborId = harbor.Id, TenantId = owner.TenantId, Kind = HarborJobKindEnum.Mission, Runtime = "ClaudeCode",
                    LaunchedUtc = now.AddMinutes(-20), StartedUtc = now.AddMinutes(-20), EndedUtc = now.AddMinutes(-10),
                    TimeToFirstOutputMs = 2000, DurationMs = 600000, ExitCode = 0, Outcome = HarborJobOutcomeEnum.Succeeded
                }).ConfigureAwait(false);
                await db.HarborJobs.CreateAsync(new HarborJobRecord
                {
                    JobId = "job_e2e_fail", HarborId = harbor.Id, TenantId = owner.TenantId, Kind = HarborJobKindEnum.AskTurn, Runtime = "ClaudeCode",
                    LaunchedUtc = now.AddMinutes(-15), StartedUtc = now.AddMinutes(-15), EndedUtc = now.AddMinutes(-14),
                    TimeToFirstOutputMs = 3000, DurationMs = 60000, ExitCode = 1, Outcome = HarborJobOutcomeEnum.Failed
                }).ConfigureAwait(false);
                await db.HarborJobs.CreateAsync(new HarborJobRecord
                {
                    JobId = "job_e2e_run", HarborId = harbor.Id, TenantId = owner.TenantId, Kind = HarborJobKindEnum.Mission, Runtime = "Codex",
                    LaunchedUtc = now.AddMinutes(-5), StartedUtc = now.AddMinutes(-5), Outcome = HarborJobOutcomeEnum.Running
                }).ConfigureAwait(false);

                await db.HarborLinkEvents.CreateAsync(new HarborLinkEvent { HarborId = harbor.Id, EventType = HarborLinkEventTypeEnum.Connected, OccurredUtc = now.AddMinutes(-30) }).ConfigureAwait(false);
                await db.HarborLinkSamples.CreateAsync(new HarborLinkSample
                {
                    HarborId = harbor.Id, BucketStartUtc = Armada.Core.Metrics.TimeBucketMath.Floor(now.AddMinutes(-3), TimeSpan.FromMinutes(1)),
                    HeartbeatCount = 4, RoundTripCount = 2, RoundTripTotalMs = 40, RoundTripMaxMs = 25, ReconnectCount = 1, LastReconnectUtc = now.AddMinutes(-30)
                }).ConfigureAwait(false);

                await db.TokenUsage.CreateAsync(new TokenUsageRecord { TenantId = owner.TenantId, UserId = owner.UserId, Model = "sonnet", Runtime = "ClaudeCode", Source = "mission", HarborId = harbor.Id, InputTokens = 150, OutputTokens = 50, TotalTokens = 200, CreatedUtc = now.AddMinutes(-10) }).ConfigureAwait(false);
                await db.TokenUsage.CreateAsync(new TokenUsageRecord { TenantId = owner.TenantId, UserId = member.UserId, Model = "sonnet", Runtime = "ClaudeCode", Source = "chat", HarborId = harbor.Id, InputTokens = 80, OutputTokens = 20, TotalTokens = 100, CreatedUtc = now.AddMinutes(-14) }).ConfigureAwait(false);
                await db.TokenUsage.CreateAsync(new TokenUsageRecord { TenantId = owner.TenantId, UserId = owner.UserId, Model = "sonnet", Runtime = "ClaudeCode", Source = "chat", InputTokens = 999, TotalTokens = 999, CreatedUtc = now.AddMinutes(-14) }).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
