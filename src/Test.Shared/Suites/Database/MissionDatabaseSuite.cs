namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Comprehensive CRUD and enumeration descriptors for mission database operations: create,
    /// read, update, existence, enumeration (all, paginated, by voyage, vessel, captain, status),
    /// delete, not-found read and existence paths, and parent-voyage/mission last-update
    /// propagation on create, update, and heartbeat. Each case builds a fresh SQLite store seeded
    /// with a fleet, vessel, and voyage.
    /// </summary>
    public sealed class MissionDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.MissionDatabase";

        private static readonly DateTime _BaseTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Mission Database suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("mission_failure_kind_and_wait_flag_round_trip", "Mission FailureKind and WaitForVoyageWorkers persist through create, update, read, and summary", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Failure Kind Mission", "desc");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.VoyageId = prereqs.Voyage.Id;
                    mission.Status = MissionStatusEnum.Failed;
                    mission.FailureKind = MissionFailureKindEnum.Compile;
                    mission.FailureReason = "Definition-of-Done gate failed (Compile): exit 1";
                    mission.WaitForVoyageWorkers = true;
                    mission.IsRescue = true;
                    mission.ReworkRound = 2;
                    mission.ReworkEscalated = true;
                    await db.Missions.CreateAsync(mission);

                    Mission? read = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(read);
                    AssertEqual(MissionFailureKindEnum.Compile, read!.FailureKind, "failure kind persists on create");
                    AssertTrue(read.WaitForVoyageWorkers, "wait flag persists on create");
                    AssertTrue(read.IsRescue, "rescue flag persists on create");
                    AssertEqual(2, read.ReworkRound, "rework round persists on create");
                    AssertTrue(read.ReworkEscalated, "escalated rework flag persists on create");

                    MissionSummary? summary = await db.Missions.ReadSummaryAsync(mission.Id);
                    AssertNotNull(summary);
                    AssertEqual(MissionFailureKindEnum.Compile, summary!.FailureKind, "failure kind appears in the summary projection");

                    read.FailureKind = MissionFailureKindEnum.JudgeRejected;
                    read.WaitForVoyageWorkers = false;
                    read.ReworkRound = 3;
                    read.ReworkEscalated = false;
                    await db.Missions.UpdateAsync(read);
                    Mission? updated = await db.Missions.ReadAsync(mission.Id);
                    AssertEqual(MissionFailureKindEnum.JudgeRejected, updated!.FailureKind, "failure kind persists on update");
                    AssertFalse(updated.WaitForVoyageWorkers, "wait flag clears on update");
                    AssertEqual(3, updated.ReworkRound, "rework round persists on update");
                    AssertFalse(updated.ReworkEscalated, "escalated rework flag clears on update");

                    updated.FailureKind = null;
                    await db.Missions.UpdateAsync(updated);
                    Mission? cleared = await db.Missions.ReadAsync(mission.Id);
                    AssertNull(cleared!.FailureKind, "failure kind clears to null");

                    Mission plain = new Mission("Plain Mission", "desc");
                    plain.VesselId = prereqs.Vessel.Id;
                    await db.Missions.CreateAsync(plain);
                    Mission? plainRead = await db.Missions.ReadAsync(plain.Id);
                    AssertNull(plainRead!.FailureKind, "a new mission has no failure kind");
                    AssertFalse(plainRead.WaitForVoyageWorkers, "a new mission does not wait for voyage workers");
                    AssertFalse(plainRead.IsRescue, "a new mission is not a rescue");
                    AssertEqual(0, plainRead.ReworkRound, "a new mission is not a rework");
                }
            }));

            cases.Add(CaseAsync("evaluation_flag_round_trips_on_missions_and_voyages", "IsEvaluation persists on create and update for missions and voyages, defaults to false, and survives enumeration", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    AssertFalse((await db.Voyages.ReadAsync(prereqs.Voyage.Id))!.IsEvaluation, "a voyage defaults to production");
                    Voyage evalVoyage = await db.Voyages.CreateAsync(new Voyage("Eval Voyage", "desc") { IsEvaluation = true });
                    Voyage? voyageRead = await db.Voyages.ReadAsync(evalVoyage.Id);
                    AssertTrue(voyageRead!.IsEvaluation, "voyage flag persists on create");
                    voyageRead.IsEvaluation = false;
                    await db.Voyages.UpdateAsync(voyageRead);
                    AssertFalse((await db.Voyages.ReadAsync(evalVoyage.Id))!.IsEvaluation, "voyage flag clears on update");
                    voyageRead.IsEvaluation = true;
                    await db.Voyages.UpdateAsync(voyageRead);
                    AssertTrue((await db.Voyages.ReadAsync(evalVoyage.Id))!.IsEvaluation, "voyage flag sets on update");

                    Mission mission = new Mission("Eval Mission", "desc");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.VoyageId = evalVoyage.Id;
                    mission.IsEvaluation = true;
                    await db.Missions.CreateAsync(mission);
                    Mission? read = await db.Missions.ReadAsync(mission.Id);
                    AssertTrue(read!.IsEvaluation, "mission flag persists on create");
                    AssertTrue((await db.Missions.EnumerateByVoyageAsync(evalVoyage.Id)).Single().IsEvaluation, "mission flag survives enumeration");
                    AssertTrue((await db.Missions.ReadSummaryAsync(mission.Id))!.IsEvaluation, "mission flag appears in the summary projection");
                    read.IsEvaluation = false;
                    await db.Missions.UpdateAsync(read);
                    AssertFalse((await db.Missions.ReadAsync(mission.Id))!.IsEvaluation, "mission flag clears on update");
                    read.IsEvaluation = true;
                    await db.Missions.UpdateAsync(read);
                    AssertTrue((await db.Missions.ReadAsync(mission.Id))!.IsEvaluation, "mission flag sets on update");

                    Mission plain = new Mission("Plain Mission", "desc");
                    plain.VesselId = prereqs.Vessel.Id;
                    await db.Missions.CreateAsync(plain);
                    AssertFalse((await db.Missions.ReadAsync(plain.Id))!.IsEvaluation, "a mission defaults to production");
                    AssertFalse((await db.Missions.ReadSummaryAsync(plain.Id))!.IsEvaluation, "a production summary is not flagged");
                }
            }));

            cases.Add(CaseAsync("mission_create_chain_is_atomic", "CreateChainAsync writes new missions and re-pointed dependencies together, and a chain that fails part-way leaves nothing behind", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    Mission judge = await db.Missions.CreateAsync(new Mission("Judge", "r") { VesselId = prereqs.Vessel.Id, VoyageId = prereqs.Voyage.Id });
                    Mission recorder = await db.Missions.CreateAsync(new Mission("Recorder", "r") { VesselId = prereqs.Vessel.Id, VoyageId = prereqs.Voyage.Id, DependsOnMissionId = judge.Id });

                    Mission rework = new Mission("Rework", "w") { VesselId = prereqs.Vessel.Id, VoyageId = prereqs.Voyage.Id, DependsOnMissionId = judge.Id, ReworkRound = 1 };
                    Mission nextJudge = new Mission("Judge again", "r") { VesselId = prereqs.Vessel.Id, VoyageId = prereqs.Voyage.Id, DependsOnMissionId = rework.Id };
                    await db.Missions.CreateChainAsync(new List<Mission> { rework, nextJudge }, new List<MissionDependencyUpdate> { new MissionDependencyUpdate(recorder.Id, nextJudge.Id) });
                    AssertEqual(1, (await db.Missions.ReadAsync(rework.Id))!.ReworkRound, "the rework was written");
                    AssertEqual(rework.Id, (await db.Missions.ReadAsync(nextJudge.Id))!.DependsOnMissionId, "the new reviewer follows the rework");
                    AssertEqual(nextJudge.Id, (await db.Missions.ReadAsync(recorder.Id))!.DependsOnMissionId, "the later stage follows the new reviewer");

                    // The second insert collides with an existing id, so the whole chain must roll back.
                    Mission orphan = new Mission("Would be orphaned", "w") { VesselId = prereqs.Vessel.Id, VoyageId = prereqs.Voyage.Id, DependsOnMissionId = judge.Id };
                    Mission duplicate = new Mission("Duplicate", "w") { VesselId = prereqs.Vessel.Id, VoyageId = prereqs.Voyage.Id };
                    duplicate.Id = rework.Id;
                    bool threw = false;
                    try
                    {
                        await db.Missions.CreateChainAsync(new List<Mission> { orphan, duplicate }, new List<MissionDependencyUpdate> { new MissionDependencyUpdate(recorder.Id, orphan.Id) });
                    }
                    catch (Exception)
                    {
                        threw = true;
                    }
                    AssertTrue(threw, "a failing insert fails the chain");
                    AssertNull(await db.Missions.ReadAsync(orphan.Id), "no part of a failed chain is written");
                    AssertEqual(nextJudge.Id, (await db.Missions.ReadAsync(recorder.Id))!.DependsOnMissionId, "the re-point of a failed chain is rolled back");
                }
            }));

            cases.Add(CaseAsync("cancelled_mission_is_not_revived_by_a_stale_update", "A plain update cannot move a Cancelled mission to another status except Pending (a restart); the caller sees Cancelled", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    Mission mission = await db.Missions.CreateAsync(new Mission("Racing completion", "d") { VesselId = prereqs.Vessel.Id, VoyageId = prereqs.Voyage.Id, Status = MissionStatusEnum.InProgress });

                    // A completion reads the mission, then an operator cancels it, then the completion writes its stale copy.
                    Mission stale = (await db.Missions.ReadAsync(mission.Id))!;
                    Mission cancel = (await db.Missions.ReadAsync(mission.Id))!;
                    cancel.Status = MissionStatusEnum.Cancelled;
                    await db.Missions.UpdateAsync(cancel);
                    stale.Status = MissionStatusEnum.WorkProduced;
                    stale.AgentOutput = "late output";
                    Mission returned = await db.Missions.UpdateAsync(stale);

                    AssertEqual(MissionStatusEnum.Cancelled, (await db.Missions.ReadAsync(mission.Id))!.Status, "the cancellation stands");
                    AssertEqual(MissionStatusEnum.Cancelled, returned.Status, "the stale writer is told the mission is Cancelled");

                    Mission restart = (await db.Missions.ReadAsync(mission.Id))!;
                    restart.Status = MissionStatusEnum.Pending;
                    await db.Missions.UpdateAsync(restart);
                    AssertEqual(MissionStatusEnum.Pending, (await db.Missions.ReadAsync(mission.Id))!.Status, "a restart may leave Cancelled");

                    Mission other = await db.Missions.CreateAsync(new Mission("Ordinary", "d") { VesselId = prereqs.Vessel.Id, Status = MissionStatusEnum.InProgress });
                    other.Status = MissionStatusEnum.Failed;
                    await db.Missions.UpdateAsync(other);
                    AssertEqual(MissionStatusEnum.Failed, (await db.Missions.ReadAsync(other.Id))!.Status, "other transitions are unaffected");
                }
            }));

            cases.Add(CaseAsync("mission_create", "Mission_Create", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    Vessel vessel = prereqs.Vessel;
                    Voyage voyage = prereqs.Voyage;

                    Mission mission = new Mission("Create Test Mission", "A test mission description");
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    mission.Status = MissionStatusEnum.Complete;
                    mission.Priority = 5;
                    mission.BranchName = "feature/test";
                    mission.RequiresReview = true;
                    mission.ReviewDenyAction = ReviewDenyActionEnum.FailPipeline;
                    mission.ReviewComment = "Approved with changes";
                    mission.ReviewedByUserId = "usr_test";
                    mission.ReviewRequestedUtc = _BaseTime.AddMinutes(5);
                    mission.ReviewedUtc = _BaseTime.AddMinutes(10);
                    mission.StartedUtc = _BaseTime;
                    mission.CompletedUtc = _BaseTime.AddMilliseconds(2500);

                    Mission result = await db.Missions.CreateAsync(mission);
                    Mission? read = await db.Missions.ReadAsync(mission.Id);

                    AssertNotNull(result);
                    AssertNotNull(read);
                    AssertStartsWith("msn_", result.Id);
                    AssertEqual("Create Test Mission", result.Title);
                    AssertEqual("A test mission description", result.Description);
                    AssertEqual(vessel.Id, result.VesselId);
                    AssertEqual(voyage.Id, result.VoyageId);
                    AssertEqual(MissionStatusEnum.Complete, result.Status);
                    AssertEqual(5, result.Priority);
                    AssertEqual("feature/test", result.BranchName);
                    AssertTrue(read!.RequiresReview, "Mission review gate should persist");
                    AssertEqual(ReviewDenyActionEnum.FailPipeline, read.ReviewDenyAction, "Mission deny action should persist");
                    AssertEqual("Approved with changes", read.ReviewComment, "Mission review comment should persist");
                    AssertEqual("usr_test", read.ReviewedByUserId, "Mission reviewer should persist");
                    AssertEqual(_BaseTime.AddMinutes(5), read.ReviewRequestedUtc, "Mission review-request timestamp should persist");
                    AssertEqual(_BaseTime.AddMinutes(10), read.ReviewedUtc, "Mission reviewed timestamp should persist");
                    long resultRuntimeMs = result.TotalRuntimeMs ?? throw new InvalidOperationException("Expected result.TotalRuntimeMs to be populated.");
                    long readRuntimeMs = read.TotalRuntimeMs ?? throw new InvalidOperationException("Expected read.TotalRuntimeMs to be populated.");
                    AssertEqual(2500L, resultRuntimeMs);
                    AssertEqual(2500L, readRuntimeMs);
                }
            }));

            cases.Add(CaseAsync("mission_read", "Mission_Read", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    Vessel vessel = prereqs.Vessel;
                    Voyage voyage = prereqs.Voyage;

                    Mission mission = new Mission("Read Test Mission");
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    mission.Priority = 10;
                    await db.Missions.CreateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);

                    AssertNotNull(result);
                    AssertEqual(mission.Id, result!.Id);
                    AssertEqual("Read Test Mission", result.Title);
                    AssertEqual(vessel.Id, result.VesselId);
                    AssertEqual(voyage.Id, result.VoyageId);
                    AssertEqual(MissionStatusEnum.Pending, result.Status);
                    AssertEqual(10, result.Priority);
                }
            }));

            cases.Add(CaseAsync("mission_update", "Mission_Update", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    Vessel vessel = prereqs.Vessel;
                    Voyage voyage = prereqs.Voyage;

                    Captain captain = new Captain("update-captain");
                    await db.Captains.CreateAsync(captain);

                    Mission mission = new Mission("Original Title");
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    await db.Missions.CreateAsync(mission);

                    mission.Title = "Updated Title";
                    mission.Description = "Updated description";
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.Priority = 1;
                    mission.CaptainId = captain.Id;
                    mission.BranchName = "feature/updated";
                    mission.StartedUtc = _BaseTime.AddHours(1);
                    mission.CompletedUtc = mission.StartedUtc.Value.AddMilliseconds(1750);
                    await db.Missions.UpdateAsync(mission);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);

                    AssertNotNull(result);
                    AssertEqual("Updated Title", result!.Title);
                    AssertEqual("Updated description", result.Description);
                    AssertEqual(MissionStatusEnum.InProgress, result.Status);
                    AssertEqual(1, result.Priority);
                    AssertEqual(captain.Id, result.CaptainId);
                    AssertEqual("feature/updated", result.BranchName);
                    AssertNotNull(result.StartedUtc);
                    AssertNotNull(result.CompletedUtc);
                    long runtimeMs = result.TotalRuntimeMs ?? throw new InvalidOperationException("Expected result.TotalRuntimeMs to be populated.");
                    AssertEqual(1750L, runtimeMs);
                }
            }));

            cases.Add(CaseAsync("mission_exists", "Mission_Exists", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    Vessel vessel = prereqs.Vessel;
                    Voyage voyage = prereqs.Voyage;

                    Mission mission = new Mission("Exists Test Mission");
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    await db.Missions.CreateAsync(mission);

                    bool exists = await db.Missions.ExistsAsync(mission.Id);
                    AssertTrue(exists);
                }
            }));

            cases.Add(CaseAsync("mission_enumerate", "Mission_Enumerate", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    Vessel vessel = prereqs.Vessel;
                    Voyage voyage = prereqs.Voyage;

                    Mission m1 = new Mission("Enumerate Mission 1");
                    m1.VesselId = vessel.Id;
                    m1.VoyageId = voyage.Id;
                    Mission m2 = new Mission("Enumerate Mission 2");
                    m2.VesselId = vessel.Id;
                    m2.VoyageId = voyage.Id;
                    Mission m3 = new Mission("Enumerate Mission 3");
                    m3.VesselId = vessel.Id;

                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);
                    await db.Missions.CreateAsync(m3);

                    List<Mission> all = await db.Missions.EnumerateAsync();

                    AssertEqual(3, all.Count);
                }
            }));

            cases.Add(CaseAsync("mission_enumerate_paginated", "Mission_EnumeratePaginated", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    Vessel vessel = prereqs.Vessel;
                    Voyage voyage = prereqs.Voyage;

                    for (int i = 0; i < 10; i++)
                    {
                        Mission mission = new Mission("Paginated Mission " + i.ToString("D2"));
                        mission.VesselId = vessel.Id;
                        mission.VoyageId = voyage.Id;
                        mission.CreatedUtc = _BaseTime.AddMinutes(i);
                        await db.Missions.CreateAsync(mission);
                    }

                    EnumerationQuery queryPage1 = new EnumerationQuery();
                    queryPage1.PageNumber = 1;
                    queryPage1.PageSize = 3;
                    queryPage1.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Mission> page1 = await db.Missions.EnumerateAsync(queryPage1);

                    AssertEqual(3, page1.Objects.Count);
                    AssertEqual(10, (int)page1.TotalRecords);
                    AssertEqual(4, page1.TotalPages);
                    AssertEqual(1, page1.PageNumber);
                    AssertEqual(3, page1.PageSize);

                    EnumerationQuery queryPage2 = new EnumerationQuery();
                    queryPage2.PageNumber = 2;
                    queryPage2.PageSize = 3;
                    queryPage2.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Mission> page2 = await db.Missions.EnumerateAsync(queryPage2);

                    AssertEqual(3, page2.Objects.Count);
                    AssertEqual(10, (int)page2.TotalRecords);

                    EnumerationQuery queryPage4 = new EnumerationQuery();
                    queryPage4.PageNumber = 4;
                    queryPage4.PageSize = 3;
                    queryPage4.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Mission> page4 = await db.Missions.EnumerateAsync(queryPage4);

                    AssertEqual(1, page4.Objects.Count);

                    EnumerationQuery queryPage5 = new EnumerationQuery();
                    queryPage5.PageNumber = 5;
                    queryPage5.PageSize = 3;
                    queryPage5.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Mission> page5 = await db.Missions.EnumerateAsync(queryPage5);

                    AssertEqual(0, page5.Objects.Count);

                    HashSet<string> allIds = new HashSet<string>();
                    foreach (Mission m in page1.Objects) allIds.Add(m.Id);
                    foreach (Mission m in page2.Objects) allIds.Add(m.Id);
                    AssertEqual(6, allIds.Count);
                }
            }));

            cases.Add(CaseAsync("mission_enumerate_by_voyage", "Mission_EnumerateByVoyage", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    Vessel vessel = prereqs.Vessel;
                    Voyage voyage = prereqs.Voyage;

                    Voyage voyage2 = new Voyage("Other Voyage");
                    await db.Voyages.CreateAsync(voyage2);

                    Mission m1 = new Mission("Voyage 1 Mission A");
                    m1.VesselId = vessel.Id;
                    m1.VoyageId = voyage.Id;
                    Mission m2 = new Mission("Voyage 1 Mission B");
                    m2.VesselId = vessel.Id;
                    m2.VoyageId = voyage.Id;
                    Mission m3 = new Mission("Voyage 2 Mission");
                    m3.VesselId = vessel.Id;
                    m3.VoyageId = voyage2.Id;
                    Mission m4 = new Mission("No Voyage Mission");
                    m4.VesselId = vessel.Id;

                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);
                    await db.Missions.CreateAsync(m3);
                    await db.Missions.CreateAsync(m4);

                    List<Mission> voyage1Missions = await db.Missions.EnumerateByVoyageAsync(voyage.Id);
                    AssertEqual(2, voyage1Missions.Count);

                    List<Mission> voyage2Missions = await db.Missions.EnumerateByVoyageAsync(voyage2.Id);
                    AssertEqual(1, voyage2Missions.Count);
                    AssertEqual("Voyage 2 Mission", voyage2Missions[0].Title);
                }
            }));

            cases.Add(CaseAsync("mission_enumerate_by_vessel", "Mission_EnumerateByVessel", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    Fleet fleet = prereqs.Fleet;
                    Vessel vessel = prereqs.Vessel;
                    Voyage voyage = prereqs.Voyage;

                    Vessel vessel2 = new Vessel("Second Vessel", "https://github.com/test/repo2");
                    vessel2.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel2);

                    Mission m1 = new Mission("Vessel 1 Mission");
                    m1.VesselId = vessel.Id;
                    m1.VoyageId = voyage.Id;
                    Mission m2 = new Mission("Vessel 2 Mission A");
                    m2.VesselId = vessel2.Id;
                    Mission m3 = new Mission("Vessel 2 Mission B");
                    m3.VesselId = vessel2.Id;
                    Mission m4 = new Mission("No Vessel Mission");

                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);
                    await db.Missions.CreateAsync(m3);
                    await db.Missions.CreateAsync(m4);

                    List<Mission> vessel1Missions = await db.Missions.EnumerateByVesselAsync(vessel.Id);
                    AssertEqual(1, vessel1Missions.Count);
                    AssertEqual("Vessel 1 Mission", vessel1Missions[0].Title);

                    List<Mission> vessel2Missions = await db.Missions.EnumerateByVesselAsync(vessel2.Id);
                    AssertEqual(2, vessel2Missions.Count);
                }
            }));

            cases.Add(CaseAsync("mission_enumerate_by_captain", "Mission_EnumerateByCaptain", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    Vessel vessel = prereqs.Vessel;

                    Captain captain1 = new Captain("captain-alpha");
                    await db.Captains.CreateAsync(captain1);
                    Captain captain2 = new Captain("captain-beta");
                    await db.Captains.CreateAsync(captain2);

                    Mission m1 = new Mission("Captain 1 Mission A");
                    m1.VesselId = vessel.Id;
                    m1.CaptainId = captain1.Id;
                    Mission m2 = new Mission("Captain 1 Mission B");
                    m2.VesselId = vessel.Id;
                    m2.CaptainId = captain1.Id;
                    Mission m3 = new Mission("Captain 2 Mission");
                    m3.VesselId = vessel.Id;
                    m3.CaptainId = captain2.Id;
                    Mission m4 = new Mission("Unassigned Mission");
                    m4.VesselId = vessel.Id;

                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);
                    await db.Missions.CreateAsync(m3);
                    await db.Missions.CreateAsync(m4);

                    List<Mission> captain1Missions = await db.Missions.EnumerateByCaptainAsync(captain1.Id);
                    AssertEqual(2, captain1Missions.Count);

                    List<Mission> captain2Missions = await db.Missions.EnumerateByCaptainAsync(captain2.Id);
                    AssertEqual(1, captain2Missions.Count);
                    AssertEqual("Captain 2 Mission", captain2Missions[0].Title);
                }
            }));

            cases.Add(CaseAsync("mission_enumerate_by_status", "Mission_EnumerateByStatus", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    Vessel vessel = prereqs.Vessel;

                    Mission mPending = new Mission("Pending Mission");
                    mPending.VesselId = vessel.Id;
                    mPending.Status = MissionStatusEnum.Pending;

                    Mission mInProgress1 = new Mission("InProgress Mission 1");
                    mInProgress1.VesselId = vessel.Id;
                    mInProgress1.Status = MissionStatusEnum.InProgress;

                    Mission mInProgress2 = new Mission("InProgress Mission 2");
                    mInProgress2.VesselId = vessel.Id;
                    mInProgress2.Status = MissionStatusEnum.InProgress;

                    Mission mComplete = new Mission("Complete Mission");
                    mComplete.VesselId = vessel.Id;
                    mComplete.Status = MissionStatusEnum.Complete;

                    Mission mFailed = new Mission("Failed Mission");
                    mFailed.VesselId = vessel.Id;
                    mFailed.Status = MissionStatusEnum.Failed;

                    Mission mCancelled = new Mission("Cancelled Mission");
                    mCancelled.VesselId = vessel.Id;
                    mCancelled.Status = MissionStatusEnum.Cancelled;

                    await db.Missions.CreateAsync(mPending);
                    await db.Missions.CreateAsync(mInProgress1);
                    await db.Missions.CreateAsync(mInProgress2);
                    await db.Missions.CreateAsync(mComplete);
                    await db.Missions.CreateAsync(mFailed);
                    await db.Missions.CreateAsync(mCancelled);

                    List<Mission> pending = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Pending);
                    AssertEqual(1, pending.Count);
                    AssertEqual("Pending Mission", pending[0].Title);

                    List<Mission> inProgress = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.InProgress);
                    AssertEqual(2, inProgress.Count);

                    List<Mission> complete = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Complete);
                    AssertEqual(1, complete.Count);
                    AssertEqual("Complete Mission", complete[0].Title);

                    List<Mission> failed = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Failed);
                    AssertEqual(1, failed.Count);

                    List<Mission> cancelled = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Cancelled);
                    AssertEqual(1, cancelled.Count);

                    List<Mission> assigned = await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Assigned);
                    AssertEqual(0, assigned.Count);
                }
            }));

            cases.Add(CaseAsync("mission_delete", "Mission_Delete", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    Vessel vessel = prereqs.Vessel;
                    Voyage voyage = prereqs.Voyage;

                    Mission mission = new Mission("Delete Test Mission");
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    await db.Missions.CreateAsync(mission);

                    bool existsBefore = await db.Missions.ExistsAsync(mission.Id);
                    AssertTrue(existsBefore);

                    await db.Missions.DeleteAsync(mission.Id);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNull(result);

                    bool existsAfter = await db.Missions.ExistsAsync(mission.Id);
                    AssertFalse(existsAfter);
                }
            }));

            cases.Add(CaseAsync("mission_read_not_found", "Mission_ReadNotFound", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;

                    Mission? result = await db.Missions.ReadAsync("msn_nonexistent_id_12345");
                    AssertNull(result);
                }
            }));

            cases.Add(CaseAsync("mission_exists_not_found", "Mission_ExistsNotFound", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;

                    bool exists = await db.Missions.ExistsAsync("msn_nonexistent_id_12345");
                    AssertFalse(exists);
                }
            }));

            cases.Add(CaseAsync("mission_create_touches_parent_voyage_last_update_utc", "Mission_Create_TouchesParentVoyageLastUpdateUtc", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    Vessel vessel = prereqs.Vessel;
                    Voyage voyage = prereqs.Voyage;

                    Voyage? beforeCreate = await db.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                    AssertNotNull(beforeCreate);
                    DateTime originalLastUpdateUtc = beforeCreate!.LastUpdateUtc;

                    await Task.Delay(25).ConfigureAwait(false);

                    Mission mission = new Mission("Create Touch Test");
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    await db.Missions.CreateAsync(mission).ConfigureAwait(false);

                    Voyage? afterCreate = await db.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                    AssertNotNull(afterCreate);
                    AssertTrue(afterCreate!.LastUpdateUtc > originalLastUpdateUtc, "Voyage.LastUpdateUtc should advance when a mission is created");
                }
            }));

            cases.Add(CaseAsync("mission_update_touches_parent_voyage_last_update_utc", "Mission_Update_TouchesParentVoyageLastUpdateUtc", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    Vessel vessel = prereqs.Vessel;
                    Voyage voyage = prereqs.Voyage;

                    Mission mission = new Mission("Update Touch Test");
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    mission = await db.Missions.CreateAsync(mission).ConfigureAwait(false);

                    Voyage? beforeUpdate = await db.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                    AssertNotNull(beforeUpdate);
                    DateTime originalLastUpdateUtc = beforeUpdate!.LastUpdateUtc;

                    await Task.Delay(25).ConfigureAwait(false);

                    mission.Status = MissionStatusEnum.InProgress;
                    await db.Missions.UpdateAsync(mission).ConfigureAwait(false);

                    Voyage? afterUpdate = await db.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                    AssertNotNull(afterUpdate);
                    AssertTrue(afterUpdate!.LastUpdateUtc > originalLastUpdateUtc, "Voyage.LastUpdateUtc should advance when a mission is updated");
                }
            }));

            cases.Add(CaseAsync("mission_update_heartbeat_touches_mission_and_voyage_last_update_utc", "Mission_UpdateHeartbeat_TouchesMissionAndVoyageLastUpdateUtc", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Heartbeat mission")
                    {
                        VesselId = prereqs.Vessel.Id,
                        VoyageId = prereqs.Voyage.Id
                    };

                    await db.Missions.CreateAsync(mission).ConfigureAwait(false);

                    Mission? beforeMission = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    Voyage? beforeVoyage = await db.Voyages.ReadAsync(prereqs.Voyage.Id).ConfigureAwait(false);

                    AssertNotNull(beforeMission);
                    AssertNotNull(beforeVoyage);

                    await Task.Delay(20).ConfigureAwait(false);
                    await db.Missions.UpdateHeartbeatAsync(mission.Id).ConfigureAwait(false);

                    Mission? afterMission = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    Voyage? afterVoyage = await db.Voyages.ReadAsync(prereqs.Voyage.Id).ConfigureAwait(false);

                    AssertNotNull(afterMission);
                    AssertNotNull(afterVoyage);
                    AssertTrue(afterMission!.LastUpdateUtc > beforeMission!.LastUpdateUtc, "Mission.LastUpdateUtc should advance when a heartbeat is recorded");
                    AssertTrue(afterVoyage!.LastUpdateUtc > beforeVoyage!.LastUpdateUtc, "Voyage.LastUpdateUtc should advance when a mission heartbeat is recorded");
                }
            }));

            cases.Add(CaseAsync("mission_conditional_status_update", "TryUpdateIfStatusAsync writes a mission only while its stored status is expected", TestTags.Database, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Conditional");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.VoyageId = prereqs.Voyage.Id;
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = 2000000401;
                    await db.Missions.CreateAsync(mission);

                    Mission requeue = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    requeue.Status = MissionStatusEnum.Pending;
                    requeue.ProcessId = null;
                    requeue.RedispatchAttempts = 1;
                    AssertFalse(await db.Missions.TryUpdateIfStatusAsync(requeue, new MissionStatusEnum[] { MissionStatusEnum.Assigned }).ConfigureAwait(false), "unexpected status refused");
                    Mission? unchanged = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.InProgress, unchanged!.Status, "row unchanged");
                    AssertEqual(0, unchanged.RedispatchAttempts, "no field written on a refused update");

                    Mission produced = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    produced.Status = MissionStatusEnum.WorkProduced;
                    produced.ProcessId = null;
                    produced.BranchName = "armada/conditional";
                    AssertTrue(await db.Missions.TryUpdateIfStatusAsync(produced, new MissionStatusEnum[] { MissionStatusEnum.Assigned, MissionStatusEnum.InProgress }).ConfigureAwait(false), "expected status applied");
                    Mission? stored = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.WorkProduced, stored!.Status);
                    AssertEqual("armada/conditional", stored.BranchName, "every column is written");
                    AssertNull(stored.ProcessId);

                    AssertFalse(await db.Missions.TryUpdateIfStatusAsync(requeue, new MissionStatusEnum[] { MissionStatusEnum.InProgress }).ConfigureAwait(false), "a stale copy cannot move the mission back");
                    AssertEqual(MissionStatusEnum.WorkProduced, (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.Status);

                    Mission missing = new Mission("Missing");
                    AssertFalse(await db.Missions.TryUpdateIfStatusAsync(missing, new MissionStatusEnum[] { MissionStatusEnum.Pending }).ConfigureAwait(false), "missing mission");
                }
            }));

            cases.Add(CaseAsync("mission_process_exit_claim", "TryClaimProcessExitAsync gives exactly one caller the exit of the recorded process", TestTags.Database, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);

                    Mission mission = new Mission("Claim");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.VoyageId = prereqs.Voyage.Id;
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = 2000000402;
                    await db.Missions.CreateAsync(mission);

                    AssertFalse(await db.Missions.TryClaimProcessExitAsync(mission.Id, 2000000499).ConfigureAwait(false), "an exit of another process (an earlier attempt) is refused");
                    AssertEqual(2000000402, (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.ProcessId ?? 0, "refused claim leaves the process recorded");

                    AssertTrue(await db.Missions.TryClaimProcessExitAsync(mission.Id, 2000000402).ConfigureAwait(false), "first claim wins");
                    AssertFalse(await db.Missions.TryClaimProcessExitAsync(mission.Id, 2000000402).ConfigureAwait(false), "second claim for the same process loses");

                    Mission? stored = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertNull(stored!.ProcessId, "the claim clears the recorded process");
                    AssertEqual(MissionStatusEnum.InProgress, stored.Status, "the claim does not change status");
                    AssertFalse(await db.Missions.TryClaimProcessExitAsync("msn_missing", 2000000402).ConfigureAwait(false), "missing mission");
                }
            }));

            cases.Add(CaseAsync("mission_process_start_round_trip", "A mission stores a process id with its launch start time and clears that start time when the id changes", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MissionTestPrerequisites prereqs = await CreatePrerequisitesAsync(db);
                    DateTime startedUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

                    Mission mission = new Mission("Launch identity");
                    mission.VesselId = prereqs.Vessel.Id;
                    mission.VoyageId = prereqs.Voyage.Id;
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.SetProcess(4242, startedUtc);
                    await db.Missions.CreateAsync(mission);

                    Mission? created = await db.Missions.ReadAsync(mission.Id);
                    AssertEqual(4242, created!.ProcessId);
                    AssertTrue(SameUtcSecond(startedUtc, created.ProcessStartedUtc), "the launch start time persists on create");

                    MissionSummary? summary = await db.Missions.ReadSummaryAsync(mission.Id);
                    AssertEqual(4242, summary!.ProcessId);
                    AssertTrue(SameUtcSecond(startedUtc, summary.ProcessStartedUtc), "the summary keeps the launch start time");

                    created.SetProcess(5150, null);
                    await db.Missions.UpdateAsync(created);
                    Mission? cleared = await db.Missions.ReadAsync(mission.Id);
                    AssertEqual(5150, cleared!.ProcessId);
                    AssertNull(cleared.ProcessStartedUtc, "a new process id without a start time clears the stored start time");

                    cleared.SetProcess(5150, startedUtc);
                    await db.Missions.UpdateAsync(cleared);
                    AssertTrue(await db.Missions.TryClaimProcessExitAsync(mission.Id, 5150), "the recorded process exit can be claimed");
                    Mission? claimed = await db.Missions.ReadAsync(mission.Id);
                    AssertNull(claimed!.ProcessId, "the claim clears the process id");
                    AssertNull(claimed.ProcessStartedUtc, "the claim clears the launch start time");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Mission Database",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static bool SameUtcSecond(DateTime expectedUtc, DateTime? actual)
        {
            if (!actual.HasValue) return false;
            DateTime observed = actual.Value;
            if (observed.Kind == DateTimeKind.Local) observed = observed.ToUniversalTime();
            else observed = DateTime.SpecifyKind(observed, DateTimeKind.Utc);
            return expectedUtc.Year == observed.Year
                && expectedUtc.Month == observed.Month
                && expectedUtc.Day == observed.Day
                && expectedUtc.Hour == observed.Hour
                && expectedUtc.Minute == observed.Minute
                && expectedUtc.Second == observed.Second;
        }

        private static async Task<MissionTestPrerequisites> CreatePrerequisitesAsync(DatabaseDriver db)
        {
            Fleet fleet = new Fleet("Test Fleet");
            fleet.CreatedUtc = _BaseTime;
            await db.Fleets.CreateAsync(fleet).ConfigureAwait(false);

            Vessel vessel = new Vessel("Test Vessel", "https://github.com/test/repo");
            vessel.FleetId = fleet.Id;
            vessel.CreatedUtc = _BaseTime;
            await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            Voyage voyage = new Voyage("Test Voyage", "Test voyage description");
            voyage.CreatedUtc = _BaseTime;
            await db.Voyages.CreateAsync(voyage).ConfigureAwait(false);

            return new MissionTestPrerequisites(fleet, vessel, voyage);
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
