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
    using Armada.Helm.Commands;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The Armada resilience angle in Helm: <c>armada blackbox show</c> prints the Armada resilience section (in scope:
    /// self-healable verdict, detection gap, severity, code pointers, proposed changes, regression test; outside scope the
    /// short reason) and marks a suspected Armada defect, and <c>armada blackbox gaps</c> lists the resilience gaps with
    /// filters, paging capped at 50, <c>--json</c> and the black box exit codes.
    /// </summary>
    public sealed class HelmBlackBoxResilienceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HelmBlackBoxResilience";
        private const string Token = "bb-token";
        private const string GapsRoute = "/api/v1/blackbox/resilience-gaps/enumerate";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("show_resilience_in_scope", "show prints the Armada resilience section of an in-scope failure", TestTags.Positive, async () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load("land-002-dock-deleted", "bbx_hr1");
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["GET /api/v1/blackbox/" + fx.EntryId] = StubAdmiralReply.Json(fx.EntryJson());
                    HelmCliRun run = await Run(stub, "blackbox", "show", fx.EntryId).ConfigureAwait(false);
                    AssertEqual(0, run.ExitCode, run.StandardError);
                    string output = Flat(run.Output);
                    AssertContains("Armada resilience", output, "section header");
                    AssertContains("Self-healable:", output, "self-healable");
                    AssertContains("Armada could have recreated the worktree", output, "what Armada could have done");
                    AssertContains("Detection gap:", output, "detection gap");
                    AssertContains("Recreate a missing dock worktree", output, "top proposed change");
                    AssertContains("Regression test:", output, "regression test");
                    AssertContains("seen 1 time in 30 days", output, "recurrence");
                    AssertTrue(output.IndexOf("Armada resilience", StringComparison.Ordinal) < output.IndexOf("Recovery paths", StringComparison.Ordinal), "before the recovery paths");
                }
            }));

            cases.Add(CaseAsync("show_resilience_suspected_and_outside", "show marks a suspected Armada defect, and outside scope prints only the reason no Armada change is suggested", TestTags.Negative, async () =>
            {
                BlackBoxUiFixture suspected = BlackBoxUiFixture.Load("land-002-dock-deleted", "bbx_hr2");
                suspected.Entry.Record!.Analysis!.Triage!.Suspected = true;
                suspected.Entry.Record.Analysis.Triage.Verdict = BlackBoxTriageEnum.ArmadaDefect;
                BlackBoxUiFixture outside = BlackBoxUiFixture.Load("dep-001-parallel-dependency-mission-2", "bbx_hr3");
                BlackBoxResilience res = outside.Entry.Record!.Analysis!.Resilience!;
                res.InScope = false;
                res.ProposedChanges.Clear();
                res.NoChangeReason = "No Armada change suggested: the failure is outside Armada's handoff and landing.";
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["GET /api/v1/blackbox/" + suspected.EntryId] = StubAdmiralReply.Json(suspected.EntryJson());
                    stub.Replies["GET /api/v1/blackbox/" + outside.EntryId] = StubAdmiralReply.Json(outside.EntryJson());
                    HelmCliRun a = await Run(stub, "blackbox", "show", suspected.EntryId).ConfigureAwait(false);
                    AssertEqual(0, a.ExitCode, a.StandardError);
                    AssertContains("suspected Armada defect", Flat(a.Output), "triage marker");
                    AssertContains("Suspected Armada defect:", Flat(a.Output), "section marker");
                    HelmCliRun b = await Run(stub, "blackbox", "show", outside.EntryId).ConfigureAwait(false);
                    AssertEqual(0, b.ExitCode, b.StandardError);
                    string text = Flat(b.Output);
                    AssertContains("No Armada change suggested", text, "short reason");
                    AssertFalse(text.Contains("Self-healable:", StringComparison.Ordinal), "no full section outside scope");
                }
            }));

            cases.Add(CaseAsync("gaps_filters_paging_json", "gaps sends the filters and paging (page size capped at 50), --json prints the page alone, the table shows the top change", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["POST " + GapsRoute] = StubAdmiralReply.Json(GapsPage());
                    HelmCliRun json = await Run(stub, "blackbox", "gaps", "--vessel", "vsl_1", "--resolution", "open", "--phase", "landing", "--since", "30d", "--page", "2", "--page-size", "500", "--json").ConfigureAwait(false);
                    AssertEqual(0, json.ExitCode, json.StandardError);
                    BlackBoxResilienceGapRequest sent = Body<BlackBoxResilienceGapRequest>(stub, "POST", GapsRoute);
                    AssertEqual("vsl_1", sent.VesselId);
                    AssertEqual(BlackBoxResolutionEnum.Open, sent.Resolution);
                    AssertEqual(BlackBoxPhaseEnum.Landing, sent.Phase);
                    AssertEqual(2, sent.PageNumber);
                    AssertEqual(BlackBoxGapsCommand.MaxPageSize, sent.PageSize, "page size capped at 50");
                    AssertTrue(sent.CreatedAfter.HasValue && Math.Abs((DateTime.UtcNow.AddDays(-30) - sent.CreatedAfter.Value).TotalMinutes) < 5, "since 30d");
                    EnumerationResult<BlackBoxResilienceGap>? page = JsonSerializer.Deserialize<EnumerationResult<BlackBoxResilienceGap>>(json.StandardOutput, ArmadaJson.Options);
                    AssertEqual("fp_gap1", page?.Objects.Single().Fingerprint, "--json prints the page alone");
                    AssertEqual("", json.Output.Trim(), "nothing else on the console");
                    HelmCliRun table = await Run(stub, "blackbox", "gaps").ConfigureAwait(false);
                    AssertEqual(0, table.ExitCode, table.StandardError);
                    string text = Flat(table.Output);
                    AssertContains("Armada resilience gaps", text, "table title");
                    AssertContains("Recreate", text, "top change");
                    AssertContains("Critical", text, "severity");
                    AssertContains("bbx_g1", text, "newest entry");
                    AssertEqual(25, Bodies<BlackBoxResilienceGapRequest>(stub, "POST", GapsRoute).Last().PageSize, "default page size 25");
                }
            }));

            cases.Add(CaseAsync("gaps_empty_and_errors", "gaps says when there are none, refuses a bad enum with exit 1, and maps 401 to the credential exit code", TestTags.Negative, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["POST " + GapsRoute] = StubAdmiralReply.Json("{\"Success\":true,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":0,\"TotalRecords\":0,\"Objects\":[]}");
                    HelmCliRun none = await Run(stub, "blackbox", "gaps").ConfigureAwait(false);
                    AssertEqual(0, none.ExitCode, none.StandardError);
                    AssertContains("No Armada resilience gaps recorded.", Flat(none.Output), "empty message");
                    HelmCliRun bad = await Run(stub, "blackbox", "gaps", "--phase", "nope").ConfigureAwait(false);
                    AssertEqual(1, bad.ExitCode, "bad phase");
                    AssertContains("Landing", bad.StandardError, "valid values listed");
                }

                using (RecordingAdmiralStub denied = new RecordingAdmiralStub())
                {
                    denied.Replies["POST " + GapsRoute] = StubAdmiralReply.Json("{\"Error\":\"Unauthorized\",\"Message\":\"Authentication required.\"}", 401);
                    HelmCliRun run = await Run(denied, "blackbox", "gaps", "--json").ConfigureAwait(false);
                    AssertEqual(Armada.Helm.Infrastructure.HelmErrorHandler.TargetErrorExitCode, run.ExitCode, "credential exit code");
                    AssertEqual("", run.StandardOutput, "nothing on stdout");
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Helm black box resilience", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string GapsPage()
        {
            BlackBoxResilienceGap gap = new BlackBoxResilienceGap
            {
                Fingerprint = "fp_gap1",
                Phase = BlackBoxPhaseEnum.Landing,
                Step = BlackBoxStepEnum.TargetIntegration,
                Occurrences = 6,
                OpenCount = 5,
                LatestEntryId = "bbx_g1",
                Severity = BlackBoxSeverityEnum.Critical,
                SelfHealable = BlackBoxSelfHealableEnum.Yes,
                Suspected = true,
                TopProposedChange = new BlackBoxProposedChange { Rank = 1, Title = "Recreate a missing dock worktree from the branch before landing", Change = "Recreate it.", Severity = BlackBoxSeverityEnum.Critical }
            };
            gap.ArmadaVersions.Add("1.1.0");
            gap.ArmadaVersions.Add("1.2.0");
            EnumerationResult<BlackBoxResilienceGap> page = new EnumerationResult<BlackBoxResilienceGap>();
            page.PageNumber = 2;
            page.PageSize = 50;
            page.TotalPages = 2;
            page.TotalRecords = 51;
            page.Objects = new List<BlackBoxResilienceGap> { gap };
            return ArmadaJson.Serialize(page);
        }

        private static string Flat(string text)
        {
            return String.Join(" ", (text ?? "").Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim().Trim('│', '|').Trim()));
        }

        private static Task<HelmCliRun> Run(RecordingAdmiralStub stub, params string[] args)
        {
            List<string> all = new List<string>(args);
            all.Add("--server");
            all.Add(stub.BaseUrl);
            all.Add("--token");
            all.Add(Token);
            return HelmCliHarness.RunCapturedAsync(all.ToArray());
        }

        private static T Body<T>(RecordingAdmiralStub stub, string method, string path) where T : class
        {
            List<T> bodies = Bodies<T>(stub, method, path);
            AssertTrue(bodies.Count > 0, "a " + method + " " + path + " request");
            return bodies.Last();
        }

        private static List<T> Bodies<T>(RecordingAdmiralStub stub, string method, string path) where T : class
        {
            return stub.Requests().Where(r => r.Method == method && r.Path == path).Select(r => JsonSerializer.Deserialize<T>(r.Body, ArmadaJson.Options)!).ToList();
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
