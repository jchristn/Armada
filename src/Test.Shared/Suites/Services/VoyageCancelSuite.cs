namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
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
    /// Voyage cancellation as fleet actions perform it (<see cref="AdmiralFleetActionMissionDispatcher.CancelVoyageAsync"/>):
    /// Pending and Assigned missions are cancelled, missions already running or finished are left alone, a captain is
    /// released only when the cancelled mission was its only active work, and a finished voyage is not rewritten. The
    /// REST, MCP, and WebSocket cancel paths carry their own copies of this logic (see the report); the E2E voyage
    /// suites cover those surfaces' status codes and responses.
    /// </summary>
    public sealed class VoyageCancelSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.VoyageCancel";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("cancel_scopes_to_unstarted_missions", "Cancel affects Pending and Assigned missions only", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    AdmiralFleetActionMissionDispatcher dispatcher = CreateDispatcher(db);
                    Voyage voyage = await db.Voyages.CreateAsync(new Voyage("cancel-scope") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                    Mission pending = await CreateMissionAsync(db, voyage.Id, MissionStatusEnum.Pending, null).ConfigureAwait(false);
                    Mission assigned = await CreateMissionAsync(db, voyage.Id, MissionStatusEnum.Assigned, null).ConfigureAwait(false);
                    Mission running = await CreateMissionAsync(db, voyage.Id, MissionStatusEnum.InProgress, null).ConfigureAwait(false);
                    Mission produced = await CreateMissionAsync(db, voyage.Id, MissionStatusEnum.WorkProduced, null).ConfigureAwait(false);
                    Mission complete = await CreateMissionAsync(db, voyage.Id, MissionStatusEnum.Complete, null).ConfigureAwait(false);

                    await dispatcher.CancelVoyageAsync(voyage.Id).ConfigureAwait(false);

                    AssertEqual(MissionStatusEnum.Cancelled, (await db.Missions.ReadAsync(pending.Id).ConfigureAwait(false))!.Status);
                    AssertEqual(MissionStatusEnum.Cancelled, (await db.Missions.ReadAsync(assigned.Id).ConfigureAwait(false))!.Status);
                    AssertEqual(MissionStatusEnum.InProgress, (await db.Missions.ReadAsync(running.Id).ConfigureAwait(false))!.Status, "running work is not killed by a voyage cancel");
                    AssertEqual(MissionStatusEnum.WorkProduced, (await db.Missions.ReadAsync(produced.Id).ConfigureAwait(false))!.Status);
                    AssertEqual(MissionStatusEnum.Complete, (await db.Missions.ReadAsync(complete.Id).ConfigureAwait(false))!.Status);
                    Voyage? after = await db.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                    AssertEqual(VoyageStatusEnum.Cancelled, after!.Status);
                    AssertNotNull(after.CompletedUtc);
                }
            }));

            cases.Add(CaseAsync("cancel_releases_captain_only_without_other_work", "Captain is released only when the cancelled mission was its only active work", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    AdmiralFleetActionMissionDispatcher dispatcher = CreateDispatcher(db);
                    Voyage voyage = await db.Voyages.CreateAsync(new Voyage("cancel-release") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                    Voyage otherVoyage = await db.Voyages.CreateAsync(new Voyage("other") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);

                    Captain solo = await db.Captains.CreateAsync(new Captain("solo-" + Guid.NewGuid().ToString("N")) { State = CaptainStateEnum.Working }).ConfigureAwait(false);
                    Mission soloMission = await CreateMissionAsync(db, voyage.Id, MissionStatusEnum.Assigned, solo.Id).ConfigureAwait(false);
                    solo.CurrentMissionId = soloMission.Id;
                    solo.ProcessId = 4242;
                    await db.Captains.UpdateAsync(solo).ConfigureAwait(false);

                    Captain busy = await db.Captains.CreateAsync(new Captain("busy-" + Guid.NewGuid().ToString("N")) { State = CaptainStateEnum.Working }).ConfigureAwait(false);
                    Mission busyMission = await CreateMissionAsync(db, voyage.Id, MissionStatusEnum.Assigned, busy.Id).ConfigureAwait(false);
                    await CreateMissionAsync(db, otherVoyage.Id, MissionStatusEnum.InProgress, busy.Id).ConfigureAwait(false);
                    busy.CurrentMissionId = busyMission.Id;
                    await db.Captains.UpdateAsync(busy).ConfigureAwait(false);

                    await dispatcher.CancelVoyageAsync(voyage.Id).ConfigureAwait(false);

                    Captain? soloAfter = await db.Captains.ReadAsync(solo.Id).ConfigureAwait(false);
                    AssertEqual(CaptainStateEnum.Idle, soloAfter!.State, "captain with no other work released");
                    AssertNull(soloAfter.CurrentMissionId);
                    AssertNull(soloAfter.ProcessId);
                    Captain? busyAfter = await db.Captains.ReadAsync(busy.Id).ConfigureAwait(false);
                    AssertEqual(CaptainStateEnum.Working, busyAfter!.State, "captain still working another voyage keeps working");
                    AssertEqual(MissionStatusEnum.Cancelled, (await db.Missions.ReadAsync(busyMission.Id).ConfigureAwait(false))!.Status);
                }
            }));

            cases.Add(CaseAsync("cancel_finished_voyage_is_noop", "Cancelling a finished voyage changes nothing", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    AdmiralFleetActionMissionDispatcher dispatcher = CreateDispatcher(db);
                    Voyage voyage = await db.Voyages.CreateAsync(new Voyage("finished") { Status = VoyageStatusEnum.Complete }).ConfigureAwait(false);
                    Mission leftover = await CreateMissionAsync(db, voyage.Id, MissionStatusEnum.Pending, null).ConfigureAwait(false);

                    await dispatcher.CancelVoyageAsync(voyage.Id).ConfigureAwait(false);
                    await dispatcher.CancelVoyageAsync("vyg_does_not_exist").ConfigureAwait(false);

                    AssertEqual(VoyageStatusEnum.Complete, (await db.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false))!.Status, "history is not rewritten");
                    AssertEqual(MissionStatusEnum.Pending, (await db.Missions.ReadAsync(leftover.Id).ConfigureAwait(false))!.Status);
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Voyage Cancel", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static AdmiralFleetActionMissionDispatcher CreateDispatcher(DatabaseDriver db)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));
            StubGitService git = new StubGitService();
            IDockService docks = new DockService(logging, db, settings, git);
            ICaptainService captains = new CaptainService(logging, db, settings, git, docks);
            IMissionService missions = new MissionService(logging, db, settings, docks, captains);
            IVoyageService voyages = new VoyageService(logging, db);
            AdmiralService admiral = new AdmiralService(logging, db, settings, captains, missions, voyages, docks);
            return new AdmiralFleetActionMissionDispatcher(db, admiral);
        }

        private static async Task<Mission> CreateMissionAsync(DatabaseDriver db, string voyageId, MissionStatusEnum status, string? captainId)
        {
            Mission mission = new Mission("Mission " + status);
            mission.VoyageId = voyageId;
            mission.Status = status;
            mission.CaptainId = captainId;
            return await db.Missions.CreateAsync(mission).ConfigureAwait(false);
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
