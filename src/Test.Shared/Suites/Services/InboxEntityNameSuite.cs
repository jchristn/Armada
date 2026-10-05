namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Fragility remediation R4: inbox items carry the entity's display name in <see cref="InboxItem.EntityName"/> and
    /// a kind from <see cref="InboxItemKinds"/>, so clients never strip "Review: " style prefixes from the title.
    /// </summary>
    public sealed class InboxEntityNameSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.InboxEntityName";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("entity_name_and_kind_constants", "Inbox items carry EntityName and constant kinds", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    await db.Missions.CreateAsync(new Mission("Review: nested title") { Status = MissionStatusEnum.Review });
                    await db.Missions.CreateAsync(new Mission("Could not land") { Status = MissionStatusEnum.LandingFailed });
                    await db.Captains.CreateAsync(new Captain("stuck-1") { State = CaptainStateEnum.Stalled });

                    InboxService inbox = new InboxService(db, CreateLogging());
                    List<InboxItem> items = await inbox.GetInboxAsync(AuthContext.Authenticated("ten_inbox", "usr_inbox", true, false, "Test"));

                    InboxItem review = items.Single(i => i.Kind == InboxItemKinds.Review);
                    AssertEqual("Review: nested title", review.EntityName, "a title that itself starts with the prefix is kept whole");
                    AssertEqual("Could not land", items.Single(i => i.Kind == InboxItemKinds.LandingFailed).EntityName, "landing failed");
                    AssertEqual("stuck-1", items.Single(i => i.Kind == InboxItemKinds.StalledCaptain).EntityName, "captain");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Inbox entity names",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive });
        }

        #endregion
    }
}
