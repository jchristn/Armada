namespace Armada.Test.Unit.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    /// <summary>
    /// Database persistence tests for the Mission.TotalRuntimeSeconds property added in v0.11.0.
    /// Verifies that total runtime is correctly stored and retrieved across all CRUD operations.
    /// </summary>
    public class MissionTotalRuntimeDatabaseTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Test suite name.
        /// </summary>
        public override string Name => "Mission TotalRuntime Database";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run all mission total runtime database tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await Mission_TotalRuntime_CreateWithValue();
            await Mission_TotalRuntime_CreateWithoutValue();
            await Mission_TotalRuntime_ReadPreservesValue();
            await Mission_TotalRuntime_UpdateSetsValue();
            await Mission_TotalRuntime_UpdateClearsValue();
            await Mission_TotalRuntime_UpdateChangesValue();
            await Mission_TotalRuntime_EnumeratePreservesValue();
            await Mission_TotalRuntime_EnumerateByStatusPreservesValue();
        }

        #endregion

        #region Private-Methods

        private async Task<MissionTestPrerequisites> CreatePrerequisitesAsync(SqliteDatabaseDriver db)
        {
            Fleet fleet = new Fleet("Test Fleet");
            await db.Fleets.CreateAsync(fleet);

            Vessel vessel = new Vessel("Test Vessel", "https://github.com/test/repo");
            vessel.FleetId = fleet.Id;
            await db.Vessels.CreateAsync(vessel);

            Voyage voyage = new Voyage("Test Voyage", "Test voyage description");
            await db.Voyages.CreateAsync(voyage);

            return new MissionTestPrerequisites(fleet, vessel, voyage);
        }

        private async Task Mission_TotalRuntime_CreateWithValue()
        {
            await RunTest("Mission_TotalRuntime_CreateWithValue", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Runtime Create Test");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.VoyageId = prereqs.Voyage.Id;
                    mission.TotalRuntimeSeconds = 150.5;

                    Mission result = await db.Missions.CreateAsync(mission);

                    AssertNotNull(result);
                    AssertEqual(150.5, result.TotalRuntimeSeconds);
                }
            });
        }

        private async Task Mission_TotalRuntime_CreateWithoutValue()
        {
            await RunTest("Mission_TotalRuntime_CreateWithoutValue", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("No Runtime Create Test");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.VoyageId = prereqs.Voyage.Id;

                    Mission result = await db.Missions.CreateAsync(mission);

                    AssertNotNull(result);
                    AssertNull(result.TotalRuntimeSeconds);
                }
            });
        }

        private async Task Mission_TotalRuntime_ReadPreservesValue()
        {
            await RunTest("Mission_TotalRuntime_ReadPreservesValue", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Runtime Read Test");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.VoyageId = prereqs.Voyage.Id;
                    mission.TotalRuntimeSeconds = 300.25;
                    await db.Missions.CreateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertEqual(300.25, result!.TotalRuntimeSeconds);
                }
            });
        }

        private async Task Mission_TotalRuntime_UpdateSetsValue()
        {
            await RunTest("Mission_TotalRuntime_UpdateSetsValue", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Runtime Update Test");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.VoyageId = prereqs.Voyage.Id;
                    await db.Missions.CreateAsync(mission);
                    AssertNull(mission.TotalRuntimeSeconds);

                    mission.TotalRuntimeSeconds = 600.0;
                    mission.Status = MissionStatusEnum.Complete;
                    mission.CompletedUtc = DateTime.UtcNow;
                    await db.Missions.UpdateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertEqual(600.0, result!.TotalRuntimeSeconds);
                    AssertEqual(MissionStatusEnum.Complete, result.Status);
                }
            });
        }

        private async Task Mission_TotalRuntime_UpdateClearsValue()
        {
            await RunTest("Mission_TotalRuntime_UpdateClearsValue", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Runtime Clear Test");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.VoyageId = prereqs.Voyage.Id;
                    mission.TotalRuntimeSeconds = 200.0;
                    await db.Missions.CreateAsync(mission);

                    mission.TotalRuntimeSeconds = null;
                    await db.Missions.UpdateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertNull(result!.TotalRuntimeSeconds);
                }
            });
        }

        private async Task Mission_TotalRuntime_UpdateChangesValue()
        {
            await RunTest("Mission_TotalRuntime_UpdateChangesValue", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Runtime Change Test");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.VoyageId = prereqs.Voyage.Id;
                    mission.TotalRuntimeSeconds = 100.0;
                    await db.Missions.CreateAsync(mission);

                    mission.TotalRuntimeSeconds = 500.0;
                    await db.Missions.UpdateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertEqual(500.0, result!.TotalRuntimeSeconds);
                }
            });
        }

        private async Task Mission_TotalRuntime_EnumeratePreservesValue()
        {
            await RunTest("Mission_TotalRuntime_EnumeratePreservesValue", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission m1 = new Mission("Enum Runtime 1");
                    m1.VesselId = prereqs.Vessel.Id;
                    m1.TotalRuntimeSeconds = 100.0;

                    Mission m2 = new Mission("Enum Runtime 2");
                    m2.VesselId = prereqs.Vessel.Id;
                    m2.TotalRuntimeSeconds = 200.0;

                    Mission m3 = new Mission("Enum No Runtime");
                    m3.VesselId = prereqs.Vessel.Id;

                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);
                    await db.Missions.CreateAsync(m3);

                    List<Mission> all = await db.Missions.EnumerateAsync();
                    AssertEqual(3, all.Count);

                    Mission? withRuntime1 = all.Find(m => m.Title == "Enum Runtime 1");
                    AssertNotNull(withRuntime1);
                    AssertEqual(100.0, withRuntime1!.TotalRuntimeSeconds);

                    Mission? withRuntime2 = all.Find(m => m.Title == "Enum Runtime 2");
                    AssertNotNull(withRuntime2);
                    AssertEqual(200.0, withRuntime2!.TotalRuntimeSeconds);

                    Mission? noRuntime = all.Find(m => m.Title == "Enum No Runtime");
                    AssertNotNull(noRuntime);
                    AssertNull(noRuntime!.TotalRuntimeSeconds);
                }
            });
        }

        private async Task Mission_TotalRuntime_EnumerateByStatusPreservesValue()
        {
            await RunTest("Mission_TotalRuntime_EnumerateByStatusPreservesValue", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mComplete = new Mission("Complete With Runtime");
                    mComplete.VesselId = prereqs.Vessel.Id;
                    mComplete.Status = MissionStatusEnum.Complete;
                    mComplete.TotalRuntimeSeconds = 450.0;
                    mComplete.CompletedUtc = DateTime.UtcNow;

                    Mission mFailed = new Mission("Failed With Runtime");
                    mFailed.VesselId = prereqs.Vessel.Id;
                    mFailed.Status = MissionStatusEnum.Failed;
                    mFailed.TotalRuntimeSeconds = 30.0;

                    Mission mPending = new Mission("Pending No Runtime");
                    mPending.VesselId = prereqs.Vessel.Id;

                    await db.Missions.CreateAsync(mComplete);
                    await db.Missions.CreateAsync(mFailed);
                    await db.Missions.CreateAsync(mPending);

                    List<Mission> complete = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Complete);
                    AssertEqual(1, complete.Count);
                    AssertEqual(450.0, complete[0].TotalRuntimeSeconds);

                    List<Mission> failed = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Failed);
                    AssertEqual(1, failed.Count);
                    AssertEqual(30.0, failed[0].TotalRuntimeSeconds);

                    List<Mission> pending = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending);
                    AssertEqual(1, pending.Count);
                    AssertNull(pending[0].TotalRuntimeSeconds);
                }
            });
        }

        #endregion
    }
}
