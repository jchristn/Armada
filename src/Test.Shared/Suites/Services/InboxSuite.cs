namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="InboxService"/>: aggregation of actionable items across missions and
    /// captains. Positive cases assert that reviews, failed landings, failed missions, and stalled
    /// captains surface with correct severity and most-urgent-first ordering; the empty case asserts a
    /// clean inbox when nothing needs attention.
    /// </summary>
    public sealed class InboxSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.Inbox";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Inbox suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("constructor_null_database_throws", "InboxService NullDatabase Throws", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() => new InboxService(null!, CreateLogging()));
            }));

            cases.Add(CaseAsync("empty_database_returns_empty_inbox", "Inbox EmptyDatabase ReturnsEmpty", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    InboxService inbox = new InboxService(testDb.Driver, CreateLogging());
                    List<InboxItem> items = await inbox.GetInboxAsync(AuthContext.Authenticated("ten_inbox", "usr_inbox", true, false, "Test"));
                    AssertEqual(0, items.Count);
                }
            }));

            cases.Add(CaseAsync("aggregates_actionable_items", "Inbox AggregatesActionableItems", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    await db.Missions.CreateAsync(new Mission("Needs review") { Status = MissionStatusEnum.Review });
                    await db.Missions.CreateAsync(new Mission("Could not land") { Status = MissionStatusEnum.LandingFailed });
                    await db.Missions.CreateAsync(new Mission("Broke") { Status = MissionStatusEnum.Failed });
                    await db.Captains.CreateAsync(new Captain("stuck-1") { State = CaptainStateEnum.Stalled });

                    InboxService inbox = new InboxService(db, CreateLogging());
                    List<InboxItem> items = await inbox.GetInboxAsync(AuthContext.Authenticated("ten_inbox", "usr_inbox", true, false, "Test"));

                    AssertEqual(4, items.Count);
                    AssertTrue(items.Any(i => i.Kind == "review"), "Should include the review");
                    AssertTrue(items.Any(i => i.Kind == "landing_failed"), "Should include the landing failure");
                    AssertTrue(items.Any(i => i.Kind == "failed"), "Should include the failed mission");
                    AssertTrue(items.Any(i => i.Kind == "stalled_captain"), "Should include the stalled captain");
                }
            }));

            cases.Add(CaseAsync("orders_critical_first", "Inbox OrdersCriticalFirst", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    await db.Missions.CreateAsync(new Mission("Broke") { Status = MissionStatusEnum.Failed });
                    await db.Missions.CreateAsync(new Mission("Could not land") { Status = MissionStatusEnum.LandingFailed });

                    InboxService inbox = new InboxService(db, CreateLogging());
                    List<InboxItem> items = await inbox.GetInboxAsync(AuthContext.Authenticated("ten_inbox", "usr_inbox", true, false, "Test"));

                    AssertTrue(items.Count >= 2, "Expected at least two items");
                    AssertEqual(InboxSeverityEnum.Critical, items[0].Severity);
                    AssertEqual("landing_failed", items[0].Kind);
                }
            }));

            cases.Add(CaseAsync("overdue_review_is_critical", "Inbox OverdueReview IsCritical", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Mission overdue = new Mission("Old review") { Status = MissionStatusEnum.Review };
                    overdue.ReviewDeadlineUtc = DateTime.UtcNow.AddHours(-1);
                    await db.Missions.CreateAsync(overdue);

                    InboxService inbox = new InboxService(db, CreateLogging());
                    List<InboxItem> items = await inbox.GetInboxAsync(AuthContext.Authenticated("ten_inbox", "usr_inbox", true, false, "Test"));

                    AssertEqual(1, items.Count);
                    AssertEqual(InboxSeverityEnum.Critical, items[0].Severity);
                }
            }));

            cases.Add(CaseAsync("pending_ask_proposals_for_caller_are_listed", "Inbox lists the caller's pending, unexpired Ask proposals with a link to the conversation", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    string tenantId = Constants.DefaultTenantId;
                    AskThread mine = await db.AskThreads.CreateAsync(new AskThread { TenantId = tenantId, UserId = "usr_ask_me" });
                    AskThread theirs = await db.AskThreads.CreateAsync(new AskThread { TenantId = tenantId, UserId = "usr_ask_other" });

                    AskActionProposal pending = await db.AskActionProposals.CreateAsync(NewProposal(mine, "dispatch", "Dispatch voyage Fix login to vessel gateway"));
                    AskActionProposal expired = NewProposal(mine, "cancel_voyage", "Cancel voyage");
                    expired.CreatedUtc = DateTime.UtcNow.AddMinutes(-90);
                    await db.AskActionProposals.CreateAsync(expired);
                    AskActionProposal rejected = NewProposal(mine, "delete_vessel", "Delete vessel");
                    rejected.Status = AskProposalStatusEnum.Rejected;
                    await db.AskActionProposals.CreateAsync(rejected);
                    await db.AskActionProposals.CreateAsync(NewProposal(theirs, "dispatch", "Someone else's dispatch"));

                    InboxService inbox = new InboxService(db, CreateLogging()) { AskProposalExpiryMinutes = 60 };
                    List<InboxItem> items = await inbox.GetInboxAsync(AuthContext.Authenticated(tenantId, "usr_ask_me", false, true, "Test"));

                    List<InboxItem> asks = items.Where(i => i.Kind == InboxItemKinds.AskProposal).ToList();
                    AssertEqual(1, asks.Count, "only the caller's pending, unexpired proposal");
                    AssertEqual(pending.Id, asks[0].EntityId);
                    AssertEqual("ask_proposal", asks[0].EntityType);
                    AssertEqual("/ask/" + mine.Id, asks[0].Href);
                    AssertEqual(InboxSeverityEnum.Warning, asks[0].Severity);
                    AssertTrue(asks[0].Title.Contains("Dispatch voyage Fix login to vessel gateway", StringComparison.Ordinal), "title carries the proposal summary");

                    List<InboxItem> noUser = await inbox.GetInboxAsync(AuthContext.Authenticated(tenantId, null!, true, true, "Test"));
                    AssertFalse(noUser.Any(i => i.Kind == InboxItemKinds.AskProposal), "an identity without a user has no Ask conversations");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Inbox",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static AskActionProposal NewProposal(AskThread thread, string toolName, string summary)
        {
            AskActionProposal proposal = new AskActionProposal();
            proposal.TenantId = thread.TenantId;
            proposal.UserId = thread.UserId;
            proposal.ThreadId = thread.Id;
            proposal.ToolName = toolName;
            proposal.SummaryText = summary;
            return proposal;
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
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
