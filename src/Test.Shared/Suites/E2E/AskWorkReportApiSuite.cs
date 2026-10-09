namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Server.Ask;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end: the real server with a scripted captain (<see cref="StubCaptainRuntime"/> standing in for Claude Code)
    /// proposes a dispatch in an Ask thread, the user approves it over REST, the mission runs and lands on the vessel, and
    /// the thread then gets, without being asked, the final milestone with its outcome and the captain's report (a
    /// WorkReport message). Also covers the Ask settings group of the settings API.
    /// </summary>
    public sealed class AskWorkReportApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.AskWorkReport";
        private const string DispatchMarker = "E2E-REPORT-DISPATCH";
        private const string ReportReply = "Done: the stub change landed and nothing failed.";
        private const int LiveTimeoutMs = 90000;
        private readonly StubCaptainBehavior _Behavior = new StubCaptainBehavior();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("voyage_completion_reports_back", "A voyage started from Ask finishes and the thread gets the outcome and the captain's report on its own", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                StubCaptainRuntime.Install(fx.Server, _Behavior);
                using (ArmadaClient admin = LiveServerSetup.Admin(fx))
                {
                    SettingsData settings = (await admin.GetSettingsAsync().ConfigureAwait(false))!;
                    AssertNotNull(settings.Ask, "the settings API returns the ask group");
                    AssertTrue(settings.Ask!.ReportResultsOnCompletion, "reports are on by default");

                    VesselSetup setup = await LiveServerSetup.CreateVesselAsync(admin, "report-e2e").ConfigureAwait(false);
                    Captain captain = await LiveServerSetup.CreateCaptainAsync(admin, "stub-report-1").ConfigureAwait(false);
                    await LiveServerSetup.CreateCaptainAsync(admin, "stub-report-2").ConfigureAwait(false);
                    string missionTitle = "Report change " + Guid.NewGuid().ToString("N").Substring(0, 6);
                    _Behavior.OnTurn = turn => TurnAsync(turn, setup.Vessel.Id, missionTitle);

                    AskThread thread = (await admin.CreateAskThreadAsync(new AskThreadCreateRequest { Title = "Report e2e", CaptainId = captain.Id }).ConfigureAwait(false))!;
                    await admin.SendAskMessageAsync(thread.Id, "Please run the change " + DispatchMarker).ConfigureAwait(false);

                    AskActionProposal? proposal = null;
                    AssertTrue(await LiveServerSetup.WaitUntilAsync(async () =>
                    {
                        AskMessagePage? page = await admin.EnumerateAskMessagesAsync(thread.Id).ConfigureAwait(false);
                        proposal = page?.Messages.Select(m => m.Proposal).FirstOrDefault(p => p != null && p.Status == AskProposalStatusEnum.Pending);
                        return proposal != null && await TurnIdleAsync(admin, thread.Id).ConfigureAwait(false);
                    }, LiveTimeoutMs).ConfigureAwait(false), "the captain proposed a dispatch: " + Errors());

                    AskActionProposal? approved = await admin.ApproveAskProposalAsync(thread.Id, proposal!.Id).ConfigureAwait(false);
                    AssertEqual(AskProposalStatusEnum.Executed, approved?.Status, "the dispatch ran");

                    // The mission lands; the voyage is marked Complete by the Admiral's next health check (the fixture's
                    // heartbeat is minutes long, so run that check now instead of waiting for it).
                    Mission? landed = null;
                    AssertTrue(await LiveServerSetup.WaitUntilAsync(async () =>
                    {
                        landed = (await admin.ListMissionsAsync(new ArmadaPageQuery(1, 100)).ConfigureAwait(false))?.Objects.FirstOrDefault(m => m.Title == missionTitle);
                        return landed != null && landed.Status == MissionStatusEnum.Complete;
                    }, LiveTimeoutMs).ConfigureAwait(false), "the mission landed (status " + (landed?.Status.ToString() ?? "none") + " " + landed?.FailureReason + "): " + Errors());
                    await fx.Server.Admiral.HealthCheckAsync().ConfigureAwait(false);

                    AskMessage? report = null;
                    AssertTrue(await LiveServerSetup.WaitUntilAsync(async () =>
                    {
                        AskMessagePage? page = await admin.EnumerateAskMessagesAsync(thread.Id, new AskMessageEnumerateQuery { PageSize = 200 }).ConfigureAwait(false);
                        report = page?.Messages.FirstOrDefault(m => m.Kind == AskMessageKindEnum.WorkReport && m.ContentText.Length > 0);
                        return report != null;
                    }, LiveTimeoutMs).ConfigureAwait(false), "the thread got the captain's report: " + Errors() + "\n" + await DumpAsync(admin, thread.Id).ConfigureAwait(false));

                    AssertEqual(ReportReply, report!.ContentText, "the captain's report");
                    AssertEqual(AskMessageRoleEnum.Assistant, report.Role, "a captain reply");
                    AssertEqual(captain.Id, report.CaptainId, "written by the thread's captain");
                    AssertNotNull(report.TrackedWorkId, "linked to the tracked work");

                    AskMessagePage messages = (await admin.EnumerateAskMessagesAsync(thread.Id, new AskMessageEnumerateQuery { PageSize = 200 }).ConfigureAwait(false))!;
                    AskMessage final = messages.Messages.Last(m => m.Kind == AskMessageKindEnum.WorkUpdate && m.Sequence < report.Sequence);
                    AssertContains("finished", final.ContentText, "the final milestone");
                    AssertContains("**Outcome**", final.ContentText, "carries the outcome");
                    AssertContains("Mission \"" + missionTitle + "\"", final.ContentText, "names the mission");
                    AssertEqual(1, messages.Messages.Count(m => m.Kind == AskMessageKindEnum.WorkReport), "exactly one report");
                    AssertTrue(_Behavior.TurnPrompts.Any(p => p.Contains(AskTurnCoordinator.ReportInstructions, StringComparison.Ordinal) && p.Contains(missionTitle, StringComparison.Ordinal)), "the captain was given the results");

                    // The ask group round-trips through the settings API and applies live.
                    settings.Ask.ReportResultsOnCompletion = false;
                    SettingsData updated = (await admin.UpdateSettingsAsync(settings).ConfigureAwait(false))!;
                    AssertFalse(updated.Ask!.ReportResultsOnCompletion, "turned off");
                    AssertEqual(settings.Ask.TurnTimeoutMinutes, updated.Ask.TurnTimeoutMinutes, "other ask values kept");
                    updated.Ask.ReportResultsOnCompletion = true;
                    SettingsData restored = (await admin.UpdateSettingsAsync(updated).ConfigureAwait(false))!;
                    AssertTrue(restored.Ask!.ReportResultsOnCompletion, "turned back on");
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "E2E Ask Work Report", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<string> TurnAsync(StubCaptainTurn turn, string vesselId, string missionTitle)
        {
            if (turn.Prompt.Contains(AskTurnCoordinator.ReportInstructions, StringComparison.Ordinal)) return ReportReply;
            if (turn.Prompt.Contains("The user approved", StringComparison.Ordinal)) return "The voyage is running; I will follow it here.";
            if (!turn.Prompt.Contains(DispatchMarker, StringComparison.Ordinal)) return "Done.";

            StubDispatchArguments args = new StubDispatchArguments();
            args.Title = missionTitle;
            args.VesselId = vesselId;
            args.Missions.Add(new MissionDescription(missionTitle, "Add a file (Ask report end-to-end test)."));
            string result = await turn.CallToolAsync("dispatch", JsonSerializer.Serialize(args)).ConfigureAwait(false);
            return "I proposed a dispatch: " + result;
        }

        private static async Task<bool> TurnIdleAsync(ArmadaClient admin, string threadId)
        {
            AskMessagePage? page = await admin.EnumerateAskMessagesAsync(threadId).ConfigureAwait(false);
            return page != null && page.Messages.Any(m => m.ContentText.StartsWith("I proposed a dispatch", StringComparison.Ordinal));
        }

        private static async Task<string> DumpAsync(ArmadaClient admin, string threadId)
        {
            AskMessagePage? page = await admin.EnumerateAskMessagesAsync(threadId, new AskMessageEnumerateQuery { PageSize = 200 }).ConfigureAwait(false);
            if (page == null) return "(no messages)";
            return String.Join("\n", page.Messages.Select(m => m.Sequence + " " + m.Role + "/" + m.Kind + ": " + (m.ContentText.Length > 300 ? m.ContentText.Substring(0, 300) : m.ContentText)));
        }

        private string Errors()
        {
            return _Behavior.Errors.Count == 0 ? "no stub errors" : String.Join("; ", _Behavior.Errors);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag, TestTags.EndToEnd });
        }

        #endregion
    }
}
