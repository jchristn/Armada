namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Authorization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Ask;
    using Armada.Server.Ask;
    using Armada.Server.Mcp.Tools;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Ask Armada and the black box (MISSION_BLACKBOX_PLAN.md sections 10 and 16.8): the read tools run without approval
    /// and the write tools become proposals; the prompt carries the black box section (appended to a customized
    /// template); a scripted captain's answer to the canonical question has the required sections and names only what
    /// the entry holds, and the grounding check flags what it does not; the /blackbox quick action shows the card; Fix it
    /// proposes a recovery that runs a dry run and then confirms when approved; mentioned missions link their cards; a
    /// mission without an entry says so and offers its FailureReason.
    /// </summary>
    public sealed class AskBlackBoxSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.AskBlackBox";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("policy_reads_allowlisted_writes_gated", "Every black box read tool is on the Ask read-only allowlist; every black box write tool is gated (a proposal), not forbidden", TestTags.Positive, () =>
            {
                List<string> blackBoxTools = McpToolAuthorizationRegistry.GetDeclaredTools()
                    .Where(t => McpToolAuthorizationRegistry.GetOrDefault(t).ResourceType == "BlackBox")
                    .ToList();
                AssertTrue(blackBoxTools.Count >= 20, "the registry declares the black box tools: " + blackBoxTools.Count);
                foreach (string tool in blackBoxTools)
                {
                    bool read = McpToolAuthorizationRegistry.GetOrDefault(tool).Operation == ResourceOperationEnum.Read;
                    AssertEqual(read, AskToolPolicy.IsReadOnly(tool), tool + (read ? " is a read and runs without approval" : " changes state and needs approval"));
                    AssertFalse(AskToolPolicy.IsThreadForbidden(tool), tool + " is proposable, not refused");
                    AssertTrue(AskToolPolicy.IsReadOnly(AskToolPolicy.ClaudeToolPrefix + tool) == read, tool + " with the Claude prefix");
                }

                foreach (string tool in McpBlackBoxTools.ReadTools) AssertTrue(AskToolPolicy.IsReadOnly(tool), "MCP read tool " + tool);
                foreach (string tool in new[] { "run_blackbox_recovery", "apply_blackbox_recommendation", "resolve_blackbox", "annotate_blackbox" })
                    AssertFalse(AskToolPolicy.IsReadOnly(tool), tool + " is gated");
                return Task.CompletedTask;
            }));

            cases.Add(Case("prompt_section_appended_once", "The turn prompt carries the black box section: from the built-in template once, appended to a customized template, and on its own without a template", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(AskTestHarness.User("usr_prompt"), null).ConfigureAwait(false);
                Captain captain = new Captain("prompt-captain");
                List<AskMessage> history = new List<AskMessage> { new AskMessage { Role = AskMessageRoleEnum.User, ContentText = AskBlackBoxGuidance.CanonicalPrompt("msn_prompt1") } };

                string custom = AskTurnCoordinator.BuildPrompt(thread, captain, history, null, "My own Ask prompt.");
                AssertContains("My own Ask prompt.", custom);
                AssertEqual(1, Count(custom, AskBlackBoxGuidance.Heading), "appended to a customized template");
                AssertTrue(custom.IndexOf("My own Ask prompt.", StringComparison.Ordinal) < custom.IndexOf(AskBlackBoxGuidance.Heading, StringComparison.Ordinal), "after the operator's text");

                string builtIn = AskTurnCoordinator.BuildPrompt(thread, captain, history, null, "Built-in.\n\n" + AskBlackBoxGuidance.Section);
                AssertEqual(1, Count(builtIn, AskBlackBoxGuidance.Heading), "not repeated when the template has it");

                string none = AskTurnCoordinator.BuildPrompt(thread, captain, history, null, null);
                AssertContains(AskBlackBoxGuidance.Section.TrimEnd(), none, "present without a template");
                AssertContains("User: Show me the pertinent details from the black box for mission msn_prompt1", none);
                AssertEquals("Show me the pertinent details from the black box for mission msn_x and give me your opinion on why it failed and what could have been done differently", AskBlackBoxGuidance.CanonicalPrompt("msn_x"));
            }));

            cases.Add(Case("scripted_answer_is_shaped_and_grounded", "A scripted captain answering the canonical question gets the black box section in its prompt; its answer has What happened, Why, My opinion (labeled), What could have been done differently and Next steps, and names only files, commits and missions in the entry; the mentioned mission links its card", TestTags.Positive, async () =>
            {
                using AskBlackBoxFixture s = await AskBlackBoxFixture.CreateAsync().ConfigureAwait(false);
                BlackBoxDigest digest = await s.Bb.Service.GetDigestAsync(s.Owner, s.EntryId).ConfigureAwait(false);
                BlackBoxRecord record = await s.Bb.Service.GetRecordAsync(s.Owner, s.EntryId).ConfigureAwait(false);
                AskBlackBoxFacts facts = AskBlackBoxFacts.FromRecord(record, s.MissionId);
                string answer = GroundedAnswer(s.MissionId, digest, record);
                s.H.Runner.Reply = answer;

                AskTurnStart start = await s.H.Turns.SendMessageAsync(s.Owner, s.Thread.Id, new AskMessageSendRequest { Content = AskBlackBoxGuidance.CanonicalPrompt(s.MissionId) }).ConfigureAwait(false);
                AssertEqual(202, start.StatusCode);
                await WaitForTurnEndAsync(s.H, s.Thread.Id).ConfigureAwait(false);

                CaptainChatTurnOptions options = s.H.Runner.Calls.Single();
                AssertContains(AskBlackBoxGuidance.Heading, options.Prompt, "the prompt carries the black box section");
                AssertContains("call get_blackbox first", options.Prompt);
                AssertContains("Never invent files, commits, missions", options.Prompt);

                AskMessage reply = (await s.H.Threads.EnumerateMessagesAsync(s.Owner, s.Thread.Id, null).ConfigureAwait(false))!.Messages.Single(m => m.Role == AskMessageRoleEnum.Assistant);
                AskBlackBoxGroundingReport report = AskBlackBoxGrounding.Check(reply.ContentText, facts);
                AssertTrue(report.Shaped, "shaped: " + report);
                AssertTrue(report.Grounded, "grounded: " + report);
                AssertContains(s.MissionId, reply.ContentText);
                AssertContains("src/a.cs", reply.ContentText, "the answer cites the entry's files");

                AskTrackedWork card = await CardAsync(s, s.Thread).ConfigureAwait(false);
                AssertEqual(s.EntryId, card.EntityId, "the mentioned mission's entry is linked");
                AskWorkSnapshot snapshot = (await s.H.Threads.GetWorkSnapshotAsync(s.Owner, s.Thread.Id, card.Id).ConfigureAwait(false))!;
                AssertNotNull(snapshot.BlackBox, "card");
                AssertEqual(s.EntryId, snapshot.BlackBox!.Digest.EntryId);
                AssertEqual(0, (await MessagesOfKindAsync(s, AskMessageKindEnum.WorkUpdate).ConfigureAwait(false)), "a black box card posts no milestones");
            }));

            cases.Add(Case("grounding_check_flags_invented_facts", "The grounding check flags a file, commit or mission that is not in the entry, and an answer that is missing a section or an opinion label", TestTags.Negative, async () =>
            {
                using AskBlackBoxFixture s = await AskBlackBoxFixture.CreateAsync().ConfigureAwait(false);
                BlackBoxDigest digest = await s.Bb.Service.GetDigestAsync(s.Owner, s.EntryId).ConfigureAwait(false);
                BlackBoxRecord record = await s.Bb.Service.GetRecordAsync(s.Owner, s.EntryId).ConfigureAwait(false);
                AskBlackBoxFacts facts = AskBlackBoxFacts.FromRecord(record, s.MissionId);
                string grounded = GroundedAnswer(s.MissionId, digest, record);
                AssertTrue(AskBlackBoxGrounding.Check(grounded, facts).Grounded, "the baseline is grounded");

                AskBlackBoxGroundingReport invented = AskBlackBoxGrounding.Check(grounded + "\nThe real cause is src/Ghost/Phantom.cs from commit deadbeef1234, landed by msn_ghost123.", facts);
                AssertFalse(invented.Grounded, "invented facts are flagged");
                AssertTrue(invented.UnknownFiles.Contains("src/Ghost/Phantom.cs"), "file: " + invented);
                AssertTrue(invented.UnknownCommits.Contains("deadbeef1234"), "commit: " + invented);
                AssertTrue(invented.UnknownMissions.Contains("msn_ghost123"), "mission: " + invented);

                AskBlackBoxGroundingReport unshaped = AskBlackBoxGrounding.Check(grounded.Replace("## My opinion", "## Thoughts").Replace("In my opinion", "Probably"), facts);
                AssertFalse(unshaped.Shaped, "an unlabeled opinion is flagged");
                AssertTrue(unshaped.MissingHeadings.Contains("My opinion"), "missing heading: " + unshaped);
                AssertFalse(AskBlackBoxGrounding.Check(grounded.Replace("Armada should change", "Also change"), facts).ChangesSplit, "user and Armada changes must be separated");
            }));

            cases.Add(Case("blackbox_quick_action_shows_card", "/blackbox calls get_blackbox (the question stays with the client), records an executed QuickAction and links the black box card with chips, conflicting missions, recovery paths and follow-ups", TestTags.Positive, async () =>
            {
                AskQuickAction action = AskQuickActionCatalog.Find("/blackbox")!;
                AssertEqual("get_blackbox", action.ToolName);
                AssertTrue(action.ReadOnly, "a read");
                AssertEqual("missionId,question", String.Join(",", action.Arguments.Select(a => a.Name)));
                AssertEqual("mission", action.Arguments[0].Type); AssertTrue(action.Arguments[0].Required, "missionId required");
                AssertEqual("text", action.Arguments[1].Type); AssertFalse(action.Arguments[1].Required, "question optional");
                AssertTrue(AskQuickActionCatalog.All().Count <= 10, "the catalog stays within its code-fixed bound (ResponseShapeSuite)");

                using AskBlackBoxFixture s = await AskBlackBoxFixture.CreateAsync().ConfigureAwait(false);
                AskActionRequest request = new AskActionRequest { ToolName = "get_blackbox", Arguments = new JsonObject { ["missionId"] = s.MissionId, ["question"] = "Why did it fail?" } };
                AskProposalDecision decision = await s.H.Actions.SubmitQuickActionAsync(s.Owner, s.Thread.Id, request).ConfigureAwait(false);
                AssertEqual(200, decision.StatusCode);
                AssertEqual(AskProposalStatusEnum.Executed, decision.Proposal!.Status, decision.Proposal.ErrorText ?? "");
                AssertEqual(AskProposalSourceEnum.QuickAction, decision.Proposal.Source);
                AssertEqual("Show the black box of mission " + s.MissionId, decision.Proposal.SummaryText);
                AskToolInvocation call = s.H.Invocations.Single(i => i.ToolName == "get_blackbox");
                AssertFalse(call.ArgumentsJson.Contains("question", StringComparison.Ordinal), "the question is not a tool argument: " + call.ArgumentsJson);

                AskTrackedWork card = await CardAsync(s, s.Thread).ConfigureAwait(false);
                AssertEqual(AskTrackedEntityTypeEnum.BlackBox, card.EntityType);
                AssertEqual(AskTrackedWorkStateEnum.Failed, card.State, "an open entry reads as a failure");
                AskWorkSnapshot snapshot = (await s.H.Threads.GetWorkSnapshotAsync(s.Owner, s.Thread.Id, card.Id).ConfigureAwait(false))!;
                AskBlackBoxCard bb = snapshot.BlackBox!;
                AssertEqual(s.MissionId, bb.Digest.MissionId);
                AssertEqual(BlackBoxStepEnum.DirtyCheckout, bb.Digest.Step, "status chip: step");
                AssertTrue(bb.Digest.RecoveryPaths.Count > 0, "recovery paths");
                AssertEqual("LandingFailed", snapshot.Status);
                AssertEqual("fix,armada-fix,compare", String.Join(",", bb.FollowUps.Where(f => f.Name != "apply").Select(f => f.Name)), "follow-ups");
                AskBlackBoxFollowUp fix = bb.FollowUps.First(f => f.Name == "fix");
                AssertEqual("run_blackbox_recovery", fix.ToolName);
                AssertContains(bb.Digest.RecoveryPaths.OrderBy(p => p.Rank).First().Id, fix.ArgumentsText ?? "", "Fix it proposes the top-ranked path");
                AssertContains(s.EntryId, bb.FollowUps.First(f => f.Name == "compare").Prompt ?? "");
                AssertEqual(0, (await MessagesOfKindAsync(s, AskMessageKindEnum.WorkUpdate).ConfigureAwait(false)), "no milestone, no report turn");

                AssertEqual(4 - (bb.Recommendations.Any(r => r.CanApply) ? 0 : 1), bb.FollowUps.Count, "apply appears only for an applicable recommendation");
            }));

            cases.Add(Case("fix_it_dry_run_then_confirm", "Fix it creates a Pending proposal that runs nothing; approving it runs run_blackbox_recovery as a dry run, then confirms it with the dry run's token, annotated as AskArmada", TestTags.Positive, async () =>
            {
                using AskBlackBoxFixture s = await AskBlackBoxFixture.CreateAsync().ConfigureAwait(false);
                await s.H.Actions.SubmitQuickActionAsync(s.Owner, s.Thread.Id, new AskActionRequest { ToolName = "get_blackbox", Arguments = new JsonObject { ["missionId"] = s.MissionId } }).ConfigureAwait(false);
                AskTrackedWork card = await CardAsync(s, s.Thread).ConfigureAwait(false);
                AskBlackBoxFollowUp fix = (await s.H.Threads.GetWorkSnapshotAsync(s.Owner, s.Thread.Id, card.Id).ConfigureAwait(false))!.BlackBox!.FollowUps.First(f => f.Name == "fix");

                AskActionRequest request = new AskActionRequest { ToolName = fix.ToolName!, Arguments = JsonNode.Parse(fix.ArgumentsText!) as JsonObject, Propose = true };
                AskProposalDecision proposed = await s.H.Actions.SubmitQuickActionAsync(s.Owner, s.Thread.Id, request).ConfigureAwait(false);
                AssertEqual(200, proposed.StatusCode);
                AssertEqual(AskProposalStatusEnum.Pending, proposed.Proposal!.Status, "Fix it is a proposal");
                AssertContains("(dry run, then confirm)", proposed.Proposal.SummaryText);
                AssertNotNull(proposed.Proposal.MessageId, "a confirm card is posted");
                AssertEqual(0, s.H.Invocations.Count(i => i.ToolName == "run_blackbox_recovery"), "nothing ran before approval");
                AssertEqual(0, s.Executor.Calls.Count);

                AskProposalDecision approved = await s.H.Actions.ApproveAsync(s.Owner, s.Thread.Id, proposed.Proposal.Id).ConfigureAwait(false);
                AssertEqual(200, approved.StatusCode);
                AssertEqual(AskProposalStatusEnum.Executed, approved.Proposal!.Status, approved.Proposal.ErrorText ?? "");
                List<AskToolInvocation> runs = s.H.Invocations.Where(i => i.ToolName == "run_blackbox_recovery").ToList();
                AssertEqual(2, runs.Count, "a dry run, then the confirmed run");
                AssertContains("\"dryRun\":true", runs[0].ArgumentsJson);
                AssertContains("\"dryRun\":false", runs[1].ArgumentsJson);
                AssertContains("\"confirmToken\":\"bbc_", runs[1].ArgumentsJson, "confirmed with the dry run's token");
                AssertTrue(s.Executor.Calls.Count > 0 || approved.Proposal.ResultText!.Contains("Manual step", StringComparison.Ordinal), "the recorded steps ran");
                AssertContains("\"dryRun\":false", approved.Proposal.ResultText ?? "", "the stored result is the confirmed run");

                List<BlackBoxNote> notes = (await s.Bb.Service.EnumerateNotesAsync(s.Owner, s.EntryId, null).ConfigureAwait(false)).Objects;
                AssertTrue(notes.Count >= 1, "the run is annotated");
                AssertTrue(notes.All(n => n.AuthorKind == BlackBoxNoteAuthorKindEnum.AskArmada), "annotated as AskArmada");

                AskProposalDecision again = await s.H.Actions.ApproveAsync(s.Owner, s.Thread.Id, proposed.Proposal.Id).ConfigureAwait(false);
                AssertEqual(409, again.StatusCode, "a proposal runs once");
                AssertEqual(2, s.H.Invocations.Count(i => i.ToolName == "run_blackbox_recovery"), "nothing ran again");
            }));

            cases.Add(Case("explicit_dry_run_stays_a_dry_run", "A proposal that sets dryRun true runs only the dry run when approved", TestTags.Negative, async () =>
            {
                using AskBlackBoxFixture s = await AskBlackBoxFixture.CreateAsync().ConfigureAwait(false);
                string pathId = (await s.Bb.Service.GetDigestAsync(s.Owner, s.EntryId).ConfigureAwait(false)).RecoveryPaths.OrderBy(p => p.Rank).First().Id;
                AskActionRequest request = new AskActionRequest { ToolName = "run_blackbox_recovery", Arguments = new JsonObject { ["entryId"] = s.EntryId, ["pathId"] = pathId, ["dryRun"] = true }, Propose = true };
                AskProposalDecision proposed = await s.H.Actions.SubmitQuickActionAsync(s.Owner, s.Thread.Id, request).ConfigureAwait(false);
                AssertContains("(dry run only)", proposed.Proposal!.SummaryText);
                AskProposalDecision approved = await s.H.Actions.ApproveAsync(s.Owner, s.Thread.Id, proposed.Proposal.Id).ConfigureAwait(false);
                AssertEqual(AskProposalStatusEnum.Executed, approved.Proposal!.Status, approved.Proposal.ErrorText ?? "");
                AssertEqual(1, s.H.Invocations.Count(i => i.ToolName == "run_blackbox_recovery"), "only the dry run");
                AssertEqual(0, s.Executor.Calls.Count, "a dry run runs nothing");
            }));

            cases.Add(Case("direct_write_call_becomes_proposal", "A thread captain calling run_blackbox_recovery or annotate_blackbox directly gets a proposal back and nothing runs; approving the note writes it as AskArmada", TestTags.Negative, async () =>
            {
                using AskBlackBoxFixture s = await AskBlackBoxFixture.CreateAsync().ConfigureAwait(false);
                string pathId = (await s.Bb.Service.GetDigestAsync(s.Owner, s.EntryId).ConfigureAwait(false)).RecoveryPaths.OrderBy(p => p.Rank).First().Id;
                object recovery = await s.H.CallAsync("run_blackbox_recovery", "{\"entryId\":\"" + s.EntryId + "\",\"pathId\":\"" + pathId + "\",\"dryRun\":false,\"confirmToken\":\"bbc_guess\"}", s.Owner, s.Thread.Id).ConfigureAwait(false);
                AssertTrue(recovery is string text && text.StartsWith("Proposed as aap_", StringComparison.Ordinal), "a proposal, not a run: " + recovery);
                AssertEqual(0, s.H.Invocations.Count(i => i.ToolName == "run_blackbox_recovery"), "not executed");
                AssertEqual(0, s.Executor.Calls.Count);

                object note = await s.H.CallAsync("annotate_blackbox", "{\"entryId\":\"" + s.EntryId + "\",\"body\":\"In my opinion the checkout was dirty.\"}", s.Owner, s.Thread.Id).ConfigureAwait(false);
                AssertTrue(note is string noteText && noteText.StartsWith("Proposed as aap_", StringComparison.Ordinal), "a note is a proposal too: " + note);
                AssertEqual(0L, (await s.Bb.Service.EnumerateNotesAsync(s.Owner, s.EntryId, null).ConfigureAwait(false)).TotalRecords, "no note yet");

                EnumerationResult<AskActionProposal> pending = (await s.H.Threads.EnumerateProposalsAsync(s.Owner, s.Thread.Id, new EnumerationQuery { Status = "Pending" }).ConfigureAwait(false))!;
                AskActionProposal noteProposal = pending.Objects.Single(p => p.ToolName == "annotate_blackbox");
                AssertContains("Add a note to the black box of entry " + s.EntryId, noteProposal.SummaryText);
                AskProposalDecision approved = await s.H.Actions.ApproveAsync(s.Owner, s.Thread.Id, noteProposal.Id).ConfigureAwait(false);
                AssertEqual(AskProposalStatusEnum.Executed, approved.Proposal!.Status, approved.Proposal.ErrorText ?? "");
                BlackBoxNote written = (await s.Bb.Service.EnumerateNotesAsync(s.Owner, s.EntryId, null).ConfigureAwait(false)).Objects.Single();
                AssertEqual(BlackBoxNoteAuthorKindEnum.AskArmada, written.AuthorKind, "saved as an Ask Armada note");

                AskActionProposal recoveryProposal = pending.Objects.Single(p => p.ToolName == "run_blackbox_recovery");
                AskProposalDecision approvedRecovery = await s.H.Actions.ApproveAsync(s.Owner, s.Thread.Id, recoveryProposal.Id).ConfigureAwait(false);
                AssertEqual(AskProposalStatusEnum.Failed, approvedRecovery.Proposal!.Status, "a proposal that carries a token runs as proposed, and a guessed token is refused");
                AssertEqual(0, s.Executor.Calls.Count, "nothing ran");
            }));

            cases.Add(Case("thread_read_runs_and_links_card", "A thread captain's get_blackbox runs without approval and links the card", TestTags.Positive, async () =>
            {
                using AskBlackBoxFixture s = await AskBlackBoxFixture.CreateAsync().ConfigureAwait(false);
                object result = await s.H.CallAsync("get_blackbox", "{\"missionId\":\"" + s.MissionId + "\"}", s.Owner, s.Thread.Id).ConfigureAwait(false);
                AssertTrue(result is BlackBoxDigest, "the digest, not a proposal: " + result);
                AssertEqual(0L, (await s.H.Threads.EnumerateProposalsAsync(s.Owner, s.Thread.Id, new EnumerationQuery()).ConfigureAwait(false))!.TotalRecords, "no proposal for a read");
                AssertEqual(s.EntryId, (await CardAsync(s, s.Thread).ConfigureAwait(false)).EntityId);
            }));

            cases.Add(Case("mentions_link_only_visible_entries", "Mission ids in a message link the cards of entries the owner can see, once each; another user's mission and an unknown mission link nothing", TestTags.Negative, async () =>
            {
                using AskBlackBoxFixture s = await AskBlackBoxFixture.CreateAsync(false).ConfigureAwait(false);
                UserMaster stranger = await s.H.Db.Driver.Users.CreateAsync(new UserMaster(Constants.DefaultTenantId, "bb-stranger-" + Guid.NewGuid().ToString("N").Substring(0, 6) + "@example.com", "password")).ConfigureAwait(false);
                string strangerMission = (await s.CaptureAsync(stranger.Id).ConfigureAwait(false)).MissionId;
                AskThread internalThread = (await s.H.Threads.ReadThreadInternalAsync(s.Thread.Id).ConfigureAwait(false))!;

                int linked = await s.H.Actions.LinkMentionedBlackBoxesAsync(internalThread, "Compare " + s.MissionId + " with " + strangerMission + " and msn_unknown99.").ConfigureAwait(false);
                AssertEqual(1, linked, "only the owner's entry");
                AssertEqual(0, await s.H.Actions.LinkMentionedBlackBoxesAsync(internalThread, "Again: " + s.MissionId).ConfigureAwait(false), "linked once");
                EnumerationResult<AskTrackedWork> work = (await s.H.Threads.EnumerateTrackedWorkAsync(s.Owner, s.Thread.Id, new EnumerationQuery()).ConfigureAwait(false))!;
                AssertEqual(1L, work.TotalRecords);
                AssertEqual(s.EntryId, work.Objects.Single().EntityId);
                AssertEqual(0, AskWorkLinker.MentionedMissionIds("msn_ab").Count, "too short to be an id");
                AssertEqual(AskWorkLinker.MaxMentionedMissions, AskWorkLinker.MentionedMissionIds(String.Join(" ", Enumerable.Range(0, 9).Select(i => "msn_abcd" + i))).Count, "bounded");
            }));

            cases.Add(Case("mission_without_entry_says_so", "/blackbox on a mission without an entry fails with BlackBoxNotFound, says so and offers the FailureReason; the answer check recognizes a reply that does the same", TestTags.Negative, async () =>
            {
                using AskBlackBoxFixture s = await AskBlackBoxFixture.CreateAsync().ConfigureAwait(false);
                Mission bare = await BlackBoxTestKit.FailedMissionAsync(s.H.Db.Driver, Constants.DefaultTenantId, s.User.Id).ConfigureAwait(false);
                AskProposalDecision decision = await s.H.Actions.SubmitQuickActionAsync(s.Owner, s.Thread.Id, new AskActionRequest { ToolName = "get_blackbox", Arguments = new JsonObject { ["missionId"] = bare.Id } }).ConfigureAwait(false);
                AssertEqual(AskProposalStatusEnum.Failed, decision.Proposal!.Status);
                AskMessage result = (await s.H.Threads.EnumerateMessagesAsync(s.Owner, s.Thread.Id, null).ConfigureAwait(false))!.Messages.Last(m => m.ProposalId == decision.Proposal.Id);
                AssertEqual(AskMessageKindEnum.Error, result.Kind);
                AssertContains("no black box entry", result.ContentText);
                AssertContains("FailureReason", result.ContentText, "offers the FailureReason");
                AssertEqual(0L, (await s.H.Threads.EnumerateTrackedWorkAsync(s.Owner, s.Thread.Id, new EnumerationQuery()).ConfigureAwait(false))!.TotalRecords, "no card");

                AssertTrue(AskBlackBoxGrounding.SaysNoEntryAndOffersFailureReason("Mission " + bare.Id + " has no black box entry. Do you want me to show its FailureReason from mission_status instead?"), "scripted reply");
                AssertFalse(AskBlackBoxGrounding.SaysNoEntryAndOffersFailureReason("What happened: the landing failed in src/a.cs."), "an invented answer");
            }));

            cases.Add(Case("evaluation_case_matches_prompt", "The Ask black box case of the prompt evaluation script evaluates the shipped black box section", TestTags.Positive, () =>
            {
                string path = System.IO.Path.Combine(DashboardSource.RepoRoot(), "scripts", "common", "prompt-evaluations", "cases.json");
                List<PromptEvaluationCase> evaluations = System.Text.Json.JsonSerializer.Deserialize<List<PromptEvaluationCase>>(System.IO.File.ReadAllText(path))!;
                PromptEvaluationCase blackBox = evaluations.Single(c => c.Kind == "blackbox");
                AssertEqual("ask.system", blackBox.Persona);
                AssertEqual("\n" + AskBlackBoxGuidance.Section, blackBox.ImprovedAddition, "cases.json carries the current section (regenerate it when the section changes)");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Ask Armada black box", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string GroundedAnswer(string missionId, BlackBoxDigest digest, BlackBoxRecord record)
        {
            List<string> files = record.Workspace?.DirtyPaths ?? new List<string>();
            StringBuilder answer = new StringBuilder();
            answer.AppendLine("## What happened");
            answer.AppendLine("Mission " + missionId + " failed in " + digest.Phase + " at step " + digest.Step + (digest.SourceFile != null ? " in " + digest.SourceFile : String.Empty) + (digest.Method != null ? " (" + digest.Method + ")" : String.Empty) + "; status " + digest.StatusBefore + " to " + digest.StatusAfter + ".");
            if (files.Count > 0) answer.AppendLine("The checkout had uncommitted changes: " + String.Join(", ", files) + ".");
            answer.AppendLine();
            answer.AppendLine("## Why");
            foreach (BlackBoxFinding finding in digest.Why) answer.AppendLine("- " + finding.RuleId + " (" + finding.Confidence + ")");
            answer.AppendLine("Triage: " + digest.Triage + ".");
            answer.AppendLine();
            answer.AppendLine("## My opinion");
            answer.AppendLine("In my opinion the triage is right: the landing found a dirty checkout. A clean checkout at the next attempt would change my mind.");
            answer.AppendLine();
            answer.AppendLine("## What could have been done differently");
            answer.AppendLine("- You can change the vessel's landing mode to a pull request so a dirty checkout does not block landing.");
            answer.AppendLine("- Armada should change the landing step to stash or reset the checkout before it integrates.");
            answer.AppendLine();
            answer.AppendLine("## Next steps");
            foreach (BlackBoxRecoveryPath path in digest.RecoveryPaths.OrderBy(p => p.Rank)) answer.AppendLine("- " + path.Id + ": " + path.Title);
            answer.AppendLine("- Copy FIX THIS to hand the brief to an agent.");
            return answer.ToString();
        }

        private static async Task<AskTrackedWork> CardAsync(AskBlackBoxFixture s, AskThread thread)
        {
            AskTrackedWork? card = null;
            bool found = await AskTestHarness.WaitUntilAsync(async () =>
            {
                EnumerationResult<AskTrackedWork>? work = await s.H.Threads.EnumerateTrackedWorkAsync(s.Owner, thread.Id, new EnumerationQuery()).ConfigureAwait(false);
                card = work?.Objects.FirstOrDefault(w => w.EntityType == AskTrackedEntityTypeEnum.BlackBox);
                return card != null;
            }).ConfigureAwait(false);
            AssertTrue(found, "a black box card is linked");
            return card!;
        }

        private static async Task<int> MessagesOfKindAsync(AskBlackBoxFixture s, AskMessageKindEnum kind)
        {
            await s.H.Tracker.WaitForMilestonesAsync(s.Thread.Id).ConfigureAwait(false);
            AskMessagePage page = (await s.H.Threads.EnumerateMessagesAsync(s.Owner, s.Thread.Id, null).ConfigureAwait(false))!;
            return page.Messages.Count(m => m.Kind == kind);
        }

        private static async Task WaitForTurnEndAsync(AskTestHarness h, string threadId)
        {
            bool done = await AskTestHarness.WaitUntilAsync(() => Task.FromResult(h.Turns.ActiveTurnId(threadId) == null), 10000).ConfigureAwait(false);
            AssertTrue(done, "turn finished");
            await Task.Delay(50).ConfigureAwait(false);
        }

        private static int Count(string text, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0) { count++; index += value.Length; }
            return count;
        }

        private static void AssertEquals(string expected, string actual)
        {
            AssertEqual(expected, actual);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
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
