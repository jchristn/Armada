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

            cases.Add(CaseAsync("deployment_approval_environment_first", "A deployment approval is titled environment first, then deployment title, and carries both as typed fields", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Deployment both = await db.Deployments.CreateAsync(new Deployment { Title = "Release 2.3 hotfix", EnvironmentName = "production", Status = DeploymentStatusEnum.PendingApproval });
                    Deployment noEnvironment = await db.Deployments.CreateAsync(new Deployment { Title = "Ad hoc deploy", Status = DeploymentStatusEnum.PendingApproval });

                    InboxService inbox = new InboxService(db, CreateLogging());
                    List<InboxItem> items = await inbox.GetInboxAsync(AuthContext.Authenticated("ten_inbox", "usr_inbox", true, false, "Test"));

                    InboxItem first = items.Single(i => i.Kind == InboxItemKinds.DeploymentApproval && i.EntityId == both.Id);
                    AssertEqual("Deploy to production: Release 2.3 hotfix", first.Title, "environment first, then title");
                    AssertEqual("production", first.EnvironmentName, "typed environment name");
                    AssertEqual("Release 2.3 hotfix", first.DeploymentTitle, "typed deployment title");
                    AssertEqual("production", first.EntityName, "entity name is unchanged (environment)");

                    InboxItem second = items.Single(i => i.Kind == InboxItemKinds.DeploymentApproval && i.EntityId == noEnvironment.Id);
                    AssertEqual("Deploy: Ad hoc deploy", second.Title, "no environment leads with the title");
                    AssertNull(second.EnvironmentName, "no environment name");
                }
            }));

            cases.Add(CaseSync("deployment_approval_label_fallbacks", "DeploymentApprovalLabel falls back gracefully when the environment or title is missing", () =>
            {
                AssertEqual("Deploy to production: Release 2.3 hotfix", DeploymentApprovalLabel.Format("production", "Release 2.3 hotfix", "dpl_1"), "both");
                AssertEqual("Deploy to production", DeploymentApprovalLabel.Format(" production ", "  ", "dpl_1"), "no title");
                AssertEqual("Deploy: Release 2.3 hotfix", DeploymentApprovalLabel.Format(null, "Release 2.3 hotfix", "dpl_1"), "no environment");
                AssertEqual("Deploy: dpl_1", DeploymentApprovalLabel.Format("", null, "dpl_1"), "only the id");
                AssertEqual("Deployment", DeploymentApprovalLabel.Format(null, null, null), "nothing");
                string? template = null;
                DeploymentApprovalLabel.Format("production", "Release", "dpl_1", (t, args) => { template = t; return "x"; });
                AssertEqual(DeploymentApprovalLabel.EnvironmentAndTitleTemplate, template, "localizer receives the catalog key");
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

        private static TestCaseDescriptor CaseSync(string caseId, string displayName, Action body)
        {
            return CaseAsync(caseId, displayName, () =>
            {
                body();
                return Task.CompletedTask;
            });
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
