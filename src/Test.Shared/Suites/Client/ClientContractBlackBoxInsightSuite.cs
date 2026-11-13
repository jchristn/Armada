namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client contract against a live server for the black box insights (ArmadaClient.BlackBoxInsights.cs):
    /// overlap preview, reservations, brief lint, configuration doctor, recovery statistics, attempt diff, nearby
    /// successes, timeline and voyage cascade, with their 400 and 404 replies.
    /// </summary>
    public sealed class ClientContractBlackBoxInsightSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.BlackBoxInsights";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("prevention", "Overlap preview, reservations, brief lint and the doctor on a live vessel; unknown vessels are 404 and an empty preview is 400", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "insights");
                string id = setup.Vessel.Id;
                MissionOverlapPreview? preview = await c.PreviewMissionOverlapAsync(id, new MissionOverlapPreviewRequest { Title = "Edit readme", Description = "Touch only README.md" });
                AssertEqual(id, preview?.VesselId, "preview vessel");
                AssertTrue(preview!.LikelyFiles.Contains("README.md"), "scoped file is likely");
                await ClientContract.ExpectErrorAsync(() => c.PreviewMissionOverlapAsync(id, new MissionOverlapPreviewRequest()), "empty preview", 400, 400);
                await ClientContract.ExpectErrorAsync(() => c.PreviewMissionOverlapAsync("vsl_missing_insights", new MissionOverlapPreviewRequest { Title = "x" }), "unknown vessel", 404, 404);

                VesselReservationPage? reservations = await c.ListVesselReservationsAsync(id, new EnumerationQuery { PageSize = 5 });
                AssertEqual(id, reservations?.VesselId); AssertEqual(5, reservations!.PageSize, "page size sent");

                BriefLintResult? lint = await c.LintMissionBriefAsync(new BriefLintRequest { Title = "Next", Description = "Do this after mission 2 lands.", VesselId = id });
                AssertTrue(lint!.Findings.Any(f => f.RuleId == MissionBriefLinter.DependencyRule), "dependency flagged");
                await ClientContract.ExpectErrorAsync(() => c.LintMissionBriefAsync(new BriefLintRequest()), "empty brief", 400, 400);

                BlackBoxDoctorPage? doctor = await c.GetVesselBlackBoxDoctorAsync(id, new BlackBoxDoctorRequest { WindowDays = 14, PageSize = 3 });
                AssertEqual(14, doctor?.WindowDays, "window sent"); AssertEqual(3, doctor!.PageSize);
                await ClientContract.ExpectErrorAsync(() => c.GetVesselBlackBoxDoctorAsync(id, new BlackBoxDoctorRequest { WindowDays = 400 }), "window over 365", 400, 400);

                EnumerationResult<BlackBoxRecoveryStat>? stats = await c.GetBlackBoxRecoveryStatsAsync(new BlackBoxRecoveryStatsRequest { RuleId = "BBX-LAND-001", PageSize = 2 });
                AssertEqual(2, stats?.PageSize, "stats page");
            }));

            cases.Add(Case("forensics", "Attempt diff, nearby successes, timeline and cascade over seeded entries; unknown ids are 404", async (c, fx) =>
            {
                BlackBoxRuntime runtime = fx.Server.BlackBox ?? throw new InvalidOperationException("no black box runtime");
                Mission mission;
                Voyage voyage;
                using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
                {
                    voyage = await db.Voyages.CreateAsync(new Voyage("Insight voyage " + ClientContract.Suffix(), "v")).ConfigureAwait(false);
                    mission = new Mission("Insight mission " + ClientContract.Suffix(), "Fix it");
                    mission.VoyageId = voyage.Id;
                    mission.Status = MissionStatusEnum.Failed;
                    mission.FailureKind = MissionFailureKindEnum.ScopeViolation;
                    mission = await db.Missions.CreateAsync(mission).ConfigureAwait(false);
                }
                string first = (await runtime.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation, ctx => ctx.OutOfScopeFiles.Add("lib/a.cs")).ConfigureAwait(false))!;
                string second = (await runtime.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.NoOp).ConfigureAwait(false))!;
                await runtime.Queue.DrainAsync().ConfigureAwait(false);

                BlackBoxAttemptDiff? diff = await c.DiffBlackBoxEntriesAsync(first, second);
                AssertTrue(diff!.SameMission, "same mission"); AssertTrue(diff.Differences.Any(d => d.Field == "Step"), "step differs");
                await ClientContract.ExpectErrorAsync(() => c.DiffBlackBoxEntriesAsync(first, first), "same entry", 400, 400);
                await ClientContract.ExpectErrorAsync(() => c.DiffBlackBoxEntriesAsync(first, "bbx_missing"), "unknown entry", 404, 404);

                EnumerationResult<BlackBoxNearbySuccess>? nearby = await c.ListBlackBoxNearbySuccessesAsync(first, new EnumerationQuery { PageSize = 4 });
                AssertEqual(4, nearby?.PageSize, "nearby page");
                BlackBoxTimelinePage? timeline = await c.GetBlackBoxTimelineAsync(first);
                AssertEqual(first, timeline?.EntryId);
                AssertTrue(timeline!.Objects.Any(i => i.Kind == "entry.captured"), "entry milestone");
                for (int i = 1; i < timeline.Objects.Count; i++) AssertTrue(timeline.Objects[i - 1].TimestampUtc <= timeline.Objects[i].TimestampUtc, "oldest first by default");
                BlackBoxTimelinePage? newest = await c.GetBlackBoxTimelineAsync(first, new EnumerationQuery { Order = EnumerationOrderEnum.CreatedDescending, PageSize = 1 });
                AssertEqual(1, newest!.Objects.Count, "order and size sent");
                await ClientContract.ExpectErrorAsync(() => c.GetBlackBoxTimelineAsync("bbx_missing"), "unknown timeline", 404, 404);

                BlackBoxCascadeGraph? graph = await c.GetVoyageBlackBoxCascadeAsync(voyage.Id);
                AssertEqual(mission.Id, graph!.RootMissionId, "root failure"); AssertEqual(1L, graph.MissionCount);
                await ClientContract.ExpectErrorAsync(() => c.GetVoyageBlackBoxCascadeAsync("vyg_missing"), "unknown voyage", 404, 404);
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: black box insights (live server)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private TestCaseDescriptor Case(string caseId, string name, Func<ArmadaClient, E2EServerFixture, Task> body)
        {
            return ClientContract.Case(Suite, this, caseId, name, body);
        }

        private static async Task<DatabaseDriver> OpenDatabaseAsync(E2EServerFixture fx)
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
            DatabaseDriver db = DatabaseDriverFactory.Create(settings, quiet);
            await db.InitializeAsync().ConfigureAwait(false);
            return db;
        }

        #endregion
    }
}
