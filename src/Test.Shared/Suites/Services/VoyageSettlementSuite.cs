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
    /// Terminal status chosen by <see cref="VoyageSettlement.DecideAsync"/> once every mission has settled,
    /// and the voyage.completed event recorded when settlement applies it.
    /// </summary>
    public sealed class VoyageSettlementSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.VoyageSettlement";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("all_cancelled_settles_cancelled", "Every cancelled mission settles the voyage as Cancelled", TestTags.Positive, async () =>
            {
                VoyageStatusEnum? settled = await SettleAsync(MissionStatusEnum.Cancelled, MissionStatusEnum.Cancelled).ConfigureAwait(false);
                AssertEqual(VoyageStatusEnum.Cancelled, settled, "every mission cancelled");
            }));

            cases.Add(CaseAsync("cancelled_and_work_produced_without_complete_settles_failed", "Cancelled plus settled WorkProduced and no Complete settles the voyage as Failed", TestTags.Positive, async () =>
            {
                VoyageStatusEnum? settled = await SettleAsync(MissionStatusEnum.Cancelled, MissionStatusEnum.WorkProduced).ConfigureAwait(false);
                AssertEqual(VoyageStatusEnum.Failed, settled, "cancelled work with no completed mission");
            }));

            cases.Add(CaseAsync("complete_and_cancelled_settles_complete", "A completed mission with a cancelled mission settles the voyage as Complete", TestTags.Positive, async () =>
            {
                VoyageStatusEnum? settled = await SettleAsync(MissionStatusEnum.Complete, MissionStatusEnum.Cancelled).ConfigureAwait(false);
                AssertEqual(VoyageStatusEnum.Complete, settled, "completed mission present");
            }));

            cases.Add(CaseAsync("failed_mission_settles_failed", "A failed mission settles the voyage as Failed", TestTags.Positive, async () =>
            {
                VoyageStatusEnum? settled = await SettleAsync(MissionStatusEnum.Failed, MissionStatusEnum.Complete).ConfigureAwait(false);
                AssertEqual(VoyageStatusEnum.Failed, settled, "failed mission");
            }));

            cases.Add(CaseAsync("landing_failed_mission_settles_failed", "A landing-failed mission settles the voyage as Failed", TestTags.Positive, async () =>
            {
                VoyageStatusEnum? settled = await SettleAsync(MissionStatusEnum.LandingFailed, MissionStatusEnum.WorkProduced).ConfigureAwait(false);
                AssertEqual(VoyageStatusEnum.Failed, settled, "landing-failed mission");
            }));

            cases.Add(CaseAsync("work_produced_only_settles_complete", "Settled WorkProduced missions and no cancelled mission settle the voyage as Complete", TestTags.Positive, async () =>
            {
                VoyageStatusEnum? settled = await SettleAsync(MissionStatusEnum.WorkProduced).ConfigureAwait(false);
                AssertEqual(VoyageStatusEnum.Complete, settled, "work produced only");
            }));

            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "completed_voyage_records_one_event",
                displayName: "A completed voyage records one voyage.completed event",
                executeAsync: async (CancellationToken token) =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    DatabaseDriver database = testDb.Driver;
                    TenantMetadata tenant = await database.Tenants.CreateAsync(new TenantMetadata { Name = "voyage-owner" }, token).ConfigureAwait(false);
                    UserMaster user = await database.Users.CreateAsync(new UserMaster { TenantId = tenant.Id, Email = "voyage-owner@acceptance.invalid" }, token).ConfigureAwait(false);
                    Voyage voyage = await database.Voyages.CreateAsync(new Voyage("completed") { TenantId = tenant.Id, UserId = user.Id, Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                    await database.Missions.CreateAsync(new Mission("finished")
                    {
                        TenantId = tenant.Id,
                        UserId = user.Id,
                        VoyageId = voyage.Id,
                        Status = MissionStatusEnum.Complete
                    }).ConfigureAwait(false);

                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;
                    VoyageService service = new VoyageService(logging, database);
                    await service.CheckCompletionsAsync(token: token).ConfigureAwait(false);

                    Voyage? settled = await database.Voyages.ReadAsync(voyage.Id, token).ConfigureAwait(false);
                    AssertEqual(VoyageStatusEnum.Complete, settled!.Status);
                    List<ArmadaEvent> events = await database.Events.EnumerateByVoyageAsync(voyage.Id, 100, token).ConfigureAwait(false);
                    List<ArmadaEvent> completed = events.Where(evt => evt.EventType == "voyage.completed").ToList();
                    AssertEqual(1, completed.Count, "the transition stores one completion event");
                    AssertEqual(voyage.Id, completed[0].VoyageId);
                    AssertEqual("voyage", completed[0].EntityType);
                    AssertEqual(voyage.Id, completed[0].EntityId);
                    AssertEqual(tenant.Id, completed[0].TenantId, "the event stays in the voyage owner's tenant");
                    AssertEqual(user.Id, completed[0].UserId, "the event stays in the voyage owner's scope");

                    List<Mission> missions = await database.Missions.EnumerateByVoyageAsync(voyage.Id, token).ConfigureAwait(false);
                    await VoyageSettlement.ApplyAsync(database, settled!, VoyageStatusEnum.Complete, missions, token).ConfigureAwait(false);
                    events = await database.Events.EnumerateByVoyageAsync(voyage.Id, 100, token).ConfigureAwait(false);
                    AssertEqual(1, events.Count(evt => evt.EventType == "voyage.completed"), "settling an already-complete voyage does not duplicate the event");
                },
                tags: new List<string> { TestTags.Positive }));

            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "failed_and_cancelled_voyages_do_not_record_completion",
                displayName: "Failed and cancelled voyages do not record voyage.completed",
                executeAsync: async (CancellationToken token) =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    DatabaseDriver database = testDb.Driver;

                    Voyage failed = await database.Voyages.CreateAsync(new Voyage("failed") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                    Voyage cancelled = await database.Voyages.CreateAsync(new Voyage("cancelled") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);

                    await VoyageSettlement.ApplyAsync(database, failed, VoyageStatusEnum.Failed, new List<Mission>(), token).ConfigureAwait(false);
                    await VoyageSettlement.ApplyAsync(database, cancelled, VoyageStatusEnum.Cancelled, new List<Mission>(), token).ConfigureAwait(false);

                    List<ArmadaEvent> failedEvents = await database.Events.EnumerateByVoyageAsync(failed.Id, 100, token).ConfigureAwait(false);
                    List<ArmadaEvent> cancelledEvents = await database.Events.EnumerateByVoyageAsync(cancelled.Id, 100, token).ConfigureAwait(false);
                    AssertEqual(0, failedEvents.Count(evt => evt.EventType == "voyage.completed"));
                    AssertEqual(0, cancelledEvents.Count(evt => evt.EventType == "voyage.completed"));
                },
                tags: new List<string> { TestTags.Negative }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Voyage Settlement", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<VoyageStatusEnum?> SettleAsync(params MissionStatusEnum[] statuses)
        {
            List<Mission> missions = new List<Mission>();
            foreach (MissionStatusEnum status in statuses)
            {
                Mission mission = new Mission(status.ToString());
                mission.Status = status;
                missions.Add(mission);
            }

            // Completion is finished and no merge-queue entry is still open, so the decision is the terminal status.
            return await VoyageSettlement.DecideAsync(
                missions,
                _ => false,
                () => Task.FromResult(new HashSet<string>())).ConfigureAwait(false);
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
