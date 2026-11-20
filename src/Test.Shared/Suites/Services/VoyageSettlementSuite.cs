namespace Test.Shared.Suites.Services
{
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
    /// Tests terminal voyage settlement and its stored event.
    /// </summary>
    public sealed class VoyageSettlementSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.VoyageSettlement";

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "completed_voyage_records_one_event",
                displayName: "A completed voyage records one voyage.completed event",
                executeAsync: async (CancellationToken token) =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    DatabaseDriver database = testDb.Driver;
                    Voyage voyage = await database.Voyages.CreateAsync(new Voyage("completed") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                    await database.Missions.CreateAsync(new Mission("finished")
                    {
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
    }
}
