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
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Database paging (docs/ENUMERATION.md) for the Ask, planning, refinement, and objective collections: every page is
    /// cut in the provider with a COUNT over the same filter and scope, pages never overlap or skip rows (rows with equal
    /// timestamps are ordered by id), a page past the end is empty with exact totals, and the objective filters (link ids,
    /// tags, free text, wildcards in the search term) run in SQL. Runs against every provider through the Database
    /// prefix; planning sessions exist on SQLite only, and the other providers must say so.
    /// </summary>
    public sealed class BoundedCollectionsDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.BoundedCollections";
        private static readonly DateTime _Fixed = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("ask_tracked_work_pages", "Tracked work pages by thread with exact totals, id tie-breaks, state filter, and tenant scope", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                AskThread thread = await db.AskThreads.CreateAsync(NewThread("usr_twp")).ConfigureAwait(false);
                for (int i = 0; i < 5; i++)
                {
                    AskTrackedWork work = new AskTrackedWork();
                    work.TenantId = thread.TenantId;
                    work.UserId = thread.UserId;
                    work.ThreadId = thread.Id;
                    work.EntityType = AskTrackedEntityTypeEnum.Mission;
                    work.EntityId = "msn_twp" + i;
                    work.Title = "work " + i;
                    work.State = i < 2 ? AskTrackedWorkStateEnum.Active : AskTrackedWorkStateEnum.Succeeded;
                    work.CreatedUtc = _Fixed;
                    await db.AskTrackedWork.CreateOrGetAsync(work).ConfigureAwait(false);
                }

                List<string> seen = new List<string>();
                for (int page = 1; page <= 3; page++)
                {
                    EnumerationResult<AskTrackedWork> result = await db.AskTrackedWork.EnumeratePageByThreadAsync(Constants.DefaultTenantId, thread.Id, null, Query(page, 2)).ConfigureAwait(false);
                    AssertEqual(5L, result.TotalRecords, "total on page " + page);
                    AssertEqual(3, result.TotalPages, "pages");
                    seen.AddRange(result.Objects.Select(w => w.Id));
                }

                AssertEqual(5, seen.Distinct().Count(), "three pages cover every row once even with equal timestamps");
                EnumerationResult<AskTrackedWork> past = await db.AskTrackedWork.EnumeratePageByThreadAsync(Constants.DefaultTenantId, thread.Id, null, Query(4, 2)).ConfigureAwait(false);
                AssertEqual(0, past.Objects.Count, "a page past the end is empty");
                AssertEqual(5L, past.TotalRecords, "with exact totals");

                EnumerationResult<AskTrackedWork> asc = await db.AskTrackedWork.EnumeratePageByThreadAsync(Constants.DefaultTenantId, thread.Id, null, Query(1, 5, EnumerationOrderEnum.CreatedAscending)).ConfigureAwait(false);
                EnumerationResult<AskTrackedWork> desc = await db.AskTrackedWork.EnumeratePageByThreadAsync(Constants.DefaultTenantId, thread.Id, null, Query(1, 5)).ConfigureAwait(false);
                AssertEqual(String.Join(",", asc.Objects.Select(w => w.Id)), String.Join(",", Enumerable.Reverse(desc.Objects).Select(w => w.Id)), "ascending is descending reversed (deterministic total order)");

                AssertEqual(2L, await db.AskTrackedWork.CountByThreadAsync(Constants.DefaultTenantId, thread.Id, AskTrackedWorkStateEnum.Active).ConfigureAwait(false), "active count");
                AssertEqual(5L, await db.AskTrackedWork.CountByThreadAsync(Constants.DefaultTenantId, thread.Id, null).ConfigureAwait(false), "count");
                EnumerationResult<AskTrackedWork> active = await db.AskTrackedWork.EnumeratePageByThreadAsync(Constants.DefaultTenantId, thread.Id, AskTrackedWorkStateEnum.Active, Query(1, 10)).ConfigureAwait(false);
                AssertEqual(2L, active.TotalRecords, "state filter counts before paging");
                AssertTrue(active.Objects.All(w => w.State == AskTrackedWorkStateEnum.Active), "state filter applied");
                AssertEqual(0L, (await db.AskTrackedWork.EnumeratePageByThreadAsync("ten_other", thread.Id, null, Query(1, 10)).ConfigureAwait(false)).TotalRecords, "another tenant sees none");
            }));

            cases.Add(CaseAsync("ask_proposal_pages", "Proposals page by thread with a status filter counted before paging", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                AskThread thread = await db.AskThreads.CreateAsync(NewThread("usr_pp")).ConfigureAwait(false);
                List<AskActionProposal> created = new List<AskActionProposal>();
                for (int i = 0; i < 4; i++)
                {
                    AskActionProposal proposal = new AskActionProposal();
                    proposal.TenantId = thread.TenantId;
                    proposal.UserId = thread.UserId;
                    proposal.ThreadId = thread.Id;
                    proposal.ToolName = "dispatch";
                    proposal.SummaryText = "p" + i;
                    proposal.CreatedUtc = _Fixed;
                    created.Add(await db.AskActionProposals.CreateAsync(proposal).ConfigureAwait(false));
                }

                AssertTrue(await db.AskActionProposals.TryTransitionAsync(Constants.DefaultTenantId, created[0].Id, AskProposalStatusEnum.Pending, AskProposalStatusEnum.Rejected, "usr_pp").ConfigureAwait(false), "one rejected");
                EnumerationResult<AskActionProposal> pending = await db.AskActionProposals.EnumeratePageByThreadAsync(Constants.DefaultTenantId, thread.Id, AskProposalStatusEnum.Pending, Query(1, 2)).ConfigureAwait(false);
                AssertEqual(3L, pending.TotalRecords, "three pending");
                AssertEqual(2, pending.Objects.Count, "first page of two");
                EnumerationResult<AskActionProposal> second = await db.AskActionProposals.EnumeratePageByThreadAsync(Constants.DefaultTenantId, thread.Id, AskProposalStatusEnum.Pending, Query(2, 2)).ConfigureAwait(false);
                AssertEqual(1, second.Objects.Count, "final page holds the rest");
                AssertFalse(second.Objects.Any(p => pending.Objects.Any(q => q.Id == p.Id)), "no overlap");
                AssertEqual(4L, await db.AskActionProposals.CountByThreadAsync(Constants.DefaultTenantId, thread.Id, null).ConfigureAwait(false), "all statuses");
                AssertEqual(3L, await db.AskActionProposals.CountByThreadAsync(Constants.DefaultTenantId, thread.Id, AskProposalStatusEnum.Pending).ConfigureAwait(false), "pending count");
            }));

            cases.Add(CaseAsync("ask_tool_call_pages_and_counts", "Tool calls page per message and are counted per message in one query", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                AskThread thread = await db.AskThreads.CreateAsync(NewThread("usr_tcp")).ConfigureAwait(false);
                AskMessage a = await db.AskMessages.CreateAsync(NewMessage(thread, "a"), false).ConfigureAwait(false);
                AskMessage b = await db.AskMessages.CreateAsync(NewMessage(thread, "b"), false).ConfigureAwait(false);
                AskMessage c = await db.AskMessages.CreateAsync(NewMessage(thread, "c"), false).ConfigureAwait(false);
                List<AskMessageToolCall> calls = new List<AskMessageToolCall>();
                for (int i = 0; i < 5; i++) calls.Add(NewToolCall(a, "tool_" + i));
                calls.Add(NewToolCall(b, "only"));
                await db.AskMessageToolCalls.CreateManyAsync(calls).ConfigureAwait(false);

                EnumerationResult<AskMessageToolCall> first = await db.AskMessageToolCalls.EnumeratePageByMessageAsync(Constants.DefaultTenantId, thread.Id, a.Id, Query(1, 3, EnumerationOrderEnum.CreatedAscending)).ConfigureAwait(false);
                EnumerationResult<AskMessageToolCall> rest = await db.AskMessageToolCalls.EnumeratePageByMessageAsync(Constants.DefaultTenantId, thread.Id, a.Id, Query(2, 3, EnumerationOrderEnum.CreatedAscending)).ConfigureAwait(false);
                AssertEqual(5L, first.TotalRecords, "five calls on a");
                AssertEqual(3, first.Objects.Count, "first page");
                AssertEqual(2, rest.Objects.Count, "final page");
                AssertEqual(5, first.Objects.Concat(rest.Objects).Select(x => x.Id).Distinct().Count(), "every call once");

                Dictionary<string, int> counts = await db.AskMessageToolCalls.CountByMessagesAsync(Constants.DefaultTenantId, thread.Id, new List<string> { a.Id, b.Id, c.Id }).ConfigureAwait(false);
                AssertEqual(5, counts[a.Id], "a");
                AssertEqual(1, counts[b.Id], "b");
                AssertFalse(counts.ContainsKey(c.Id), "a message without calls is absent");
                AssertEqual(0, (await db.AskMessageToolCalls.CountByMessagesAsync("ten_other", thread.Id, new List<string> { a.Id }).ConfigureAwait(false)).Count, "tenant scoped");
            }));

            cases.Add(CaseAsync("refinement_sessions_and_transcript_pages", "Refinement sessions page per objective and scope; transcripts page by sequence in both orders", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Captain captain = await db.Captains.CreateAsync(new Captain("bounded-refiner")).ConfigureAwait(false);
                string userRef = await CreateUserAsync(db, Constants.DefaultTenantId, "bounded-ref").ConfigureAwait(false);
                string userRef2 = await CreateUserAsync(db, Constants.DefaultTenantId, "bounded-ref2").ConfigureAwait(false);
                Objective objective = await db.Objectives.CreateAsync(NewObjective("Refine me", Constants.DefaultTenantId, userRef)).ConfigureAwait(false);
                Objective other = await db.Objectives.CreateAsync(NewObjective("Other", Constants.DefaultTenantId, userRef)).ConfigureAwait(false);
                List<ObjectiveRefinementSession> sessions = new List<ObjectiveRefinementSession>();
                for (int i = 0; i < 3; i++) sessions.Add(await db.ObjectiveRefinementSessions.CreateAsync(NewRefinement(objective, captain, i == 2 ? userRef2 : userRef)).ConfigureAwait(false));
                await db.ObjectiveRefinementSessions.CreateAsync(NewRefinement(other, captain, userRef)).ConfigureAwait(false);

                EnumerationResult<ObjectiveRefinementSession> all = await db.ObjectiveRefinementSessions.EnumeratePageByObjectiveAsync(objective.Id, Constants.DefaultTenantId, null, Query(1, 2)).ConfigureAwait(false);
                AssertEqual(3L, all.TotalRecords, "only this objective's sessions");
                AssertEqual(2, all.TotalPages, "two pages of two");
                EnumerationResult<ObjectiveRefinementSession> mine = await db.ObjectiveRefinementSessions.EnumeratePageByObjectiveAsync(objective.Id, Constants.DefaultTenantId, userRef, Query(1, 10)).ConfigureAwait(false);
                AssertEqual(2L, mine.TotalRecords, "user scope applies before counting");
                AssertEqual(0L, (await db.ObjectiveRefinementSessions.EnumeratePageByObjectiveAsync(objective.Id, "ten_other", null, Query(1, 10)).ConfigureAwait(false)).TotalRecords, "another tenant sees none");
                EnumerationResult<ObjectiveRefinementSession> stopped = await db.ObjectiveRefinementSessions.EnumeratePageByObjectiveAsync(objective.Id, null, null, new EnumerationQuery { Status = "Stopped" }).ConfigureAwait(false);
                AssertEqual(0L, stopped.TotalRecords, "status filter");

                ObjectiveRefinementSession session = sessions[0];
                for (int i = 1; i <= 5; i++)
                {
                    ObjectiveRefinementMessage message = new ObjectiveRefinementMessage();
                    message.ObjectiveRefinementSessionId = session.Id;
                    message.ObjectiveId = objective.Id;
                    message.TenantId = session.TenantId;
                    message.UserId = session.UserId;
                    message.Role = i % 2 == 1 ? "User" : "Assistant";
                    message.Sequence = i;
                    message.Content = "m" + i;
                    await db.ObjectiveRefinementMessages.CreateAsync(message).ConfigureAwait(false);
                }

                EnumerationResult<ObjectiveRefinementMessage> newest = await db.ObjectiveRefinementMessages.EnumeratePageBySessionAsync(session.Id, Query(1, 2)).ConfigureAwait(false);
                AssertEqual(5L, newest.TotalRecords, "transcript length");
                AssertEqual("m5,m4", String.Join(",", newest.Objects.Select(m => m.Content)), "newest first by default");
                EnumerationResult<ObjectiveRefinementMessage> oldest = await db.ObjectiveRefinementMessages.EnumeratePageBySessionAsync(session.Id, Query(3, 2, EnumerationOrderEnum.CreatedAscending)).ConfigureAwait(false);
                AssertEqual("m5", String.Join(",", oldest.Objects.Select(m => m.Content)), "final ascending page");
                AssertEqual(5L, await db.ObjectiveRefinementMessages.CountBySessionAsync(session.Id).ConfigureAwait(false), "count");
                AssertEqual(0L, await db.ObjectiveRefinementMessages.CountBySessionAsync(sessions[1].Id).ConfigureAwait(false), "empty transcript");
            }));

            cases.Add(CaseAsync("objective_pages_filters_and_scope", "Objective pages filter in SQL: link ids, tags, free text, enums, wildcards, scope, and max rank", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string userObj = await CreateUserAsync(db, Constants.DefaultTenantId, "bounded-obj").ConfigureAwait(false);
                string userOther = await CreateUserAsync(db, Constants.DefaultTenantId, "bounded-obj-other").ConfigureAwait(false);
                TenantMetadata otherTenant = await db.Tenants.CreateAsync(new TenantMetadata("Bounded other")).ConfigureAwait(false);
                string userForeign = await CreateUserAsync(db, otherTenant.Id, "bounded-foreign").ConfigureAwait(false);
                Objective alpha = NewObjective("Alpha login", Constants.DefaultTenantId, userObj);
                alpha.VesselIds = new List<string> { "vsl_alpha_1", "vsl_shared" };
                alpha.Tags = new List<string> { "Security", "50%_off" };
                alpha.AcceptanceCriteria = new List<string> { "Retries are idempotent" };
                alpha.Owner = "Joel";
                alpha.Rank = 7;
                alpha.Status = ObjectiveStatusEnum.Scoped;
                Objective beta = NewObjective("Beta billing", Constants.DefaultTenantId, userObj);
                beta.VesselIds = new List<string> { "vsl_shared" };
                beta.Tags = new List<string> { "50xyoff" };
                beta.Rank = 3;
                Objective gamma = NewObjective("Gamma", Constants.DefaultTenantId, userOther);
                gamma.Rank = 11;
                Objective foreign = NewObjective("Foreign", otherTenant.Id, userForeign);
                foreign.VesselIds = new List<string> { "vsl_shared" };
                foreach (Objective o in new[] { alpha, beta, gamma, foreign })
                {
                    o.LastUpdateUtc = _Fixed;
                    await db.Objectives.CreateAsync(o).ConfigureAwait(false);
                }

                AssertEqual(2L, (await db.Objectives.EnumeratePageAsync(Constants.DefaultTenantId, null, new ObjectiveQuery { VesselId = "vsl_shared" }).ConfigureAwait(false)).TotalRecords, "link id filter inside the tenant");
                AssertEqual(1L, (await db.Objectives.EnumeratePageAsync(Constants.DefaultTenantId, null, new ObjectiveQuery { VesselId = "VSL_ALPHA_1" }).ConfigureAwait(false)).TotalRecords, "link id match ignores case");
                AssertEqual(0L, (await db.Objectives.EnumeratePageAsync(Constants.DefaultTenantId, null, new ObjectiveQuery { VesselId = "vsl_alpha" }).ConfigureAwait(false)).TotalRecords, "a link id must match a whole element");
                AssertEqual(1L, (await db.Objectives.EnumeratePageAsync(Constants.DefaultTenantId, null, new ObjectiveQuery { Tag = "security" }).ConfigureAwait(false)).TotalRecords, "tag filter");
                AssertEqual(1L, (await db.Objectives.EnumeratePageAsync(Constants.DefaultTenantId, null, new ObjectiveQuery { Tag = "50%_off" }).ConfigureAwait(false)).TotalRecords, "LIKE wildcards in a tag are literal");
                AssertEqual(1L, (await db.Objectives.EnumeratePageAsync(Constants.DefaultTenantId, null, new ObjectiveQuery { Search = "idempotent" }).ConfigureAwait(false)).TotalRecords, "search reaches acceptance criteria");
                AssertEqual(1L, (await db.Objectives.EnumeratePageAsync(Constants.DefaultTenantId, null, new ObjectiveQuery { Search = "BILLING" }).ConfigureAwait(false)).TotalRecords, "search ignores case");
                AssertEqual(1L, (await db.Objectives.EnumeratePageAsync(Constants.DefaultTenantId, null, new ObjectiveQuery { Search = "%_" }).ConfigureAwait(false)).TotalRecords, "search wildcards are literal");
                AssertEqual(0L, (await db.Objectives.EnumeratePageAsync(Constants.DefaultTenantId, null, new ObjectiveQuery { Search = "," }).ConfigureAwait(false)).TotalRecords, "JSON punctuation alone does not match list syntax");
                AssertEqual(1L, (await db.Objectives.EnumeratePageAsync(Constants.DefaultTenantId, null, new ObjectiveQuery { Owner = "joe" }).ConfigureAwait(false)).TotalRecords, "owner contains");
                AssertEqual(1L, (await db.Objectives.EnumeratePageAsync(Constants.DefaultTenantId, null, new ObjectiveQuery { Status = ObjectiveStatusEnum.Scoped }).ConfigureAwait(false)).TotalRecords, "status");
                AssertEqual(2L, (await db.Objectives.EnumeratePageAsync(Constants.DefaultTenantId, userObj, new ObjectiveQuery()).ConfigureAwait(false)).TotalRecords, "user scope");
                AssertTrue((await db.Objectives.EnumeratePageAsync(null, null, new ObjectiveQuery()).ConfigureAwait(false)).TotalRecords >= 4, "every tenant");

                EnumerationResult<Objective> page1 = await db.Objectives.EnumeratePageAsync(Constants.DefaultTenantId, null, new ObjectiveQuery { PageNumber = 1, PageSize = 2 }).ConfigureAwait(false);
                EnumerationResult<Objective> page2 = await db.Objectives.EnumeratePageAsync(Constants.DefaultTenantId, null, new ObjectiveQuery { PageNumber = 2, PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(3L, page1.TotalRecords, "tenant total");
                AssertEqual(2, page1.Objects.Count, "first page");
                AssertEqual(1, page2.Objects.Count, "final page");
                AssertEqual(3, page1.Objects.Concat(page2.Objects).Select(o => o.Id).Distinct().Count(), "equal update times are ordered by id without overlap");
                AssertEqual(0, (await db.Objectives.EnumeratePageAsync(Constants.DefaultTenantId, null, new ObjectiveQuery { PageNumber = 3, PageSize = 2 }).ConfigureAwait(false)).Objects.Count, "past the end");
                ObjectiveQuery huge = new ObjectiveQuery { PageSize = 100000 };
                AssertEqual(CollectionLimits.MaxPageSize, huge.PageSize, "an oversized page size is normalized to 1000");

                AssertEqual(11, await db.Objectives.ReadMaxRankAsync(Constants.DefaultTenantId, null).ConfigureAwait(false), "max rank in the tenant");
                AssertEqual(7, await db.Objectives.ReadMaxRankAsync(Constants.DefaultTenantId, userObj).ConfigureAwait(false), "max rank for the user");
                AssertNull(await db.Objectives.ReadMaxRankAsync("ten_none", null).ConfigureAwait(false), "no objectives, no rank");
            }));

            cases.Add(CaseAsync("planning_session_pages", "Planning sessions and transcripts page on SQLite; the server providers report that planning is SQLite-only", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                if (!TestDatabaseConfig.IsSqlite)
                {
                    await AssertThrowsAsync<NotSupportedException>(() => db.PlanningSessions.EnumeratePageAsync(null, null, new EnumerationQuery())).ConfigureAwait(false);
                    await AssertThrowsAsync<NotSupportedException>(() => db.PlanningSessionMessages.EnumeratePageBySessionAsync("psn_x", new EnumerationQuery())).ConfigureAwait(false);
                    return;
                }

                string userPlan = await CreateUserAsync(db, Constants.DefaultTenantId, "bounded-plan").ConfigureAwait(false);
                string userPlan2 = await CreateUserAsync(db, Constants.DefaultTenantId, "bounded-plan2").ConfigureAwait(false);
                List<PlanningSession> created = new List<PlanningSession>();
                for (int i = 0; i < 3; i++)
                {
                    PlanningSession session = new PlanningSession { CaptainId = "cpt_p", VesselId = "vsl_p", Title = "plan " + i, Status = i == 0 ? PlanningSessionStatusEnum.Stopped : PlanningSessionStatusEnum.Active };
                    session.TenantId = Constants.DefaultTenantId;
                    session.UserId = i == 2 ? userPlan2 : userPlan;
                    created.Add(await db.PlanningSessions.CreateAsync(session).ConfigureAwait(false));
                }

                EnumerationResult<PlanningSession> all = await db.PlanningSessions.EnumeratePageAsync(Constants.DefaultTenantId, null, Query(1, 2)).ConfigureAwait(false);
                AssertEqual(3L, all.TotalRecords, "tenant total");
                AssertEqual(2, all.Objects.Count, "first page");
                AssertEqual(2L, (await db.PlanningSessions.EnumeratePageAsync(Constants.DefaultTenantId, userPlan, Query(1, 10)).ConfigureAwait(false)).TotalRecords, "user scope");
                AssertEqual(1L, (await db.PlanningSessions.EnumeratePageAsync(Constants.DefaultTenantId, null, new EnumerationQuery { Status = "Stopped" }).ConfigureAwait(false)).TotalRecords, "status filter");

                for (int i = 1; i <= 4; i++)
                {
                    PlanningSessionMessage message = new PlanningSessionMessage { PlanningSessionId = created[1].Id, Role = "User", Sequence = i, Content = "p" + i };
                    await db.PlanningSessionMessages.CreateAsync(message).ConfigureAwait(false);
                }

                EnumerationResult<PlanningSessionMessage> newest = await db.PlanningSessionMessages.EnumeratePageBySessionAsync(created[1].Id, Query(1, 3)).ConfigureAwait(false);
                AssertEqual(4L, newest.TotalRecords, "transcript length");
                AssertEqual("p4,p3,p2", String.Join(",", newest.Objects.Select(m => m.Content)), "newest first");
                AssertEqual("p1", String.Join(",", (await db.PlanningSessionMessages.EnumeratePageBySessionAsync(created[1].Id, Query(2, 3)).ConfigureAwait(false)).Objects.Select(m => m.Content)), "final page");
                AssertEqual(4L, await db.PlanningSessionMessages.CountBySessionAsync(created[1].Id).ConfigureAwait(false), "count");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Bounded collections (Ask, planning, refinement, objectives) paging", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static EnumerationQuery Query(int pageNumber, int pageSize, EnumerationOrderEnum order = EnumerationOrderEnum.CreatedDescending)
        {
            return new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize, Order = order };
        }

        private static async Task<string> CreateUserAsync(DatabaseDriver db, string tenantId, string name)
        {
            UserMaster user = new UserMaster(tenantId, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@example.com", "password");
            user = await db.Users.CreateAsync(user).ConfigureAwait(false);
            return user.Id;
        }

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
            call.CreatedUtc = _Fixed;
            return call;
        }

        private static Objective NewObjective(string title, string? tenantId, string? userId)
        {
            Objective objective = new Objective();
            objective.Title = title;
            objective.TenantId = tenantId;
            objective.UserId = userId;
            return objective;
        }

        private static ObjectiveRefinementSession NewRefinement(Objective objective, Captain captain, string userId)
        {
            ObjectiveRefinementSession session = new ObjectiveRefinementSession();
            session.ObjectiveId = objective.Id;
            session.TenantId = objective.TenantId;
            session.UserId = userId;
            session.CaptainId = captain.Id;
            session.Title = "Refine " + objective.Title;
            session.Status = ObjectiveRefinementSessionStatusEnum.Active;
            session.CreatedUtc = _Fixed;
            session.LastUpdateUtc = _Fixed;
            return session;
        }

        private static async Task AssertThrowsAsync<TException>(Func<Task> body) where TException : Exception
        {
            try
            {
                await body().ConfigureAwait(false);
            }
            catch (TException)
            {
                return;
            }

            throw new AssertionException("Expected " + typeof(TException).Name);
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
