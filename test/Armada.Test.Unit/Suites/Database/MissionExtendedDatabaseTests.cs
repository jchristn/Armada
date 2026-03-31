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
    /// Extended database tests for Mission covering persona, dependency, failure, and output properties.
    /// </summary>
    public class MissionExtendedDatabaseTests : TestSuite
    {
        /// <summary>
        /// Test suite name.
        /// </summary>
        public override string Name => "Mission Extended Database";

        /// <summary>
        /// Run all extended mission database tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("CreateAsync persists Persona", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Persona Mission");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.Persona = "Architect";
                    await db.Missions.CreateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertEqual("Architect", result!.Persona);
                }
            });

            await RunTest("CreateAsync persists DependsOnMissionId", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission parentMission = new Mission("Parent Mission");
                    parentMission.VesselId = prereqs.Vessel.Id;
                    await db.Missions.CreateAsync(parentMission);

                    Mission childMission = new Mission("Child Mission");
                    childMission.VesselId = prereqs.Vessel.Id;
                    childMission.DependsOnMissionId = parentMission.Id;
                    await db.Missions.CreateAsync(childMission);

                    Mission? result = await db.Missions.ReadAsync(childMission.Id);
                    AssertNotNull(result);
                    AssertEqual(parentMission.Id, result!.DependsOnMissionId);
                }
            });

            await RunTest("CreateAsync persists FailureReason", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Failed Mission");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.Status = MissionStatusEnum.Failed;
                    mission.FailureReason = "Process exited with code 1";
                    await db.Missions.CreateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertEqual("Process exited with code 1", result!.FailureReason);
                }
            });

            await RunTest("CreateAsync persists AgentOutput", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Output Mission");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.AgentOutput = "[ARMADA:PROGRESS] 100\nAll tasks complete.";
                    await db.Missions.CreateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertEqual("[ARMADA:PROGRESS] 100\nAll tasks complete.", result!.AgentOutput);
                }
            });

            await RunTest("CreateAsync persists CommitHash", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Commit Mission");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.CommitHash = "abc123def456789";
                    await db.Missions.CreateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertEqual("abc123def456789", result!.CommitHash);
                }
            });

            await RunTest("CreateAsync persists DiffSnapshot", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Diff Mission");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.DiffSnapshot = "diff --git a/file.cs b/file.cs\n+new line";
                    await db.Missions.CreateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertEqual("diff --git a/file.cs b/file.cs\n+new line", result!.DiffSnapshot);
                }
            });

            await RunTest("UpdateAsync modifies Persona", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Update Persona Mission");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.Persona = "Worker";
                    await db.Missions.CreateAsync(mission);

                    mission.Persona = "Judge";
                    await db.Missions.UpdateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertEqual("Judge", result!.Persona);
                }
            });

            await RunTest("UpdateAsync modifies FailureReason", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Update Failure Mission");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.Status = MissionStatusEnum.Failed;
                    await db.Missions.CreateAsync(mission);

                    mission.FailureReason = "Merge conflict detected";
                    await db.Missions.UpdateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertEqual("Merge conflict detected", result!.FailureReason);
                }
            });

            await RunTest("UpdateAsync modifies AgentOutput", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Update Output Mission");
                    mission.VesselId = prereqs.Vessel.Id;
                    await db.Missions.CreateAsync(mission);

                    mission.AgentOutput = "Updated agent output text";
                    await db.Missions.UpdateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertEqual("Updated agent output text", result!.AgentOutput);
                }
            });

            await RunTest("UpdateAsync sets StartedUtc and CompletedUtc", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Timing Mission");
                    mission.VesselId = prereqs.Vessel.Id;
                    await db.Missions.CreateAsync(mission);

                    DateTime startTime = new DateTime(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);
                    DateTime endTime = new DateTime(2025, 6, 1, 10, 30, 0, DateTimeKind.Utc);
                    mission.StartedUtc = startTime;
                    mission.CompletedUtc = endTime;
                    mission.Status = MissionStatusEnum.Complete;
                    await db.Missions.UpdateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result!.StartedUtc);
                    AssertNotNull(result.CompletedUtc);
                    AssertEqual(MissionStatusEnum.Complete, result.Status);
                }
            });

            await RunTest("CreateAsync with null optional fields persists correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Minimal Mission");
                    mission.VesselId = prereqs.Vessel.Id;
                    await db.Missions.CreateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertNull(result!.Persona);
                    AssertNull(result.DependsOnMissionId);
                    AssertNull(result.FailureReason);
                    AssertNull(result.AgentOutput);
                    AssertNull(result.CommitHash);
                    AssertNull(result.DiffSnapshot);
                    AssertNull(result.PrUrl);
                    AssertNull(result.ProcessId);
                    AssertNull(result.DockId);
                    AssertNull(result.ParentMissionId);
                    AssertNull(result.StartedUtc);
                    AssertNull(result.CompletedUtc);
                }
            });

            await RunTest("UpdateAsync with PrUrl and CommitHash", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("PR Mission");
                    mission.VesselId = prereqs.Vessel.Id;
                    await db.Missions.CreateAsync(mission);

                    mission.PrUrl = "https://github.com/test/repo/pull/42";
                    mission.CommitHash = "deadbeefcafe1234";
                    mission.Status = MissionStatusEnum.Complete;
                    await db.Missions.UpdateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertEqual("https://github.com/test/repo/pull/42", result!.PrUrl);
                    AssertEqual("deadbeefcafe1234", result.CommitHash);
                }
            });
        }

        #region Private-Methods

        private async Task<MissionTestPrerequisites> CreatePrerequisitesAsync(SqliteDatabaseDriver db)
        {
            Fleet fleet = new Fleet("Test Fleet");
            await db.Fleets.CreateAsync(fleet).ConfigureAwait(false);

            Vessel vessel = new Vessel("Test Vessel", "https://github.com/test/repo");
            vessel.FleetId = fleet.Id;
            await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            Voyage voyage = new Voyage("Test Voyage", "Test voyage description");
            await db.Voyages.CreateAsync(voyage).ConfigureAwait(false);

            return new MissionTestPrerequisites(fleet, vessel, voyage);
        }

        #endregion
    }
}
