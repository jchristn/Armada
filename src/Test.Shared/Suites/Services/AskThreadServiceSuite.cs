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
    /// AskThreadService: owner scoping (another user's thread reads as not found everywhere), automatic titles, unread
    /// counts, captain clearing, owner-only events, message population, and sequence monotonicity under concurrent
    /// milestone posts.
    /// </summary>
    public sealed class AskThreadServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.AskThreadService";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("other_user_thread_is_not_found", "Another user's thread is not found by any owner-scoped operation", TestTags.Negative, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_owner");
                AuthContext other = AskTestHarness.User("usr_other");
                AuthContext admin = AuthContext.Authenticated(Constants.DefaultTenantId, "usr_admin", true, true, "Test");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { Title = "Private" }).ConfigureAwait(false);

                foreach (AuthContext stranger in new[] { other, admin })
                {
                    AssertNull(await h.Threads.GetThreadAsync(stranger, thread.Id).ConfigureAwait(false), "get");
                    AssertNull(await h.Threads.GetThreadDetailAsync(stranger, thread.Id).ConfigureAwait(false), "detail");
                    AssertNull(await h.Threads.UpdateThreadAsync(stranger, thread.Id, new AskThreadUpdateRequest { Title = "x" }).ConfigureAwait(false), "update");
                    AssertNull(await h.Threads.EnumerateMessagesAsync(stranger, thread.Id, null).ConfigureAwait(false), "messages");
                    AssertNull(await h.Threads.MarkReadAsync(stranger, thread.Id).ConfigureAwait(false), "read");
                    AssertFalse(await h.Threads.DeleteThreadAsync(stranger, thread.Id).ConfigureAwait(false), "delete");
                    AssertEqual(0L, (await h.Threads.EnumerateThreadsAsync(stranger, null).ConfigureAwait(false)).TotalRecords, "enumerate");
                    AssertEqual(404, (await h.Turns.SendMessageAsync(stranger, thread.Id, new AskMessageSendRequest { Content = "hi" }).ConfigureAwait(false)).StatusCode, "send");
                    AssertEqual(404, (await h.Actions.SubmitQuickActionAsync(stranger, thread.Id, new AskActionRequest { ToolName = "status" }).ConfigureAwait(false)).StatusCode, "action");
                }

                AssertNotNull(await h.Threads.GetThreadAsync(owner, thread.Id).ConfigureAwait(false), "owner still sees it");
                AssertEqual(1L, (await h.Threads.EnumerateThreadsAsync(owner, null).ConfigureAwait(false)).TotalRecords, "owner enumerate");
            }));

            cases.Add(CaseAsync("detail_sets_cli_permission_decision_flags", "Thread detail sets the caller's decision flags on pending CLI permission requests", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext admin = AuthContext.Authenticated(Constants.DefaultTenantId, "usr_admin_owner", true, true, "Test");
                AuthContext user = AskTestHarness.User("usr_plain_owner", false);

                foreach (AuthContext owner in new[] { admin, user })
                {
                    AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { Title = "Permissions" }).ConfigureAwait(false);
                    CliPermissionRequest request = new CliPermissionRequest();
                    request.TenantId = Constants.DefaultTenantId;
                    request.UserId = owner.UserId;
                    request.ThreadId = thread.Id;
                    request.ToolName = "Bash";
                    request.InputText = "git status";
                    request.SummaryText = "git status";
                    await h.Db.Driver.CliPermissionRequests.CreateAsync(request).ConfigureAwait(false);

                    AskThreadDetail? detail = await h.Threads.GetThreadDetailAsync(owner, thread.Id).ConfigureAwait(false);
                    CliPermissionRequest pending = detail!.PendingCliPermissions.Single();
                    bool expected = owner.IsAdmin || owner.IsTenantAdmin;
                    AssertEqual(expected, pending.CanDecide, (expected ? "an admin owner can decide" : "a regular owner cannot decide by default") + " from thread detail");
                    AssertEqual(expected, pending.CanRemember, "remember follows decide for " + owner.UserId);
                }
            }));

            cases.Add(CaseAsync("auto_title_from_first_message", "The first message names a default-titled thread", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_title");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest()).ConfigureAwait(false);
                AssertEqual(AskThreadService.DefaultTitle, thread.Title, "default title");

                AskTurnStart start = await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "Please dispatch a cleanup voyage for the payments service and then report back on the results" }).ConfigureAwait(false);
                AssertEqual(202, start.StatusCode, "accepted");
                AssertNull(start.Response!.TurnId, "no captain, no turn");
                AskThread? named = await h.Threads.GetThreadAsync(owner, thread.Id).ConfigureAwait(false);
                AssertStartsWith("Please dispatch a cleanup voyage", named!.Title, "title from message");
                AssertTrue(named.Title.Length <= 63, "title is short");

                await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "Second message" }).ConfigureAwait(false);
                AskThread? still = await h.Threads.GetThreadAsync(owner, thread.Id).ConfigureAwait(false);
                AssertEqual(named.Title, still!.Title, "later messages do not rename");

                AssertEqual("Short", AskThreadService.BuildAutoTitle("  Short  ", 60));
                AssertEqual(String.Empty, AskThreadService.BuildAutoTitle("   ", 60));
            }));

            cases.Add(CaseAsync("unread_counts_and_read_marker", "Captain and Armada messages count as unread until marked read; user messages do not", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_unread");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);
                await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "hello" }).ConfigureAwait(false);
                AssertEqual(0, (await h.Threads.GetThreadAsync(owner, thread.Id).ConfigureAwait(false))!.UnreadCount, "own message is read");

                AskThread fresh = (await h.Threads.ReadThreadInternalAsync(thread.Id).ConfigureAwait(false))!;
                await h.Threads.AppendMessageAsync(fresh, new AskMessage { Role = AskMessageRoleEnum.System, Kind = AskMessageKindEnum.WorkUpdate, ContentText = "Voyage started." }, true).ConfigureAwait(false);
                await h.Threads.AppendMessageAsync(fresh, new AskMessage { Role = AskMessageRoleEnum.System, Kind = AskMessageKindEnum.WorkUpdate, ContentText = "Voyage finished." }, true).ConfigureAwait(false);
                AskThread? unread = await h.Threads.GetThreadAsync(owner, thread.Id).ConfigureAwait(false);
                AssertEqual(2, unread!.UnreadCount, "two unread updates");
                AssertEqual(3, unread.MessageCount, "message count");

                AskThread? read = await h.Threads.MarkReadAsync(owner, thread.Id).ConfigureAwait(false);
                AssertEqual(0, read!.UnreadCount, "marked read");
                AssertTrue(h.EventsFor("usr_unread", "ask.thread").Any(), "ask.thread announced");
            }));

            cases.Add(CaseAsync("update_semantics_and_captain_clear", "Updates change only given fields; CaptainId present with null clears the captain", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_upd");
                Captain captain = await h.Db.Driver.Captains.CreateAsync(new Captain("ask-captain") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                AssertEqual(captain.Id, thread.CaptainId, "captain set on create");

                AskThread? pinned = await h.Threads.UpdateThreadAsync(owner, thread.Id, new AskThreadUpdateRequest { Pinned = true }).ConfigureAwait(false);
                AssertTrue(pinned!.Pinned, "pinned");
                AssertEqual(captain.Id, pinned.CaptainId, "absent CaptainId keeps the captain");

                AskThread? cleared = await h.Threads.UpdateThreadAsync(owner, thread.Id, new AskThreadUpdateRequest { CaptainId = null, CaptainIdSpecified = true }).ConfigureAwait(false);
                AssertNull(cleared!.CaptainId, "explicit null clears the captain");
                AssertTrue(cleared.Pinned, "other fields unchanged");

                bool threw = false;
                try { await h.Threads.UpdateThreadAsync(owner, thread.Id, new AskThreadUpdateRequest { CaptainId = "cpt_missing", CaptainIdSpecified = true }).ConfigureAwait(false); }
                catch (ArgumentException) { threw = true; }
                AssertTrue(threw, "unknown captain rejected");

                Captain foreign = await h.Db.Driver.Captains.CreateAsync(new Captain("foreign") { TenantId = (await h.Db.Driver.Tenants.CreateAsync(new TenantMetadata("foreign-" + Guid.NewGuid().ToString("N").Substring(0, 6))).ConfigureAwait(false)).Id }).ConfigureAwait(false);
                threw = false;
                try { await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = foreign.Id }).ConfigureAwait(false); }
                catch (ArgumentException) { threw = true; }
                AssertTrue(threw, "another tenant's captain is not visible");
            }));

            cases.Add(CaseAsync("events_go_to_owner_only", "Thread events are addressed to the owner only", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_evt");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);
                await h.Actions.SubmitQuickActionAsync(owner, thread.Id, new AskActionRequest { ToolName = "status" }).ConfigureAwait(false);

                List<AskRecordedEvent> events;
                lock (h.Events) events = h.Events.ToList();
                AssertTrue(events.Count > 0, "events emitted");
                AssertTrue(events.All(e => e.UserId == "usr_evt" && e.TenantId == Constants.DefaultTenantId), "every event addressed to the owner");
                AssertTrue(events.Any(e => e.EventType == "ask.message"), "ask.message");
                AssertTrue(events.Any(e => e.EventType == "ask.proposal"), "ask.proposal");
            }));

            cases.Add(CaseAsync("sequence_monotonic_under_concurrent_milestones", "Concurrent milestone posts get unique increasing sequences", TestTags.Reliability, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_mono");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);
                AskMessage[] posted = await Task.WhenAll(Enumerable.Range(0, 30).Select(i => Task.Run(() =>
                    h.Threads.AppendMessageAsync(thread, new AskMessage { Role = AskMessageRoleEnum.System, Kind = AskMessageKindEnum.WorkUpdate, ContentText = "m" + i }, true)))).ConfigureAwait(false);

                List<int> sequences = posted.Select(m => m.Sequence).OrderBy(s => s).ToList();
                AssertEqual(30, sequences.Distinct().Count(), "unique");
                AssertEqual(String.Join(",", Enumerable.Range(1, 30)), String.Join(",", sequences), "gap-free and increasing");

                AskMessagePage page = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, new AskMessageEnumerateRequest { PageSize = 10 }).ConfigureAwait(false))!;
                AssertEqual(10, page.Messages.Count, "newest page");
                AssertEqual(21, page.Messages.First().Sequence, "newest page starts at 21");
                AssertTrue(page.HasMore, "has more");
            }));

            cases.Add(CaseAsync("messages_embed_proposal_and_tool_calls", "Messages embed tool calls, proposals (with ExpiresUtc), and tracked work", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_embed");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);
                await h.CallAsync("dispatch", "{\"title\":\"x\",\"vesselId\":\"vsl_1\",\"missions\":[{\"title\":\"a\"}]}", owner, thread.Id).ConfigureAwait(false);
                AskMessage reply = new AskMessage { Role = AskMessageRoleEnum.Assistant, ContentText = "Proposed." };
                await h.Threads.AppendMessageAsync(thread, reply, true, new List<AskMessageToolCall> { new AskMessageToolCall { CallId = "c1", ToolName = "mcp__armada__dispatch", Ok = true } }).ConfigureAwait(false);

                AskMessagePage page = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!;
                AskMessage card = page.Messages.First(m => m.Kind == AskMessageKindEnum.ActionProposal);
                AssertNotNull(card.Proposal, "card embeds proposal");
                AssertEqual(AskProposalStatusEnum.Pending, card.Proposal!.Status);
                AssertNotNull(card.Proposal.ExpiresUtc, "pending proposal has ExpiresUtc");
                AskMessage text = page.Messages.First(m => m.Role == AskMessageRoleEnum.Assistant);
                AssertEqual(1, text.ToolCalls.Count, "tool call embedded");
                AssertEqual("mcp__armada__dispatch", text.ToolCalls[0].ToolName);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Ask Thread Service", cases: cases);
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
