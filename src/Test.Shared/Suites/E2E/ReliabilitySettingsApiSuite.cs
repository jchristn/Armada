namespace Test.Shared.Suites.E2E
{
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using Armada.Core.Enums;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The 1.5.0 reliability settings over REST (RS-12.4): GET /api/v1/settings returns them with their defaults; PUT
    /// validates (out-of-range thresholds are 400 before anything changes), clamps the clamped ones, applies live and
    /// persists. Runs a private Admiral so the shared fixture's settings are never changed.
    /// </summary>
    public sealed class ReliabilitySettingsApiSuite : IArmadaTestSuite
    {
        private const string SuiteId = "E2E.ReliabilitySettingsApi";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "get_put_reliability_settings",
                displayName: "GET returns the 1.5.0 reliability defaults; PUT validates, clamps, applies live, and persists",
                executeAsync: async (CancellationToken ct) =>
                {
                    string dataDir = TestTemp.NewDirectory("reliability_settings_api");
                    DatabaseSettings db = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(dataDir, "armada.db") };
                    using (InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(dataDir, db).ConfigureAwait(false))
                    {
                        HttpResponseMessage get = await server.Client.GetAsync("/api/v1/settings", ct).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.OK, get, "GET settings");
                        ReliabilitySettingsEnvelope before = await JsonHelper.DeserializeAsync<ReliabilitySettingsEnvelope>(get).ConfigureAwait(false);
                        AssertEqual((int?)30, before.TimeoutScaleReferenceTokensPerSecond, "timeout scale default");
                        AssertEqual((int?)32768, before.PromptVariantCompactMaxContextTokens, "compact default");
                        AssertEqual((int?)65536, before.PromptVariantLeanMaxContextTokens, "lean default");
                        AssertEqual((int?)3, before.MergeQueueRequeueLimit, "requeue default");
                        AssertEqual((int?)1, before.MergeQueueBatchSize, "batch default");
                        AssertNotNull(before.RoutingData, "routing data returned");
                        AssertTrue(before.RoutingData!.RankByOutcomes, "ranking on by default");
                        AssertFalse(before.RoutingData.SuggestTiers, "tier suggestions off by default");

                        using (HttpResponseMessage bad = await server.Client.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { TimeoutScaleReferenceTokensPerSecond = 5000, MaxEscalations = 2 }), ct).ConfigureAwait(false))
                            AssertStatusCode(HttpStatusCode.BadRequest, bad, "out-of-range rate");
                        AssertEqual(30, server.Settings.TimeoutScaleReferenceTokensPerSecond, "refused PUT changes nothing");
                        using (HttpResponseMessage badLean = await server.Client.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { PromptVariantLeanMaxContextTokens = -1 }), ct).ConfigureAwait(false))
                            AssertStatusCode(HttpStatusCode.BadRequest, badLean, "negative lean threshold");

                        HttpResponseMessage put = await server.Client.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new
                        {
                            TimeoutScaleReferenceTokensPerSecond = 20,
                            PromptVariantCompactMaxContextTokens = 16384,
                            MergeQueueRequeueLimit = 99,
                            MergeQueueBatchSize = 4,
                            UnclassifiedResumeLimit = 2,
                            JudgeReworkRounds = 3,
                            MaxEscalations = 9,
                            EscalateOnOpenEndpointBreaker = true,
                            RoutingData = new { RankByOutcomes = false, SuggestTiers = true, MinimumSamples = 1, HalfLifeDays = 30 }
                        }), ct).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.OK, put, "PUT settings");
                        ReliabilitySettingsEnvelope after = await JsonHelper.DeserializeAsync<ReliabilitySettingsEnvelope>(put).ConfigureAwait(false);
                        AssertEqual((int?)20, after.TimeoutScaleReferenceTokensPerSecond, "rate updated");
                        AssertEqual((int?)16384, after.PromptVariantCompactMaxContextTokens, "compact updated");
                        AssertEqual((int?)65536, after.PromptVariantLeanMaxContextTokens, "omitted value unchanged");
                        AssertEqual((int?)10, after.MergeQueueRequeueLimit, "requeue clamped to 10");
                        AssertEqual((int?)4, after.MergeQueueBatchSize, "batch updated");
                        AssertEqual((int?)5, after.MaxEscalations, "escalations clamped to 5");
                        AssertEqual((bool?)true, after.EscalateOnOpenEndpointBreaker, "breaker escalation on");
                        AssertFalse(after.RoutingData!.RankByOutcomes, "ranking off");
                        AssertEqual(5, after.RoutingData.MinimumSamples, "minimum samples clamped to 5");
                        AssertEqual(16384, server.Settings.PromptVariantCompactMaxContextTokens, "applied live");
                        AssertEqual(30, server.Settings.RoutingData.HalfLifeDays, "routing data applied live");

                        ArmadaSettings reloaded = await ArmadaSettings.LoadAsync(Path.Combine(dataDir, "settings.json")).ConfigureAwait(false);
                        AssertEqual(20, reloaded.TimeoutScaleReferenceTokensPerSecond, "persisted rate");
                        AssertEqual(4, reloaded.MergeQueueBatchSize, "persisted batch");
                        AssertTrue(reloaded.RoutingData.SuggestTiers, "persisted routing data");
                    }
                },
                tags: new List<string> { TestTags.Positive, TestTags.EndToEnd }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Reliability Settings API", cases: cases);
        }
    }
}
