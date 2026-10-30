namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Database.Postgresql.Implementations;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="CascadeCleanup"/>. The events and planning_sessions tables carry plain
    /// (non-foreign-key) references to their parent entities, so a hard delete would otherwise leave rows
    /// that dangle and later fail to resolve. These cases prove that removing the dependents of a vessel,
    /// mission, voyage, or captain deletes exactly the rows that referenced that parent and leaves rows
    /// belonging to other parents untouched.
    /// </summary>
    public sealed class CascadeCleanupSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.CascadeCleanup";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Cascade Cleanup suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("removes_events_for_vessel_only", "RemoveEventsForVesselAsync deletes only the target vessel's events", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    await CreateEventAsync(db, "vessel.updated", vesselId: "vsl_target").ConfigureAwait(false);
                    await CreateEventAsync(db, "vessel.updated", vesselId: "vsl_target").ConfigureAwait(false);
                    await CreateEventAsync(db, "vessel.updated", vesselId: "vsl_other").ConfigureAwait(false);

                    int removed = await CascadeCleanup.RemoveEventsForVesselAsync(db, "vsl_target").ConfigureAwait(false);

                    AssertEqual(2, removed);
                    AssertEqual(0, (await db.Events.EnumerateByVesselAsync("vsl_target", 500).ConfigureAwait(false)).Count);
                    AssertEqual(1, (await db.Events.EnumerateByVesselAsync("vsl_other", 500).ConfigureAwait(false)).Count);
                }
            }));

            cases.Add(CaseAsync("removes_events_for_mission_only", "RemoveEventsForMissionAsync deletes only the target mission's events", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    await CreateEventAsync(db, "mission.progress", missionId: "msn_target").ConfigureAwait(false);
                    await CreateEventAsync(db, "mission.progress", missionId: "msn_other").ConfigureAwait(false);

                    int removed = await CascadeCleanup.RemoveEventsForMissionAsync(db, "msn_target").ConfigureAwait(false);

                    AssertEqual(1, removed);
                    AssertEqual(0, (await db.Events.EnumerateByMissionAsync("msn_target", 500).ConfigureAwait(false)).Count);
                    AssertEqual(1, (await db.Events.EnumerateByMissionAsync("msn_other", 500).ConfigureAwait(false)).Count);
                }
            }));

            cases.Add(CaseAsync("removes_events_for_voyage_only", "RemoveEventsForVoyageAsync deletes only the target voyage's events", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    await CreateEventAsync(db, "voyage.updated", voyageId: "vyg_target").ConfigureAwait(false);
                    await CreateEventAsync(db, "voyage.updated", voyageId: "vyg_other").ConfigureAwait(false);

                    int removed = await CascadeCleanup.RemoveEventsForVoyageAsync(db, "vyg_target").ConfigureAwait(false);

                    AssertEqual(1, removed);
                    AssertEqual(0, (await db.Events.EnumerateByVoyageAsync("vyg_target", 500).ConfigureAwait(false)).Count);
                    AssertEqual(1, (await db.Events.EnumerateByVoyageAsync("vyg_other", 500).ConfigureAwait(false)).Count);
                }
            }));

            cases.Add(CaseAsync("removes_captain_events_and_planning_sessions", "RemoveDependentsForCaptainAsync deletes the captain's events and planning sessions", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    await CreateEventAsync(db, "captain.assigned", captainId: "cpt_target").ConfigureAwait(false);
                    await CreateEventAsync(db, "captain.assigned", captainId: "cpt_other").ConfigureAwait(false);
                    await CreatePlanningSessionAsync(db, "cpt_target").ConfigureAwait(false);
                    await CreatePlanningSessionAsync(db, "cpt_target").ConfigureAwait(false);
                    await CreatePlanningSessionAsync(db, "cpt_other").ConfigureAwait(false);

                    int removed = await CascadeCleanup.RemoveDependentsForCaptainAsync(db, "cpt_target").ConfigureAwait(false);

                    // 1 event + 2 planning sessions.
                    AssertEqual(3, removed);
                    AssertEqual(0, (await db.Events.EnumerateByCaptainAsync("cpt_target", 500).ConfigureAwait(false)).Count);
                    AssertEqual(0, (await db.PlanningSessions.EnumerateByCaptainAsync("cpt_target").ConfigureAwait(false)).Count);
                    AssertEqual(1, (await db.Events.EnumerateByCaptainAsync("cpt_other", 500).ConfigureAwait(false)).Count);
                    AssertEqual(1, (await db.PlanningSessions.EnumerateByCaptainAsync("cpt_other").ConfigureAwait(false)).Count);
                }
            }));

            cases.Add(CaseAsync("removes_captain_events_when_planning_sessions_are_unsupported", "RemoveDependentsForCaptainAsync continues when the provider does not support planning sessions", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = new UnsupportedPlanningSessionDatabaseDriver(testDb.Driver);
                    await CreateEventAsync(db, "captain.assigned", captainId: "cpt_target").ConfigureAwait(false);

                    int removed = await CascadeCleanup.RemoveDependentsForCaptainAsync(db, "cpt_target").ConfigureAwait(false);

                    AssertEqual(1, removed);
                    AssertEqual(0, (await testDb.Driver.Events.EnumerateByCaptainAsync("cpt_target", 500).ConfigureAwait(false)).Count);
                }
            }));

            cases.Add(CaseAsync("empty_parent_id_is_a_safe_no_op", "Cleanup with an empty parent id removes nothing", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    await CreateEventAsync(db, "vessel.updated", vesselId: "vsl_keep").ConfigureAwait(false);

                    int removed = await CascadeCleanup.RemoveEventsForVesselAsync(db, String.Empty).ConfigureAwait(false);

                    AssertEqual(0, removed);
                    AssertEqual(1, (await db.Events.EnumerateByVesselAsync("vsl_keep", 500).ConfigureAwait(false)).Count);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Cascade Cleanup",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task CreateEventAsync(
            DatabaseDriver db,
            string eventType,
            string? captainId = null,
            string? missionId = null,
            string? vesselId = null,
            string? voyageId = null)
        {
            ArmadaEvent armadaEvent = new ArmadaEvent
            {
                EventType = eventType,
                Message = eventType,
                CaptainId = captainId,
                MissionId = missionId,
                VesselId = vesselId,
                VoyageId = voyageId
            };

            await db.Events.CreateAsync(armadaEvent).ConfigureAwait(false);
        }

        private static async Task CreatePlanningSessionAsync(DatabaseDriver db, string captainId)
        {
            PlanningSession session = new PlanningSession
            {
                CaptainId = captainId,
                VesselId = "vsl_planning",
                Title = "Planning for " + captainId
            };

            await db.PlanningSessions.CreateAsync(session).ConfigureAwait(false);
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

        private sealed class UnsupportedPlanningSessionDatabaseDriver : DatabaseDriver
        {
            private readonly DatabaseDriver _Inner;

            public UnsupportedPlanningSessionDatabaseDriver(DatabaseDriver inner)
            {
                _Inner = inner;
                foreach (PropertyInfo property in typeof(DatabaseDriver).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    MethodInfo? setter = property.GetSetMethod(true);
                    if (setter == null || property.GetIndexParameters().Length > 0) continue;
                    setter.Invoke(this, new object?[] { property.GetValue(inner) });
                }

                PlanningSessions = new PlanningSessionMethods(null!, null!, null!);
            }

            public override Task InitializeAsync(CancellationToken token = default) => _Inner.InitializeAsync(token);

            public override Task<int> GetSchemaVersionAsync(CancellationToken token = default) => _Inner.GetSchemaVersionAsync(token);

            public override int GetLatestSchemaVersion() => _Inner.GetLatestSchemaVersion();

            public override void Dispose()
            {
            }

            internal override IReadOnlyList<SchemaMigration> GetMigrationsForVerification() => _Inner.GetMigrationsForVerification();

            internal override Task ReplayMigrationsAsync(CancellationToken token = default) => _Inner.ReplayMigrationsAsync(token);
        }

        #endregion
    }
}
