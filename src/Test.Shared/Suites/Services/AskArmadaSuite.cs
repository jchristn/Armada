namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="AskArmadaService"/>: intent routing over live fleet state. Positive
    /// cases assert that recognized intents (help, status, captains, failures) produce the expected
    /// answer kind and content; negative cases assert graceful handling of empty and unrecognized input.
    /// </summary>
    public sealed class AskArmadaSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.AskArmada";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Ask Armada suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("constructor_null_database_throws", "AskArmadaService NullDatabase Throws", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() => new AskArmadaService(null!, null!, CreateLogging()));
            }));

            cases.Add(CaseAsync("help_intent_returns_help", "Ask Help ReturnsHelp", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    AskArmadaService ask = CreateAsk(testDb.Driver);
                    AskResponse response = await ask.AskAsync("what can you do?", AuthContext.Authenticated("ten_ask", "usr_ask", true, false, "Test"));
                    AssertEqual(AskResponseKindEnum.Help, response.Kind);
                    AssertTrue(response.Links.Count > 0, "Help should offer links");
                }
            }));

            cases.Add(CaseAsync("empty_message_returns_help", "Ask EmptyMessage ReturnsHelp", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    AskArmadaService ask = CreateAsk(testDb.Driver);
                    AskResponse response = await ask.AskAsync("   ", AuthContext.Authenticated("ten_ask", "usr_ask", true, false, "Test"));
                    AssertEqual(AskResponseKindEnum.Help, response.Kind);
                }
            }));

            cases.Add(CaseAsync("unknown_intent_returns_unknown", "Ask UnknownIntent ReturnsUnknown", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    AskArmadaService ask = CreateAsk(testDb.Driver);
                    AskResponse response = await ask.AskAsync("xyzzy plugh frobnicate", AuthContext.Authenticated("ten_ask", "usr_ask", true, false, "Test"));
                    AssertEqual(AskResponseKindEnum.Unknown, response.Kind);
                }
            }));

            cases.Add(CaseAsync("status_intent_summarizes_counts", "Ask Status SummarizesCounts", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain idle = new Captain("idle-1") { State = CaptainStateEnum.Idle };
                    Captain working = new Captain("working-1") { State = CaptainStateEnum.Working };
                    await db.Captains.CreateAsync(idle);
                    await db.Captains.CreateAsync(working);

                    AskArmadaService ask = CreateAsk(db);
                    AskResponse response = await ask.AskAsync("how are things going?", AuthContext.Authenticated("ten_ask", "usr_ask", true, false, "Test"));
                    AssertEqual(AskResponseKindEnum.Answer, response.Kind);
                    AssertContains("2 captains", response.Reply);
                }
            }));

            cases.Add(CaseAsync("captains_intent_answers", "Ask Captains Answers", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    await db.Captains.CreateAsync(new Captain("c1") { State = CaptainStateEnum.Idle });
                    AskArmadaService ask = CreateAsk(db);
                    AskResponse response = await ask.AskAsync("how many captains?", AuthContext.Authenticated("ten_ask", "usr_ask", true, false, "Test"));
                    AssertEqual(AskResponseKindEnum.Answer, response.Kind);
                    AssertContains("captains", response.Reply);
                }
            }));

            cases.Add(CaseAsync("failures_intent_counts_failed", "Ask Failures CountsFailed", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Mission m = new Mission("Broken") { Status = MissionStatusEnum.Failed };
                    await db.Missions.CreateAsync(m);
                    AskArmadaService ask = CreateAsk(db);
                    AskResponse response = await ask.AskAsync("any failures?", AuthContext.Authenticated("ten_ask", "usr_ask", true, false, "Test"));
                    AssertEqual(AskResponseKindEnum.Answer, response.Kind);
                    AssertContains("1 failed mission", response.Reply);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Ask Armada",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static AskArmadaService CreateAsk(DatabaseDriver db)
        {
            LoggingModule logging = CreateLogging();
            ArmadaSettings settings = CreateSettings();
            StubGitService git = new StubGitService();
            IDockService dockService = new DockService(logging, db, settings, git);
            ICaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            IMissionService missionService = new MissionService(logging, db, settings, dockService, captainService);
            IVoyageService voyageService = new VoyageService(logging, db);
            IAdmiralService admiral = new AdmiralService(logging, db, settings, captainService, missionService, voyageService, dockService);
            return new AskArmadaService(db, admiral, logging);
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));
            return settings;
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
