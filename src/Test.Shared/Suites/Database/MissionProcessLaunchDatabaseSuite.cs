namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the mission-owned process start time (migration 131): SetProcessLaunchAsync stores the exact start
    /// time with its process id at full precision on every provider, a full mission update leaves it unchanged, and a
    /// non-local launch clears it.
    /// </summary>
    public sealed class MissionProcessLaunchDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.MissionProcessLaunch";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the mission process launch suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("launch_start_round_trips_and_survives_updates", "The recorded start time round-trips exactly and survives a full mission update", TestTags.Positive, async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    Mission mission = await db.Missions.CreateAsync(new Mission("process launch"), ct).ConfigureAwait(false);
                    Mission fresh = (await db.Missions.ReadAsync(mission.Id, ct).ConfigureAwait(false))!;
                    AssertNull(fresh.ProcessStartedPid, "no pid before a launch");
                    AssertNull(fresh.ProcessStartedUtc, "no start before a launch");

                    DateTime started = new DateTime(2026, 11, 19, 8, 30, 15, DateTimeKind.Utc).AddTicks(1234567);
                    AssertTrue(await db.Missions.SetProcessLaunchAsync(mission.Id, 4321, started, ct).ConfigureAwait(false), "recorded");
                    Mission launched = (await db.Missions.ReadAsync(mission.Id, ct).ConfigureAwait(false))!;
                    AssertEqual(4321, launched.ProcessStartedPid, "pid");
                    AssertNotNull(launched.ProcessStartedUtc, "start");
                    AssertEqual(started.Ticks, launched.ProcessStartedUtc!.Value.Ticks, "start at full precision");
                    AssertEqual(DateTimeKind.Utc, launched.ProcessStartedUtc.Value.Kind, "UTC");

                    // Every other mission write leaves the pair alone, including a write of a stale object.
                    launched.ProcessId = 4321;
                    launched.Status = MissionStatusEnum.InProgress;
                    launched.ProcessStartedPid = null;
                    launched.ProcessStartedUtc = null;
                    await db.Missions.UpdateAsync(launched, ct).ConfigureAwait(false);
                    Mission updated = (await db.Missions.ReadAsync(mission.Id, ct).ConfigureAwait(false))!;
                    AssertEqual(4321, updated.ProcessStartedPid, "pid kept by a full update");
                    AssertEqual(started.Ticks, updated.ProcessStartedUtc!.Value.Ticks, "start kept by a full update");

                    AssertTrue(await db.Missions.SetProcessLaunchAsync(mission.Id, null, null, ct).ConfigureAwait(false), "cleared");
                    Mission cleared = (await db.Missions.ReadAsync(mission.Id, ct).ConfigureAwait(false))!;
                    AssertNull(cleared.ProcessStartedPid, "pid cleared");
                    AssertNull(cleared.ProcessStartedUtc, "start cleared");

                    AssertTrue(await db.Missions.SetProcessLaunchAsync(mission.Id, 77, null, ct).ConfigureAwait(false), "half a record");
                    Mission half = (await db.Missions.ReadAsync(mission.Id, ct).ConfigureAwait(false))!;
                    AssertNull(half.ProcessStartedPid, "a pid without a start time is not stored");
                    AssertNull(half.ProcessStartedUtc, "no start time stored");
                }
            }));

            cases.Add(Case("unknown_mission_and_guards", "Recording for an unknown mission changes nothing; an empty id is rejected", TestTags.Negative, async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    AssertFalse(await db.Missions.SetProcessLaunchAsync("msn_does_not_exist", 1, DateTime.UtcNow, ct).ConfigureAwait(false), "no row");
                    await AssertThrowsAsync<ArgumentNullException>(() => db.Missions.SetProcessLaunchAsync("", 1, DateTime.UtcNow, ct), "empty id").ConfigureAwait(false);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Mission Process Launch",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: body,
                tags: new List<string> { tag, TestTags.Database });
        }

        #endregion
    }
}
