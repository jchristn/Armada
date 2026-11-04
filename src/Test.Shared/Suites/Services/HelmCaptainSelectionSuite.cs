namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Helm;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The Helm captain-selection commands against a recording Admiral stub: <c>mission selection</c> shows recovery
    /// permissions with the summary, and <c>mission selection-recovery</c> reads the current revision, posts the
    /// audited request, honors an explicit revision, refuses non-administrators before writing, and requires a reason.
    /// </summary>
    public sealed class HelmCaptainSelectionSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HelmCaptainSelection";
        private const string Token = "selection-token";
        private const string Summary = "GET /api/v1/missions/msn_1/captain-selection";
        private const string Recovery = "/api/v1/missions/msn_1/captain-selection/recovery";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("recovery_reads_revision_and_posts_audited_request", "selection-recovery reads the current revision, then posts flags and the trimmed reason", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies[Summary] = StubAdmiralReply.Json(SummaryJson(canManage: true, revision: 4));
                    stub.Replies["POST " + Recovery] = StubAdmiralReply.Json(ArmadaJson.Serialize(Record(5, true, true, "Preferred captain offline")));
                    HelmCliRun run = await Run(stub, "mission", "selection-recovery", "msn_1", "--allow-fallback", "--reserve-exception", "--reason", "  Preferred captain offline  ", "--yes").ConfigureAwait(false);
                    AssertEqual(0, run.ExitCode, run.StandardError + run.Output);
                    UsageSelectionRecoveryRequest sent = Body<UsageSelectionRecoveryRequest>(stub, "POST", Recovery);
                    AssertEqual(4, sent.ExpectedRevision, "current revision is used");
                    AssertTrue(sent.AllowFallback && sent.ReserveException, "flags");
                    AssertEqual("Preferred captain offline", sent.Reason, "reason trimmed");
                    AssertContains("Saved recovery revision 5", run.Output, "confirmation");
                }
            }));

            cases.Add(CaseAsync("recovery_explicit_revision_skips_read", "--expected-revision is sent as given without reading the summary", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["POST " + Recovery] = StubAdmiralReply.Json(ArmadaJson.Serialize(Record(8, false, false, "Revoke")));
                    HelmCliRun run = await Run(stub, "mission", "selection-recovery", "msn_1", "--expected-revision", "7", "--reason", "Revoke", "--json").ConfigureAwait(false);
                    AssertEqual(0, run.ExitCode, run.StandardError + run.Output);
                    AssertEqual(7, Body<UsageSelectionRecoveryRequest>(stub, "POST", Recovery).ExpectedRevision);
                    AssertFalse(stub.Requests().Any(r => r.Method == "GET" && r.Path == "/api/v1/missions/msn_1/captain-selection"), "no summary read");
                    UsageSelectionRecovery? saved = JsonSerializer.Deserialize<UsageSelectionRecovery>(run.StandardOutput, ArmadaJson.Options);
                    AssertEqual(8, saved?.Revision, "JSON alone on stdout");
                }
            }));

            cases.Add(CaseAsync("recovery_refused_for_viewers_and_without_reason", "A caller who cannot manage recovery gets no write; a missing reason is rejected", TestTags.Negative, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies[Summary] = StubAdmiralReply.Json(SummaryJson(canManage: false, revision: 0));
                    HelmCliRun viewer = await Run(stub, "mission", "selection-recovery", "msn_1", "--allow-fallback", "--reason", "x", "--yes").ConfigureAwait(false);
                    AssertEqual(1, viewer.ExitCode, "refused");
                    AssertFalse(stub.Requests().Any(r => r.Method == "POST"), "nothing written");
                    // Spectre settings validation surfaces as an exception from the captured harness, before any request.
                    string refusal;
                    try
                    {
                        HelmCliRun missing = await Run(stub, "mission", "selection-recovery", "msn_1", "--allow-fallback").ConfigureAwait(false);
                        refusal = missing.ExitCode != 0 ? missing.StandardError + missing.Output : "";
                    }
                    catch (Exception ex)
                    {
                        refusal = ex.Message;
                    }

                    AssertContains("--reason is required", refusal, "reason required");
                    AssertFalse(stub.Requests().Any(r => r.Method == "POST"), "still nothing written");
                }
            }));

            cases.Add(CaseAsync("summary_shows_recovery_permissions", "mission selection prints the recovery revision and permissions with the summary", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies[Summary] = StubAdmiralReply.Json(SummaryJson(canManage: true, revision: 2));
                    HelmCliRun run = await Run(stub, "mission", "selection", "msn_1").ConfigureAwait(false);
                    AssertEqual(0, run.ExitCode, run.StandardError + run.Output);
                    AssertContains("Recovery permissions revision 2", run.Output, "recovery line");
                    AssertContains("Fallback allowed", run.Output, "fallback state");
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Helm captain selection commands", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static UsageSelectionRecovery Record(int revision, bool fallback, bool reserve, string reason)
            => new UsageSelectionRecovery { Id = "ura_" + revision, MissionId = "msn_1", ActorUserId = "usr_admin", Reason = reason,
                AllowFallback = fallback, ReserveException = reserve, Revision = revision, CreatedUtc = DateTime.UtcNow };

        private static string SummaryJson(bool canManage, int revision)
            => ArmadaJson.Serialize(new CaptainSelectionSummary
            {
                MissionId = "msn_1", CurrentMode = UsageSelectionModeEnum.Enforce, CurrentStrategy = CaptainSelectionStrategyEnum.FirstAvailable,
                CanManageRecovery = canManage, Recovery = revision == 0 ? null : Record(revision, true, false, "Earlier approval")
            });

        private static T Body<T>(RecordingAdmiralStub stub, string method, string path) where T : class
        {
            List<T> bodies = stub.Requests().Where(r => r.Method == method && r.Path == path).Select(r => JsonSerializer.Deserialize<T>(r.Body, ArmadaJson.Options)!).ToList();
            AssertTrue(bodies.Count > 0, "a " + method + " " + path + " request");
            return bodies.Last();
        }

        private static Task<HelmCliRun> Run(RecordingAdmiralStub stub, params string[] args)
        {
            List<string> all = new List<string>(args) { "--server", stub.BaseUrl, "--token", Token };
            return HelmCliHarness.RunCapturedAsync(all.ToArray());
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
            => new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });

        #endregion
    }
}
