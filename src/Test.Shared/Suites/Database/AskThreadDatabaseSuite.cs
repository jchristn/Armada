namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the Ask Armada thread persistence added by migration 75: ask_threads (owner scoping, ordering,
    /// search, archive filter, paging, read marker, cascade delete), ask_messages (sequence assignment under concurrency,
    /// counters, paging), ask_message_tool_calls, ask_action_proposals (compare-and-set transitions, expiry query), and
    /// ask_tracked_work (create-or-get, update, active queries).
    /// </summary>
    public sealed class AskThreadDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.AskThreads";
        private static readonly DateTime _Fixed = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("thread_roundtrip_and_owner_scoping", "Threads round-trip and are only readable by their owner", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AskThread thread = NewThread("usr_owner");
                thread.Title = "Deploy plan";
                thread.CaptainId = "cpt_a";
                thread.AutoApprove = true;
                thread.SummaryText = "summary";
                thread.SummaryUtc = _Fixed;
                await db.AskThreads.CreateAsync(thread).ConfigureAwait(false);

                AskThread? read = await db.AskThreads.ReadAsync(Constants.DefaultTenantId, "usr_owner", thread.Id).ConfigureAwait(false);
                AssertNotNull(read, "owner read");
                AssertEqual("Deploy plan", read!.Title);
                AssertEqual("cpt_a", read.CaptainId);
                AssertTrue(read.AutoApprove, "auto approve");
                AssertEqual("summary", read.SummaryText);
                AssertSameInstant(_Fixed, read.SummaryUtc!.Value, "summary utc");
                AssertFalse(read.Pinned, "pinned default");
                AssertFalse(read.Archived, "archived default");
                AssertEqual(0, read.MessageCount);
                AssertNull(read.LastMessageUtc, "no messages yet");

                AssertNull(await db.AskThreads.ReadAsync(Constants.DefaultTenantId, "usr_other", thread.Id).ConfigureAwait(false), "another user");
                AssertNull(await db.AskThreads.ReadAsync("ten_other", "usr_owner", thread.Id).ConfigureAwait(false), "another tenant");
                AssertNotNull(await db.AskThreads.ReadByIdAsync(thread.Id).ConfigureAwait(false), "internal read by id");
            }));

            cases.Add(CaseAsync("thread_update", "Thread user-editable fields update", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AskThread thread = await db.AskThreads.CreateAsync(NewThread("usr_u")).ConfigureAwait(false);
                thread.Title = "Renamed";
                thread.Pinned = true;
                thread.Archived = true;
                thread.AutoApprove = true;
                thread.CaptainId = null;
                AskThread updated = await db.AskThreads.UpdateAsync(thread).ConfigureAwait(false);
                AssertEqual("Renamed", updated.Title);
                AssertTrue(updated.Pinned, "pinned");
                AssertTrue(updated.Archived, "archived");
                AssertTrue(updated.AutoApprove, "auto approve");
                AssertNull(updated.CaptainId, "captain cleared");
            }));

            cases.Add(CaseAsync("thread_enumerate_order_search_archive_paging", "Enumeration orders pinned first then recent, filters, and pages", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string user = "usr_enum";

                AskThread old = NewThread(user); old.Title = "Alpha release"; old.CreatedUtc = _Fixed;
                AskThread recent = NewThread(user); recent.Title = "Beta fixes"; recent.CreatedUtc = _Fixed.AddMinutes(10);
                AskThread pinned = NewThread(user); pinned.Title = "Pinned alpha"; pinned.CreatedUtc = _Fixed.AddMinutes(-30); pinned.Pinned = true;
                AskThread archived = NewThread(user); archived.Title = "Archived alpha"; archived.CreatedUtc = _Fixed.AddMinutes(20); archived.Archived = true;
                AskThread foreign = NewThread("usr_someone_else"); foreign.Title = "Alpha foreign";
                foreach (AskThread t in new[] { old, recent, pinned, archived, foreign }) await db.AskThreads.CreateAsync(t).ConfigureAwait(false);

                // A message on the oldest thread makes it the most recently active unpinned thread.
                await db.AskMessages.CreateAsync(NewMessage(old, "hello"), false).ConfigureAwait(false);

                EnumerationResult<AskThread> all = await db.AskThreads.EnumerateAsync(Constants.DefaultTenantId, user, new AskThreadEnumerateRequest()).ConfigureAwait(false);
                AssertEqual(3L, all.TotalRecords, "archived and foreign threads excluded");
                AssertEqual(pinned.Id, all.Objects[0].Id, "pinned first");
                AssertEqual(old.Id, all.Objects[1].Id, "most recent activity next");
                AssertEqual(recent.Id, all.Objects[2].Id, "older activity last");

                AskThreadEnumerateRequest withArchived = new AskThreadEnumerateRequest();
                withArchived.IncludeArchived = true;
                withArchived.Search = "ALPHA";
                EnumerationResult<AskThread> searched = await db.AskThreads.EnumerateAsync(Constants.DefaultTenantId, user, withArchived).ConfigureAwait(false);
                AssertEqual(3L, searched.TotalRecords, "case-insensitive search including archived");
                AssertFalse(searched.Objects.Any(t => t.Id == foreign.Id), "other users never match");

                AskThreadEnumerateRequest paged = new AskThreadEnumerateRequest();
                paged.PageSize = 2;
                paged.PageNumber = 2;
                EnumerationResult<AskThread> page2 = await db.AskThreads.EnumerateAsync(Constants.DefaultTenantId, user, paged).ConfigureAwait(false);
                AssertEqual(1, page2.Objects.Count, "second page");
                AssertEqual(2, page2.TotalPages, "total pages");
                AssertEqual(recent.Id, page2.Objects[0].Id, "second page content");
            }));

            cases.Add(CaseAsync("thread_delete_cascades", "Deleting a thread removes its messages, tool calls, proposals, and tracked work", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AskThread thread = await db.AskThreads.CreateAsync(NewThread("usr_del")).ConfigureAwait(false);
                AskMessage message = await db.AskMessages.CreateAsync(NewMessage(thread, "hi"), false).ConfigureAwait(false);
                await db.AskMessageToolCalls.CreateManyAsync(new List<AskMessageToolCall> { NewToolCall(message, "status") }).ConfigureAwait(false);
                AskActionProposal proposal = await db.AskActionProposals.CreateAsync(NewProposal(thread, "dispatch")).ConfigureAwait(false);
                AskTrackedWork work = await db.AskTrackedWork.CreateOrGetAsync(NewWork(thread, AskTrackedEntityTypeEnum.Voyage, "vyg_1")).ConfigureAwait(false);

                AssertFalse(await db.AskThreads.DeleteAsync(Constants.DefaultTenantId, "usr_intruder", thread.Id).ConfigureAwait(false), "another user cannot delete");
                AssertTrue(await db.AskThreads.DeleteAsync(Constants.DefaultTenantId, "usr_del", thread.Id).ConfigureAwait(false), "owner deletes");

                AssertNull(await db.AskThreads.ReadByIdAsync(thread.Id).ConfigureAwait(false), "thread gone");
                AssertNull(await db.AskMessages.ReadAsync(Constants.DefaultTenantId, message.Id).ConfigureAwait(false), "message gone");
                AssertEqual(0, (await db.AskMessageToolCalls.EnumerateByMessagesAsync(Constants.DefaultTenantId, thread.Id, new List<string> { message.Id }).ConfigureAwait(false)).Count, "tool calls gone");
                AssertNull(await db.AskActionProposals.ReadAsync(Constants.DefaultTenantId, proposal.Id).ConfigureAwait(false), "proposal gone");
                AssertNull(await db.AskTrackedWork.ReadAsync(Constants.DefaultTenantId, work.Id).ConfigureAwait(false), "tracked work gone");
            }));

            cases.Add(CaseAsync("message_sequence_counters_and_read", "Messages get sequences, update counters, and the read marker resets unread", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AskThread thread = await db.AskThreads.CreateAsync(NewThread("usr_seq")).ConfigureAwait(false);
                AskMessage first = await db.AskMessages.CreateAsync(NewMessage(thread, "user says"), false).ConfigureAwait(false);
                AskMessage reply = NewMessage(thread, "assistant says");
                reply.Role = AskMessageRoleEnum.Assistant;
                reply.ThinkingText = "pondering";
                reply.DurationMs = 1234;
                reply.CaptainId = "cpt_x";
                reply = await db.AskMessages.CreateAsync(reply, true).ConfigureAwait(false);
                AskMessage update = NewMessage(thread, "voyage landed");
                update.Role = AskMessageRoleEnum.System;
                update.Kind = AskMessageKindEnum.WorkUpdate;
                update.TrackedWorkId = "atw_1";
                update = await db.AskMessages.CreateAsync(update, true).ConfigureAwait(false);

                AssertEqual(1, first.Sequence, "first sequence");
                AssertEqual(2, reply.Sequence, "second sequence");
                AssertEqual(3, update.Sequence, "third sequence");

                AskThread? afterAppend = await db.AskThreads.ReadByIdAsync(thread.Id).ConfigureAwait(false);
                AssertEqual(3, afterAppend!.MessageCount, "message count");
                AssertEqual(2, afterAppend.UnreadCount, "unread counts only flagged messages");
                AssertNotNull(afterAppend.LastMessageUtc, "last message time");

                AskMessage? readReply = await db.AskMessages.ReadAsync(Constants.DefaultTenantId, reply.Id).ConfigureAwait(false);
                AssertEqual(AskMessageRoleEnum.Assistant, readReply!.Role);
                AssertEqual("pondering", readReply.ThinkingText);
                AssertEqual(1234L, readReply.DurationMs);
                AssertEqual("cpt_x", readReply.CaptainId);

                readReply.ContentText = "edited";
                readReply.ProposalId = "aap_1";
                await db.AskMessages.UpdateAsync(readReply).ConfigureAwait(false);
                AskMessage? edited = await db.AskMessages.ReadAsync(Constants.DefaultTenantId, reply.Id).ConfigureAwait(false);
                AssertEqual("edited", edited!.ContentText);
                AssertEqual("aap_1", edited.ProposalId);
                AssertEqual(2, edited.Sequence, "sequence unchanged by update");

                AssertFalse(await db.AskThreads.MarkReadAsync(Constants.DefaultTenantId, "usr_other", thread.Id).ConfigureAwait(false), "another user cannot mark read");
                AssertTrue(await db.AskThreads.MarkReadAsync(Constants.DefaultTenantId, "usr_seq", thread.Id).ConfigureAwait(false), "owner marks read");
                AskThread? afterRead = await db.AskThreads.ReadByIdAsync(thread.Id).ConfigureAwait(false);
                AssertEqual(0, afterRead!.UnreadCount, "unread reset");
            }));

            cases.Add(CaseAsync("message_turn_metrics_roundtrip", "A reply's turn telemetry (migration 82) round-trips through create, update, read, and enumerate", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AskThread thread = await db.AskThreads.CreateAsync(NewThread("usr_metrics")).ConfigureAwait(false);
                AskMessage question = await db.AskMessages.CreateAsync(NewMessage(thread, "how fast?"), false).ConfigureAwait(false);

                // The reply's place is reserved first (no telemetry yet), then completed with the turn's metrics, as
                // AskTurnCoordinator does.
                AskMessage reply = NewMessage(thread, String.Empty);
                reply.Role = AskMessageRoleEnum.Assistant;
                reply = await db.AskMessages.CreateAsync(reply, true).ConfigureAwait(false);
                AskMessage? reserved = await db.AskMessages.ReadAsync(Constants.DefaultTenantId, reply.Id).ConfigureAwait(false);
                AssertNull(reserved!.Metrics, "no telemetry on the reservation");

                reserved.ContentText = "Fast enough.";
                reserved.DurationMs = 2500;
                reserved.Metrics = new CaptainChatMetrics
                {
                    TimeToFirstTokenMs = 400.25,
                    TimeToFirstTextMs = 1000.5,
                    StreamingMs = 2099.75,
                    TotalMs = 2500,
                    TokensPerSecond = 57.125,
                    PromptTokens = 1050,
                    CompletionTokens = 120,
                    CachedTokens = 900,
                    TokensEstimated = false,
                    CostUsd = 0.0123,
                    ToolCallCount = 2,
                    ToolTimeMs = 812.5
                };
                await db.AskMessages.UpdateAsync(reserved).ConfigureAwait(false);

                AskMessage? read = await db.AskMessages.ReadAsync(Constants.DefaultTenantId, reply.Id).ConfigureAwait(false);
                CaptainChatMetrics? m = read!.Metrics;
                AssertNotNull(m, "metrics read back");
                AssertEqual(400.25, m!.TimeToFirstTokenMs, "ttft");
                AssertEqual(1000.5, m.TimeToFirstTextMs, "first text");
                AssertEqual(2099.75, m.StreamingMs, "streaming");
                AssertEqual(2500.0, m.TotalMs, "total is duration_ms");
                AssertEqual(57.125, m.TokensPerSecond, "tokens/sec");
                AssertEqual(1050, m.PromptTokens, "input");
                AssertEqual(120, m.CompletionTokens, "output");
                AssertEqual(900, m.CachedTokens, "cached");
                AssertEqual(1170, m.TotalTokens, "total tokens derived");
                AssertEqual(false, m.TokensEstimated, "reported");
                AssertTrue(m.CostUsd.HasValue && Math.Abs(m.CostUsd.Value - 0.0123) < 1e-9, "cost");
                AssertEqual(2, m.ToolCallCount, "tool calls");
                AssertEqual(812.5, m.ToolTimeMs, "tool time");

                // A runtime without usage: only timing and an estimate are stored; the rest stays null.
                AskMessage estimated = NewMessage(thread, "plain");
                estimated.Role = AskMessageRoleEnum.Assistant;
                estimated.DurationMs = 900;
                estimated.Metrics = new CaptainChatMetrics { TimeToFirstTokenMs = 300, TimeToFirstTextMs = 300, StreamingMs = 600, CompletionTokens = 2, TokensEstimated = true, TokensPerSecond = 3.3333, ToolCallCount = 0, ToolTimeMs = 0 };
                estimated = await db.AskMessages.CreateAsync(estimated, true).ConfigureAwait(false);

                AskMessagePage page = await db.AskMessages.EnumerateAsync(Constants.DefaultTenantId, thread.Id, null, 10).ConfigureAwait(false);
                AskMessage pagedQuestion = page.Messages.Single(x => x.Id == question.Id);
                AskMessage pagedReply = page.Messages.Single(x => x.Id == reply.Id);
                AskMessage pagedEstimated = page.Messages.Single(x => x.Id == estimated.Id);
                AssertNull(pagedQuestion.Metrics, "a user message has no telemetry");
                AssertEqual(120, pagedReply.Metrics!.CompletionTokens, "enumerate reads the columns too");
                AssertEqual(true, pagedEstimated.Metrics!.TokensEstimated, "estimate flag");
                AssertNull(pagedEstimated.Metrics.PromptTokens, "no input reported");
                AssertNull(pagedEstimated.Metrics.CachedTokens, "no cache reported");
                AssertNull(pagedEstimated.Metrics.CostUsd, "no cost reported");
                AssertNull(pagedEstimated.Metrics.TotalTokens, "no total without input");
                AssertEqual(0, pagedEstimated.Metrics.ToolCallCount, "zero tool calls stored as zero");

                // Clearing the metrics clears every column.
                pagedReply.Metrics = null;
                await db.AskMessages.UpdateAsync(pagedReply).ConfigureAwait(false);
                AskMessage? cleared = await db.AskMessages.ReadAsync(Constants.DefaultTenantId, reply.Id).ConfigureAwait(false);
                AssertNull(cleared!.Metrics, "cleared");
                AssertEqual(2500L, cleared.DurationMs, "duration kept");
            }));

            cases.Add(CaseAsync("message_sequence_monotonic_under_concurrency", "Concurrent appends to one thread get unique, gap-free sequences", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AskThread thread = await db.AskThreads.CreateAsync(NewThread("usr_conc")).ConfigureAwait(false);
                List<Task<AskMessage>> appends = new List<Task<AskMessage>>();
                for (int i = 0; i < 25; i++)
                {
                    int n = i;
                    appends.Add(Task.Run(() => db.AskMessages.CreateAsync(NewMessage(thread, "milestone " + n), true)));
                }

                AskMessage[] created = await Task.WhenAll(appends).ConfigureAwait(false);
                List<int> sequences = created.Select(m => m.Sequence).OrderBy(s => s).ToList();
                AssertEqual(25, sequences.Distinct().Count(), "unique sequences");
                AssertEqual(1, sequences.First(), "starts at 1");
                AssertEqual(25, sequences.Last(), "no gaps");

                AskThread? after = await db.AskThreads.ReadByIdAsync(thread.Id).ConfigureAwait(false);
                AssertEqual(25, after!.MessageCount, "count");
                AssertEqual(25, after.UnreadCount, "unread");
            }));

            cases.Add(CaseAsync("message_paging", "Message pages go backwards by sequence and report HasMore", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AskThread thread = await db.AskThreads.CreateAsync(NewThread("usr_page")).ConfigureAwait(false);
                for (int i = 1; i <= 5; i++) await db.AskMessages.CreateAsync(NewMessage(thread, "m" + i), false).ConfigureAwait(false);

                AskMessagePage newest = await db.AskMessages.EnumerateAsync(Constants.DefaultTenantId, thread.Id, null, 2).ConfigureAwait(false);
                AssertEqual(2, newest.Messages.Count, "page size");
                AssertEqual(4, newest.Messages[0].Sequence, "ascending order within the page");
                AssertEqual(5, newest.Messages[1].Sequence, "newest last");
                AssertTrue(newest.HasMore, "older messages exist");

                AskMessagePage older = await db.AskMessages.EnumerateAsync(Constants.DefaultTenantId, thread.Id, 2, 10).ConfigureAwait(false);
                AssertEqual(1, older.Messages.Count, "before sequence 2");
                AssertEqual("m1", older.Messages[0].ContentText);
                AssertFalse(older.HasMore, "nothing older");

                AskMessagePage foreign = await db.AskMessages.EnumerateAsync("ten_other", thread.Id, null, 10).ConfigureAwait(false);
                AssertEqual(0, foreign.Messages.Count, "tenant scoped");
            }));

            cases.Add(CaseAsync("message_create_missing_thread_throws", "Appending to a missing thread throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AskThread ghost = NewThread("usr_ghost");
                bool threw = false;
                try { await db.AskMessages.CreateAsync(NewMessage(ghost, "x"), false).ConfigureAwait(false); }
                catch (KeyNotFoundException) { threw = true; }
                AssertTrue(threw, "KeyNotFoundException expected");
            }));

            cases.Add(CaseAsync("tool_calls_roundtrip", "Tool calls round-trip per message", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AskThread thread = await db.AskThreads.CreateAsync(NewThread("usr_tc")).ConfigureAwait(false);
                AskMessage a = await db.AskMessages.CreateAsync(NewMessage(thread, "a"), false).ConfigureAwait(false);
                AskMessage b = await db.AskMessages.CreateAsync(NewMessage(thread, "b"), false).ConfigureAwait(false);

                AskMessageToolCall okCall = NewToolCall(a, "enumerate");
                okCall.Ok = true;
                okCall.ElapsedMs = 42;
                okCall.ArgumentsText = "{\"entityType\":\"voyage\"}";
                okCall.ResultText = "{\"objects\":[]}";
                AskMessageToolCall unknown = NewToolCall(b, "dispatch");
                unknown.Ok = null;
                AskMessageToolCall failed = NewToolCall(b, "cancel_voyage");
                failed.Ok = false;
                await db.AskMessageToolCalls.CreateManyAsync(new List<AskMessageToolCall> { okCall, unknown, failed }).ConfigureAwait(false);

                List<AskMessageToolCall> forA = await db.AskMessageToolCalls.EnumerateByMessagesAsync(Constants.DefaultTenantId, thread.Id, new List<string> { a.Id }).ConfigureAwait(false);
                AssertEqual(1, forA.Count, "one call on a");
                AssertEqual("enumerate", forA[0].ToolName);
                AssertEqual(true, forA[0].Ok);
                AssertEqual(42L, forA[0].ElapsedMs);
                AssertEqual("{\"entityType\":\"voyage\"}", forA[0].ArgumentsText);

                List<AskMessageToolCall> both = await db.AskMessageToolCalls.EnumerateByMessagesAsync(Constants.DefaultTenantId, thread.Id, new List<string> { a.Id, b.Id }).ConfigureAwait(false);
                AssertEqual(3, both.Count, "three calls");
                AssertNull(both.First(c => c.ToolName == "dispatch").Ok, "null ok preserved");
                AssertEqual(false, both.First(c => c.ToolName == "cancel_voyage").Ok);
            }));

            cases.Add(CaseAsync("proposal_roundtrip_and_cas", "Proposals round-trip and transitions are compare-and-set", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AskThread thread = await db.AskThreads.CreateAsync(NewThread("usr_prop")).ConfigureAwait(false);
                AskActionProposal proposal = NewProposal(thread, "dispatch");
                proposal.ArgumentsText = "{\"title\":\"x\"}";
                proposal.SummaryText = "Dispatch voyage x";
                proposal.Source = AskProposalSourceEnum.QuickAction;
                await db.AskActionProposals.CreateAsync(proposal).ConfigureAwait(false);

                AskActionProposal? read = await db.AskActionProposals.ReadAsync(Constants.DefaultTenantId, proposal.Id).ConfigureAwait(false);
                AssertEqual(AskProposalStatusEnum.Pending, read!.Status);
                AssertEqual(AskProposalSourceEnum.QuickAction, read.Source);
                AssertEqual("{\"title\":\"x\"}", read.ArgumentsText);
                AssertEqual("Dispatch voyage x", read.SummaryText);
                AssertNull(await db.AskActionProposals.ReadAsync("ten_other", proposal.Id).ConfigureAwait(false), "tenant scoped");

                List<Task<bool>> racers = new List<Task<bool>>();
                for (int i = 0; i < 8; i++)
                    racers.Add(Task.Run(() => db.AskActionProposals.TryTransitionAsync(Constants.DefaultTenantId, proposal.Id, AskProposalStatusEnum.Pending, AskProposalStatusEnum.Approved, "usr_prop")));
                bool[] outcomes = await Task.WhenAll(racers).ConfigureAwait(false);
                AssertEqual(1, outcomes.Count(o => o), "exactly one transition wins");

                AskActionProposal? approved = await db.AskActionProposals.ReadAsync(Constants.DefaultTenantId, proposal.Id).ConfigureAwait(false);
                AssertEqual(AskProposalStatusEnum.Approved, approved!.Status);
                AssertEqual("usr_prop", approved.DecidedByUserId);
                AssertNotNull(approved.DecidedUtc, "decided utc");

                approved.Status = AskProposalStatusEnum.Executed;
                approved.ResultText = "{\"id\":\"vyg_1\"}";
                approved.ExecutedUtc = _Fixed;
                approved.MessageId = "amg_1";
                await db.AskActionProposals.UpdateAsync(approved).ConfigureAwait(false);
                AskActionProposal? executed = await db.AskActionProposals.ReadAsync(Constants.DefaultTenantId, proposal.Id).ConfigureAwait(false);
                AssertEqual(AskProposalStatusEnum.Executed, executed!.Status);
                AssertEqual("{\"id\":\"vyg_1\"}", executed.ResultText);
                AssertSameInstant(_Fixed, executed.ExecutedUtc!.Value, "executed utc");
                AssertEqual("amg_1", executed.MessageId);

                AssertFalse(await db.AskActionProposals.TryTransitionAsync(Constants.DefaultTenantId, proposal.Id, AskProposalStatusEnum.Pending, AskProposalStatusEnum.Rejected, "usr_prop").ConfigureAwait(false), "no longer pending");
            }));

            cases.Add(CaseAsync("proposal_queries", "Proposal queries filter by thread, status, and age", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AskThread thread = await db.AskThreads.CreateAsync(NewThread("usr_pq")).ConfigureAwait(false);
                AskActionProposal oldPending = NewProposal(thread, "dispatch"); oldPending.CreatedUtc = DateTime.UtcNow.AddHours(-3);
                AskActionProposal newPending = NewProposal(thread, "cancel_voyage");
                AskActionProposal rejected = NewProposal(thread, "delete_vessel"); rejected.Status = AskProposalStatusEnum.Rejected; rejected.CreatedUtc = DateTime.UtcNow.AddHours(-5);
                foreach (AskActionProposal p in new[] { oldPending, newPending, rejected }) await db.AskActionProposals.CreateAsync(p).ConfigureAwait(false);

                List<AskActionProposal> pending = await db.AskActionProposals.EnumerateByThreadAsync(Constants.DefaultTenantId, thread.Id, AskProposalStatusEnum.Pending).ConfigureAwait(false);
                AssertEqual(2, pending.Count, "two pending");
                AssertEqual(oldPending.Id, pending[0].Id, "oldest first");
                List<AskActionProposal> all = await db.AskActionProposals.EnumerateByThreadAsync(Constants.DefaultTenantId, thread.Id, null).ConfigureAwait(false);
                AssertEqual(3, all.Count, "all");

                List<AskActionProposal> expired = await db.AskActionProposals.EnumeratePendingBeforeAsync(DateTime.UtcNow.AddHours(-1)).ConfigureAwait(false);
                AssertTrue(expired.Any(p => p.Id == oldPending.Id), "old pending is due");
                AssertFalse(expired.Any(p => p.Id == newPending.Id), "new pending is not due");
                AssertFalse(expired.Any(p => p.Id == rejected.Id), "decided proposals never expire");
            }));

            cases.Add(CaseAsync("tracked_work_lifecycle", "Tracked work is create-or-get, updates, and is queryable while active", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AskThread thread = await db.AskThreads.CreateAsync(NewThread("usr_tw")).ConfigureAwait(false);
                AskTrackedWork first = await db.AskTrackedWork.CreateOrGetAsync(NewWork(thread, AskTrackedEntityTypeEnum.Voyage, "vyg_track")).ConfigureAwait(false);
                AskTrackedWork again = await db.AskTrackedWork.CreateOrGetAsync(NewWork(thread, AskTrackedEntityTypeEnum.Voyage, "vyg_track")).ConfigureAwait(false);
                AssertEqual(first.Id, again.Id, "same row for the same entity");
                AskTrackedWork job = await db.AskTrackedWork.CreateOrGetAsync(NewWork(thread, AskTrackedEntityTypeEnum.Job, "job_track")).ConfigureAwait(false);
                AssertNotEqual(first.Id, job.Id, "different entity gets its own row");

                List<AskTrackedWork> concurrent = (await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => db.AskTrackedWork.CreateOrGetAsync(NewWork(thread, AskTrackedEntityTypeEnum.Mission, "msn_race"))))).ConfigureAwait(false)).ToList();
                AssertEqual(1, concurrent.Select(w => w.Id).Distinct().Count(), "concurrent create-or-get yields one row");

                first.Status = "InProgress";
                first.SnapshotHash = "abc";
                first.LastChangeUtc = _Fixed;
                first.Title = "Voyage title";
                await db.AskTrackedWork.UpdateAsync(first).ConfigureAwait(false);
                AskTrackedWork? read = await db.AskTrackedWork.ReadAsync(Constants.DefaultTenantId, first.Id).ConfigureAwait(false);
                AssertEqual("InProgress", read!.Status);
                AssertEqual("abc", read.SnapshotHash);
                AssertEqual("Voyage title", read.Title);
                AssertSameInstant(_Fixed, read.LastChangeUtc!.Value, "last change");

                List<AskTrackedWork> byEntity = await db.AskTrackedWork.EnumerateActiveByEntityAsync(AskTrackedEntityTypeEnum.Voyage, "vyg_track").ConfigureAwait(false);
                AssertEqual(1, byEntity.Count, "active by entity");

                read.State = AskTrackedWorkStateEnum.Succeeded;
                read.CompletedUtc = _Fixed.AddMinutes(5);
                await db.AskTrackedWork.UpdateAsync(read).ConfigureAwait(false);
                AssertEqual(0, (await db.AskTrackedWork.EnumerateActiveByEntityAsync(AskTrackedEntityTypeEnum.Voyage, "vyg_track").ConfigureAwait(false)).Count, "finished rows are not active");
                List<AskTrackedWork> active = await db.AskTrackedWork.EnumerateActiveAsync().ConfigureAwait(false);
                AssertFalse(active.Any(w => w.Id == first.Id), "finished not in sweep");
                AssertTrue(active.Any(w => w.Id == job.Id), "job still active");

                List<AskTrackedWork> byThread = await db.AskTrackedWork.EnumerateByThreadAsync(Constants.DefaultTenantId, thread.Id).ConfigureAwait(false);
                AssertEqual(3, byThread.Count, "three tracked items");
                AssertEqual(0, (await db.AskTrackedWork.EnumerateByThreadAsync("ten_other", thread.Id).ConfigureAwait(false)).Count, "tenant scoped");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Ask Thread Database",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static AskThread NewThread(string userId)
        {
            AskThread thread = new AskThread();
            thread.TenantId = Constants.DefaultTenantId;
            thread.UserId = userId;
            return thread;
        }

        private static AskMessage NewMessage(AskThread thread, string content)
        {
            AskMessage message = new AskMessage();
            message.TenantId = thread.TenantId;
            message.UserId = thread.UserId;
            message.ThreadId = thread.Id;
            message.ContentText = content;
            return message;
        }

        private static AskMessageToolCall NewToolCall(AskMessage message, string toolName)
        {
            AskMessageToolCall call = new AskMessageToolCall();
            call.TenantId = message.TenantId;
            call.UserId = message.UserId;
            call.ThreadId = message.ThreadId;
            call.MessageId = message.Id;
            call.CallId = "call_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            call.ToolName = toolName;
            return call;
        }

        private static AskActionProposal NewProposal(AskThread thread, string toolName)
        {
            AskActionProposal proposal = new AskActionProposal();
            proposal.TenantId = thread.TenantId;
            proposal.UserId = thread.UserId;
            proposal.ThreadId = thread.Id;
            proposal.ToolName = toolName;
            proposal.SummaryText = toolName;
            return proposal;
        }

        private static AskTrackedWork NewWork(AskThread thread, AskTrackedEntityTypeEnum type, string entityId)
        {
            AskTrackedWork work = new AskTrackedWork();
            work.TenantId = thread.TenantId;
            work.UserId = thread.UserId;
            work.ThreadId = thread.Id;
            work.EntityType = type;
            work.EntityId = entityId;
            work.Title = entityId;
            return work;
        }

        private static void AssertSameInstant(DateTime expected, DateTime actual, string label)
        {
            double delta = Math.Abs((expected.ToUniversalTime() - actual.ToUniversalTime()).TotalMilliseconds);
            AssertTrue(delta < 1.0, label + ": expected " + expected.ToString("o") + " but was " + actual.ToString("o"));
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
