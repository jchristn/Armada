namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Ask;
    using Armada.Server.Ask;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The Ask Armada approval gate: read-only tools run on a thread-scoped call; state-changing tools become proposals
    /// (handler not invoked); auto-approve executes and records; approve executes through the registered MCP handler
    /// under the approving user's ambient context; reject, expiry, compare-and-set under concurrent approvals; non-thread
    /// calls pass through; work linking and follow-up turns.
    /// </summary>
    public sealed class AskApprovalGateSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.AskApprovalGate";
        private const string DispatchArgs = "{\"title\":\"Fix flaky test\",\"vesselId\":\"vsl_demo\",\"missions\":[{\"title\":\"Fix it\",\"description\":\"d\"}]}";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("policy_allowlist", "The read-only allowlist contains readers and nothing that changes state", TestTags.Positive, () =>
            {
                foreach (string tool in new[] { "enumerate", "status", "voyage_status", "mission_status", "fleet_action_run_status", "vessel_health", "get_vessel", "get_mission_log", "list_objectives", "inbox" })
                    AssertTrue(AskToolPolicy.IsReadOnly(tool), tool + " is read-only");
                foreach (string tool in new[] { "dispatch", "cancel_voyage", "delete_vessel", "run_fleet_action", "evaluate_vessel_health", "stop_server", "restore", "update_vessel", "send_signal", "unknown_future_tool" })
                    AssertFalse(AskToolPolicy.IsReadOnly(tool), tool + " is gated");
                AssertTrue(AskToolPolicy.IsReadOnly("mcp__armada__enumerate"), "Claude prefix ignored");
                AssertFalse(AskToolPolicy.ReadOnlyTools.Any(t => t.StartsWith("delete_") || t.StartsWith("create_") || t.StartsWith("update_") || t.StartsWith("cancel_")), "no writers on the list");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("read_only_tool_runs", "A read-only tool runs normally on a thread-scoped call", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_ro");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);

                object result = await h.CallAsync("enumerate", "{\"entityType\":\"voyage\"}", owner, thread.Id).ConfigureAwait(false);
                AssertFalse(result is string, "real result returned");
                AssertEqual(1, h.Invocations.Count(i => i.ToolName == "enumerate"), "handler invoked");
                AssertEqual(0, (await h.Db.Driver.AskActionProposals.EnumerateByThreadAsync(Constants.DefaultTenantId, thread.Id, null).ConfigureAwait(false)).Count, "no proposal");
            }));

            cases.Add(CaseAsync("mutating_tool_becomes_proposal", "A state-changing tool becomes a pending proposal and is not executed", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_prop");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);

                object result = await h.CallAsync("dispatch", DispatchArgs, owner, thread.Id).ConfigureAwait(false);
                AssertTrue(result is string, "text returned to the captain");
                string text = (string)result;
                AssertContains("Proposed as aap_", text);
                AssertContains("Do not retry", text);
                AssertEqual(0, h.Invocations.Count(i => i.ToolName == "dispatch"), "handler not invoked");

                List<AskActionProposal> proposals = await h.Db.Driver.AskActionProposals.EnumerateByThreadAsync(Constants.DefaultTenantId, thread.Id, null).ConfigureAwait(false);
                AssertEqual(1, proposals.Count, "one proposal");
                AskActionProposal proposal = proposals[0];
                AssertEqual(AskProposalStatusEnum.Pending, proposal.Status);
                AssertEqual(AskProposalSourceEnum.Captain, proposal.Source);
                AssertEqual("dispatch", proposal.ToolName);
                AssertContains("Fix flaky test", proposal.ArgumentsText);
                AssertContains("Dispatch voyage \"Fix flaky test\" to vessel vsl_demo with 1 mission(s)", proposal.SummaryText);
                AssertContains(proposal.Id, text);
                AssertNotNull(proposal.MessageId, "confirm card message");

                AskMessage? card = await h.Db.Driver.AskMessages.ReadAsync(Constants.DefaultTenantId, proposal.MessageId!).ConfigureAwait(false);
                AssertEqual(AskMessageKindEnum.ActionProposal, card!.Kind);
                AssertEqual(proposal.Id, card.ProposalId);
                AssertTrue(h.EventsFor("usr_prop", "ask.proposal").Any(), "ask.proposal sent");

                AskThreadDetail detail = (await h.Threads.GetThreadDetailAsync(owner, thread.Id).ConfigureAwait(false))!;
                AssertEqual(1, detail.PendingProposals.Count, "pending in detail");
            }));

            cases.Add(CaseAsync("auto_approve_executes_and_records", "With auto-approve the tool runs immediately and is recorded as Executed", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_auto");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { AutoApprove = true }).ConfigureAwait(false);

                object result = await h.CallAsync("dispatch", DispatchArgs, owner, thread.Id).ConfigureAwait(false);
                AssertFalse(result is string, "the real result goes back to the captain");
                AssertEqual(1, h.Invocations.Count(i => i.ToolName == "dispatch"), "handler invoked once");

                AskActionProposal proposal = (await h.Db.Driver.AskActionProposals.EnumerateByThreadAsync(Constants.DefaultTenantId, thread.Id, null).ConfigureAwait(false)).Single();
                AssertEqual(AskProposalStatusEnum.Executed, proposal.Status);
                AssertEqual("usr_auto", proposal.DecidedByUserId);
                AssertContains("vyg_stub", proposal.ResultText ?? "");

                AskMessagePage page = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!;
                AskMessage resultMessage = page.Messages.Single(m => m.Kind == AskMessageKindEnum.ActionResult);
                AssertNotNull(resultMessage.TrackedWorkId, "created voyage is tracked");
                List<AskTrackedWork> tracked = await h.Db.Driver.AskTrackedWork.EnumerateByThreadAsync(Constants.DefaultTenantId, thread.Id).ConfigureAwait(false);
                AssertEqual(AskTrackedEntityTypeEnum.Voyage, tracked.Single().EntityType);
            }));

            cases.Add(CaseAsync("approve_executes_via_handler", "Approve executes through the registered handler as the approving user", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_appr");
                Captain captain = await h.Db.Driver.Captains.CreateAsync(new Captain("approver-captain") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                await h.CallAsync("dispatch", DispatchArgs, owner, thread.Id).ConfigureAwait(false);
                AskActionProposal pending = (await h.Db.Driver.AskActionProposals.EnumerateByThreadAsync(Constants.DefaultTenantId, thread.Id, null).ConfigureAwait(false)).Single();

                AskProposalDecision decision = await h.Actions.ApproveAsync(owner, thread.Id, pending.Id).ConfigureAwait(false);
                AssertEqual(200, decision.StatusCode, "approved");
                AssertEqual(AskProposalStatusEnum.Executed, decision.Proposal!.Status);
                AssertNotNull(decision.Proposal.ExecutedUtc, "executed time");
                AssertEqual("usr_appr", decision.Proposal.DecidedByUserId);

                AskToolInvocation invocation = h.Invocations.Single(i => i.ToolName == "dispatch");
                AssertEqual(DispatchArgs, invocation.ArgumentsJson, "exact proposed arguments");
                AssertEqual("usr_appr", invocation.Claims["userId"], "runs as the approving user");
                AssertEqual(Constants.DefaultTenantId, invocation.Claims["tenantId"], "in the user's tenant");
                AssertFalse(invocation.Claims.ContainsKey("askThreadId"), "in-process execution is not gated again");

                AskMessagePage page = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!;
                AssertTrue(page.Messages.Any(m => m.Kind == AskMessageKindEnum.ActionResult && m.ProposalId == pending.Id && m.TrackedWorkId != null), "ActionResult linked to tracked work");

                bool followedUp = await AskTestHarness.WaitUntilAsync(() =>
                {
                    lock (h.Runner.Calls) return Task.FromResult(h.Runner.Calls.Any(c => c.Prompt.Contains("The user approved " + pending.Id)));
                }).ConfigureAwait(false);
                AssertTrue(followedUp, "follow-up captain turn started with the approval outcome");

                AskProposalDecision again = await h.Actions.ApproveAsync(owner, thread.Id, pending.Id).ConfigureAwait(false);
                AssertEqual(409, again.StatusCode, "second approval is a conflict");
                AssertEqual(1, h.Invocations.Count(i => i.ToolName == "dispatch"), "still executed once");
            }));

            cases.Add(CaseAsync("concurrent_approvals_execute_once", "Concurrent approvals execute the tool exactly once", TestTags.Reliability, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_race");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);
                await h.CallAsync("cancel_voyage", "{\"voyageId\":\"vyg_x\"}", owner, thread.Id).ConfigureAwait(false);
                AskActionProposal pending = (await h.Db.Driver.AskActionProposals.EnumerateByThreadAsync(Constants.DefaultTenantId, thread.Id, null).ConfigureAwait(false)).Single();

                AskProposalDecision[] decisions = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => h.Actions.ApproveAsync(owner, thread.Id, pending.Id)))).ConfigureAwait(false);
                AssertEqual(1, decisions.Count(d => d.StatusCode == 200), "one winner");
                AssertEqual(5, decisions.Count(d => d.StatusCode == 409), "the rest conflict");
                AssertEqual(1, h.Invocations.Count(i => i.ToolName == "cancel_voyage"), "executed once");
            }));

            cases.Add(CaseAsync("reject_never_executes", "Reject marks the proposal Rejected and never executes it", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_rej");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);
                await h.CallAsync("dispatch", DispatchArgs, owner, thread.Id).ConfigureAwait(false);
                AskActionProposal pending = (await h.Db.Driver.AskActionProposals.EnumerateByThreadAsync(Constants.DefaultTenantId, thread.Id, null).ConfigureAwait(false)).Single();

                AssertEqual(404, (await h.Actions.RejectAsync(AskTestHarness.User("usr_intruder"), thread.Id, pending.Id).ConfigureAwait(false)).StatusCode, "another user cannot reject");
                AskProposalDecision decision = await h.Actions.RejectAsync(owner, thread.Id, pending.Id).ConfigureAwait(false);
                AssertEqual(200, decision.StatusCode);
                AssertEqual(AskProposalStatusEnum.Rejected, decision.Proposal!.Status);
                AssertNull(decision.Proposal.ExpiresUtc, "decided proposals do not expire");
                AssertEqual(409, (await h.Actions.ApproveAsync(owner, thread.Id, pending.Id).ConfigureAwait(false)).StatusCode, "cannot approve after reject");
                AssertEqual(0, h.Invocations.Count(i => i.ToolName == "dispatch"), "never executed");
            }));

            cases.Add(CaseAsync("expiry", "Pending proposals expire and can no longer be approved", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                h.Settings.Ask.ProposalExpiryMinutes = 1;
                AuthContext owner = AskTestHarness.User("usr_exp");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);

                AskActionProposal old = new AskActionProposal { ToolName = "dispatch", ArgumentsText = DispatchArgs, SummaryText = "old", CreatedUtc = DateTime.UtcNow.AddMinutes(-5) };
                old = await h.Threads.CreateProposalAsync(thread, old, true).ConfigureAwait(false);
                AskActionProposal fresh = await h.Threads.CreateProposalAsync(thread, new AskActionProposal { ToolName = "dispatch", ArgumentsText = DispatchArgs, SummaryText = "fresh" }, true).ConfigureAwait(false);
                AskActionProposal approvedLate = new AskActionProposal { ToolName = "dispatch", ArgumentsText = DispatchArgs, SummaryText = "late", CreatedUtc = DateTime.UtcNow.AddMinutes(-3) };
                approvedLate = await h.Threads.CreateProposalAsync(thread, approvedLate, true).ConfigureAwait(false);

                AskProposalDecision late = await h.Actions.ApproveAsync(owner, thread.Id, approvedLate.Id).ConfigureAwait(false);
                AssertEqual(409, late.StatusCode, "approving an expired proposal conflicts");
                AssertEqual(AskProposalStatusEnum.Expired, late.Proposal!.Status, "and marks it Expired");

                int expired = await h.Actions.ExpireDueAsync().ConfigureAwait(false);
                AssertEqual(1, expired, "the sweep expires the remaining old one");
                AssertEqual(AskProposalStatusEnum.Expired, (await h.Db.Driver.AskActionProposals.ReadAsync(Constants.DefaultTenantId, old.Id).ConfigureAwait(false))!.Status);
                AssertEqual(AskProposalStatusEnum.Pending, (await h.Db.Driver.AskActionProposals.ReadAsync(Constants.DefaultTenantId, fresh.Id).ConfigureAwait(false))!.Status, "fresh stays pending");
                AssertEqual(0, h.Invocations.Count, "nothing executed");
            }));

            cases.Add(CaseAsync("outcome_from_typed_result", "Success comes from the typed result: an Error-named field is not a failure and a huge McpToolError still fails", TestTags.Negative, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_typed");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);

                // A successful result that happens to carry an Error-named field (for example a record's last error).
                h.RegisterStub("update_vessel", _ => new { Id = "vsl_x", Error = "previous run failed" });
                AskProposalDecision ok = await h.Actions.SubmitQuickActionAsync(owner, thread.Id, new AskActionRequest { ToolName = "update_vessel" }).ConfigureAwait(false);
                AssertEqual(AskProposalStatusEnum.Executed, ok.Proposal!.Status, "an Error field in a successful result is not a failure");
                AssertNull(ok.Proposal.ErrorText);

                // A failure whose serialized form exceeds the result limit (so its truncated text no longer parses).
                string huge = new string('x', 70000);
                h.RegisterStub("restore", _ => McpToolError.Conflict(huge));
                AskProposalDecision failed = await h.Actions.SubmitQuickActionAsync(owner, thread.Id, new AskActionRequest { ToolName = "restore" }).ConfigureAwait(false);
                AssertEqual(AskProposalStatusEnum.Failed, failed.Proposal!.Status, "a truncated McpToolError is still a failure");
                AssertEqual(huge, failed.Proposal.ErrorText);
            }));

            cases.Add(CaseAsync("failed_execution", "A tool that errors or throws marks the proposal Failed and posts an Error message", TestTags.Negative, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_fail");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);

                AskProposalDecision errored = await h.Actions.SubmitQuickActionAsync(owner, thread.Id, new AskActionRequest { ToolName = "delete_vessel" }).ConfigureAwait(false);
                AssertEqual(AskProposalStatusEnum.Failed, errored.Proposal!.Status);
                AssertEqual("Vessel not found", errored.Proposal.ErrorText);

                AskProposalDecision thrown = await h.Actions.SubmitQuickActionAsync(owner, thread.Id, new AskActionRequest { ToolName = "explode" }).ConfigureAwait(false);
                AssertEqual(AskProposalStatusEnum.Failed, thrown.Proposal!.Status);
                AssertEqual("boom", thrown.Proposal.ErrorText);

                AskMessagePage page = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!;
                AssertEqual(2, page.Messages.Count(m => m.Kind == AskMessageKindEnum.Error), "two error messages");
                AssertEqual(0, (await h.Db.Driver.AskTrackedWork.EnumerateByThreadAsync(Constants.DefaultTenantId, thread.Id).ConfigureAwait(false)).Count, "failures link nothing");
            }));

            cases.Add(CaseAsync("non_thread_and_foreign_thread_calls", "Normal MCP calls pass through; a token for someone else's thread is refused", TestTags.Negative, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_own");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);

                object direct = await h.CallAsync("dispatch", DispatchArgs, owner, null).ConfigureAwait(false);
                AssertFalse(direct is string, "a normal MCP call executes");
                AssertEqual(1, h.Invocations.Count, "executed");

                object foreign = await h.CallAsync("dispatch", DispatchArgs, AskTestHarness.User("usr_mallory"), thread.Id).ConfigureAwait(false);
                AssertContains("no longer exists", System.Text.Json.JsonSerializer.Serialize(foreign));
                AssertEqual(1, h.Invocations.Count, "foreign-thread call not executed");
                AssertEqual(0, (await h.Db.Driver.AskActionProposals.EnumerateByThreadAsync(Constants.DefaultTenantId, thread.Id, null).ConfigureAwait(false)).Count, "and not proposed");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Ask Approval Gate", cases: cases);
        }

        #endregion

        #region Private-Methods

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
