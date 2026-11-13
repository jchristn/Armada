namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The optional model-written summary (BlackBox.SummaryEndpointId): used and labeled when the endpoint answers,
    /// silently replaced by the rules summary when it fails, times out, answers empty, or is not configured; and the
    /// model endpoint writer against a fake chat endpoint.
    /// </summary>
    public sealed class BlackBoxSummarySuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxSummary";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("model_summary_and_fallback", "A configured summary writer's text becomes the model-written summary; failure, timeout, empty text or no endpoint keep the rules summary", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                ScriptedBlackBoxSummaryWriter writer = new ScriptedBlackBoxSummaryWriter();
                bb.Pipeline.SummaryWriter = writer;

                BlackBoxRecord none = await AnalyzeAsync(t, bb).ConfigureAwait(false);
                AssertEqual(0, writer.Calls, "not called without an endpoint id");
                AssertFalse(none.Analysis!.SummaryIsModelWritten, "rules summary");

                settings.BlackBox.SummaryEndpointId = "mep_fake";
                BlackBoxRecord modeled = await AnalyzeAsync(t, bb).ConfigureAwait(false);
                AssertEqual(1, writer.Calls); AssertEqual("mep_fake", writer.LastEndpointId);
                AssertTrue(modeled.Analysis!.SummaryIsModelWritten, "labeled model-written");
                AssertEqual("A model wrote this summary.", modeled.Analysis.Summary);
                AssertTrue(modeled.Analysis.Why.Any(w => w.RuleId == "BBX-LAND-001"), "rule findings stay authoritative");

                writer.Throw = true;
                BlackBoxRecord thrown = await AnalyzeAsync(t, bb).ConfigureAwait(false);
                AssertFalse(thrown.Analysis!.SummaryIsModelWritten, "a throwing writer falls back");
                AssertContains("uncommitted tracked changes", thrown.Analysis.Summary ?? "", "rules summary kept");

                writer.Throw = false;
                writer.Text = "   ";
                AssertFalse((await AnalyzeAsync(t, bb).ConfigureAwait(false)).Analysis!.SummaryIsModelWritten, "empty text falls back");

                writer.Text = "late";
                writer.DelayMs = 5000;
                bb.Pipeline.SummaryTimeoutMs = 1000;
                DateTime start = DateTime.UtcNow;
                BlackBoxRecord slow = await AnalyzeAsync(t, bb).ConfigureAwait(false);
                AssertFalse(slow.Analysis!.SummaryIsModelWritten, "a timeout falls back");
                AssertTrue(DateTime.UtcNow - start < TimeSpan.FromSeconds(4), "the timeout cut the wait");

                string brief = await bb.Service.GetBriefAsync(BlackBoxTestKit.Admin(), slow.EntryId).ConfigureAwait(false);
                AssertFalse(brief.Contains("(model-written)"), "brief does not label a rules summary");
            }));

            cases.Add(Case("model_endpoint_writer", "The model endpoint writer calls the configured endpoint with the record facts and findings, and returns null on an HTTP error, a disabled or missing endpoint", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using FakeChatEndpoint fake = new FakeChatEndpoint();
                ModelEndpoint endpoint = await t.Driver.ModelEndpoints.CreateAsync(new ModelEndpoint { Name = "summary-" + Guid.NewGuid().ToString("N").Substring(0, 6), Provider = ModelProviderEnum.OpenAICompatible, BaseUrl = fake.BaseUrl, Model = "fake-model" }).ConfigureAwait(false);
                ModelEndpointBlackBoxSummaryWriter writer = new ModelEndpointBlackBoxSummaryWriter(t.Driver, BlackBoxTestKit.Logging());
                BlackBoxRecord record = new BlackBoxRecord
                {
                    EntryId = "bbx_s",
                    Subject = new BlackBoxSubject { MissionId = "msn_s", Title = "Summary mission", VesselName = "V" },
                    Site = new BlackBoxSite { Phase = BlackBoxPhaseEnum.Landing, Step = BlackBoxStepEnum.DirtyCheckout },
                    Classification = new BlackBoxClassification { Kind = MissionFailureKindEnum.Infra, RawReason = "Landing refused: dirty checkout" },
                    Workspace = new BlackBoxWorkspace { CheckoutPath = "/work/c", CheckoutDirty = true, DirtyPaths = new List<string> { "a.cs" }, DirtyPathTotal = 1 }
                };
                BlackBoxAnalysis analysis = BlackBoxAdvisor.Analyze(record, BlackBoxRuleCatalog.Default);
                string? text = await writer.WriteSummaryAsync(endpoint.Id, record, analysis).ConfigureAwait(false);
                AssertEqual(fake.Reply, text, "the model's text: " + String.Join(", ", fake.Requests));
                AssertTrue(fake.Requests.Count >= 1, "endpoint called");
                AssertContains("BBX-LAND-001", fake.Bodies.Last(), "the prompt carries the findings");
                AssertContains("msn_s", fake.Bodies.Last(), "the prompt carries the subject");
                AssertContains("no em-dashes", writer.BuildPrompt(record, analysis));

                fake.Status = 500;
                AssertNull(await writer.WriteSummaryAsync(endpoint.Id, record, analysis).ConfigureAwait(false), "HTTP error returns null");
                fake.Status = 200;
                AssertNull(await writer.WriteSummaryAsync("mep_missing", record, analysis).ConfigureAwait(false), "missing endpoint");
                endpoint.Enabled = false;
                await t.Driver.ModelEndpoints.UpdateAsync(endpoint).ConfigureAwait(false);
                AssertNull(await writer.WriteSummaryAsync(endpoint.Id, record, analysis).ConfigureAwait(false), "disabled endpoint");
                AssertNull(await writer.WriteSummaryAsync(" ", record, analysis).ConfigureAwait(false), "blank id");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box model-written summary", cases: cases);
        }

        private static async Task<BlackBoxRecord> AnalyzeAsync(TestDatabase t, BlackBoxRuntime bb)
        {
            Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Infra, "Landing refused: dirty checkout", MissionStatusEnum.LandingFailed).ConfigureAwait(false);
            string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.DirtyCheckout, c =>
            {
                c.GitFacts = new BlackBoxGitFacts { CheckoutPath = "/work/checkout" };
                c.GitFacts.DirtyPaths.Add("src/a.cs");
            }).ConfigureAwait(false))!;
            BlackBoxEntry entry = (await bb.Pipeline.ProcessAsync(id).ConfigureAwait(false))!;
            return await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
