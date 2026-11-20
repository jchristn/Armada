namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Postgresql;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Npgsql;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// PostgreSQL stores UTC clocks in <c>timestamp without time zone</c>. These cases open a
    /// non-UTC session and check that writes, reads, and range filters keep that clock.
    /// The SQLite case checks the same window on the default driver.
    /// </summary>
    public sealed class PostgresqlUtcClockSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.PostgresqlUtcClock";
        private const string NonUtcZone = "America/New_York";

        private static readonly DateTime _InsideUtc = new DateTime(2024, 6, 15, 16, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime _OutsideUtc = new DateTime(2024, 6, 14, 16, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime _FromUtc = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime _ToUtc = new DateTime(2024, 6, 16, 0, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for PostgreSQL UTC-clock binding.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            bool postgres = TestDatabaseConfig.Type == DatabaseTypeEnum.Postgresql;
            bool sqlite = TestDatabaseConfig.Type == DatabaseTypeEnum.Sqlite;
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case(
                "postgresql_non_utc_session_keeps_mission_vessel_and_workflow_profile_clocks",
                "A non-UTC PostgreSQL session stores and reads mission, vessel, and workflow-profile UTC clocks",
                postgres,
                "Requires PostgreSQL. The non-UTC session shows timestamp-without-time-zone conversion. SQLite, MySQL, and SQL Server do not prove it.",
                async () =>
                {
                    using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                    {
                        await UseNonUtcSessionAsync(testDb, async (DatabaseDriver db) =>
                        {
                            string tenantId = await CreateTenantAsync(db).ConfigureAwait(false);
                            Vessel vessel = new Vessel("Clock vessel", "https://example.test/clock.git");
                            vessel.TenantId = tenantId;
                            vessel.CreatedUtc = _InsideUtc;
                            await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                            await AssertClockAsync(db.Vessels.ReadAsync(vessel.Id), vessel.Id, _InsideUtc).ConfigureAwait(false);

                            Mission mission = new Mission("Clock mission", "clock");
                            mission.TenantId = tenantId;
                            mission.VesselId = vessel.Id;
                            mission.CreatedUtc = _InsideUtc;
                            await db.Missions.CreateAsync(mission).ConfigureAwait(false);
                            await AssertClockAsync(db.Missions.ReadAsync(mission.Id), mission.Id, _InsideUtc).ConfigureAwait(false);

                            WorkflowProfile profile = new WorkflowProfile { Name = "Clock profile", TenantId = tenantId, CreatedUtc = _InsideUtc };
                            await db.WorkflowProfiles.CreateAsync(profile).ConfigureAwait(false);
                            await AssertClockAsync(db.WorkflowProfiles.ReadAsync(profile.Id), profile.Id, _InsideUtc).ConfigureAwait(false);

                            WorkflowProfileQuery window = new WorkflowProfileQuery
                            {
                                TenantId = tenantId,
                                FromUtc = _FromUtc,
                                ToUtc = _ToUtc,
                                PageSize = 50
                            };
                            EnumerationResult<WorkflowProfile> listed = await db.WorkflowProfiles.EnumerateAsync(window).ConfigureAwait(false);
                            AssertWindow(listed, profile.Id, String.Empty, item => item.Id);
                        }).ConfigureAwait(false);
                    }
                }));

            cases.Add(Case(
                "postgresql_non_utc_session_reads_objective_and_model_endpoint_clocks_as_utc",
                "A non-UTC PostgreSQL session returns objective and model-endpoint UTC clocks with DateTimeKind.Utc",
                postgres,
                "Requires PostgreSQL. The non-UTC session shows timestamp-without-time-zone conversion. SQLite, MySQL, and SQL Server do not prove it.",
                async () =>
                {
                    using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                    {
                        await UseNonUtcSessionAsync(testDb, async (DatabaseDriver db) =>
                        {
                            string tenantId = await CreateTenantAsync(db).ConfigureAwait(false);
                            Objective objective = new Objective
                            {
                                TenantId = tenantId,
                                Title = "Clock objective",
                                CreatedUtc = _InsideUtc,
                                LastUpdateUtc = _InsideUtc
                            };
                            await db.Objectives.CreateAsync(objective).ConfigureAwait(false);
                            Objective? readObjective = await db.Objectives.ReadAsync(objective.Id).ConfigureAwait(false);
                            AssertNotNull(readObjective, "objective is readable");
                            AssertUtcClock(readObjective!.CreatedUtc, _InsideUtc, "objective CreatedUtc");
                            AssertUtcClock(readObjective.LastUpdateUtc, _InsideUtc, "objective LastUpdateUtc");

                            ModelEndpoint endpoint = new ModelEndpoint
                            {
                                TenantId = tenantId,
                                Name = "Clock endpoint",
                                BaseUrl = "https://example.test",
                                CreatedUtc = _InsideUtc,
                                LastUpdateUtc = _InsideUtc
                            };
                            await db.ModelEndpoints.CreateAsync(endpoint).ConfigureAwait(false);
                            ModelEndpoint? readEndpoint = await db.ModelEndpoints.ReadAsync(endpoint.Id).ConfigureAwait(false);
                            AssertNotNull(readEndpoint, "model endpoint is readable");
                            AssertUtcClock(readEndpoint!.CreatedUtc, _InsideUtc, "model endpoint CreatedUtc");
                            AssertUtcClock(readEndpoint.LastUpdateUtc, _InsideUtc, "model endpoint LastUpdateUtc");
                        }).ConfigureAwait(false);
                    }
                }));

            cases.Add(Case(
                "postgresql_skill_and_project_profile_bounds_match_across_session_zones",
                "Skill and project-profile UTC bounds return the same ids and totals on UTC and non-UTC sessions",
                postgres,
                "Requires PostgreSQL. The non-UTC session shows timestamp-without-time-zone conversion. SQLite, MySQL, and SQL Server do not prove it.",
                async () =>
                {
                    using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                    {
                        await UseNonUtcSessionAsync(testDb, async (DatabaseDriver db) =>
                        {
                            string tenantId = await CreateTenantAsync(db).ConfigureAwait(false);
                            Skill insideSkill = new Skill { Name = "Inside skill", TenantId = tenantId, CreatedUtc = _InsideUtc };
                            Skill outsideSkill = new Skill { Name = "Outside skill", TenantId = tenantId, CreatedUtc = _OutsideUtc };
                            await db.Skills.CreateAsync(insideSkill).ConfigureAwait(false);
                            await db.Skills.CreateAsync(outsideSkill).ConfigureAwait(false);
                            Skill? readSkill = await db.Skills.ReadAsync(insideSkill.Id).ConfigureAwait(false);
                            AssertNotNull(readSkill, "skill is readable");
                            AssertUtcClock(readSkill!.CreatedUtc, _InsideUtc, "skill CreatedUtc");

                            ProjectProfile insideProfile = new ProjectProfile { Name = "Inside profile", TenantId = tenantId, CreatedUtc = _InsideUtc };
                            ProjectProfile outsideProfile = new ProjectProfile { Name = "Outside profile", TenantId = tenantId, CreatedUtc = _OutsideUtc };
                            await db.ProjectProfiles.CreateAsync(insideProfile).ConfigureAwait(false);
                            await db.ProjectProfiles.CreateAsync(outsideProfile).ConfigureAwait(false);
                            ProjectProfile? readProfile = await db.ProjectProfiles.ReadAsync(insideProfile.Id).ConfigureAwait(false);
                            AssertNotNull(readProfile, "project profile is readable");
                            AssertUtcClock(readProfile!.CreatedUtc, _InsideUtc, "project profile CreatedUtc");

                            SkillQuery skillQuery = new SkillQuery { TenantId = tenantId, FromUtc = _FromUtc, ToUtc = _ToUtc, PageSize = 50 };
                            EnumerationResult<Skill> skillEast = await db.Skills.EnumerateAsync(skillQuery).ConfigureAwait(false);
                            LoggingModule logging = new LoggingModule();
                            logging.Settings.EnableConsole = false;
                            EnumerationResult<Skill> skillUtc;
                            EnumerationResult<ProjectProfile> profileUtc;
                            using (PostgresqlDatabaseDriver utcDriver = new PostgresqlDatabaseDriver(testDb.ConnectionString + ";Timezone=UTC", logging))
                            {
                                skillUtc = await utcDriver.Skills.EnumerateAsync(skillQuery).ConfigureAwait(false);
                                ProjectProfileQuery profileQuery = new ProjectProfileQuery { TenantId = tenantId, FromUtc = _FromUtc, ToUtc = _ToUtc, PageSize = 50 };
                                EnumerationResult<ProjectProfile> profileEast = await db.ProjectProfiles.EnumerateAsync(profileQuery).ConfigureAwait(false);
                                profileUtc = await utcDriver.ProjectProfiles.EnumerateAsync(profileQuery).ConfigureAwait(false);
                                AssertWindow(profileEast, insideProfile.Id, outsideProfile.Id, item => item.Id);
                                AssertEqual(JoinedIds(profileEast.Objects, item => item.Id), JoinedIds(profileUtc.Objects, item => item.Id), "project-profile ids match across session zones");
                                AssertEqual(profileEast.TotalRecords, profileUtc.TotalRecords, "project-profile totals match across session zones");
                            }
                            AssertWindow(skillEast, insideSkill.Id, outsideSkill.Id, item => item.Id);
                            AssertEqual(JoinedIds(skillEast.Objects, item => item.Id), JoinedIds(skillUtc.Objects, item => item.Id), "skill ids match across session zones");
                            AssertEqual(skillEast.TotalRecords, skillUtc.TotalRecords, "skill totals match across session zones");
                        }).ConfigureAwait(false);
                    }
                }));

            cases.Add(Case(
                "postgresql_timestamp_range_filters_return_the_inside_row",
                "PostgreSQL timestamp range filters return the inside row and keep the mission-history upper bound exclusive",
                postgres,
                "Requires PostgreSQL. The non-UTC session shows timestamp-without-time-zone conversion. SQLite, MySQL, and SQL Server do not prove it.",
                async () =>
                {
                    using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                    {
                        await UseNonUtcSessionAsync(testDb, async (DatabaseDriver db) =>
                        {
                            string tenantId = await CreateTenantAsync(db).ConfigureAwait(false);
                            await AssertCreatedRangeAsync(db, tenantId).ConfigureAwait(false);
                        }).ConfigureAwait(false);
                    }
                }));

            cases.Add(Case(
                "sqlite_timestamp_range_returns_the_inside_row",
                "SQLite returns the token-usage row inside the same UTC window",
                sqlite,
                "SQLite control for the shared UTC window. The PostgreSQL cases prove timestamp-without-time-zone binding.",
                async () =>
                {
                    using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                    {
                        DatabaseDriver db = testDb.Driver;
                        string tenantId = await CreateTenantAsync(db).ConfigureAwait(false);
                        TokenUsageRecord inside = Token("tku_inside", tenantId, _InsideUtc);
                        TokenUsageRecord outside = Token("tku_outside", tenantId, _OutsideUtc);
                        await db.TokenUsage.CreateAsync(inside).ConfigureAwait(false);
                        await db.TokenUsage.CreateAsync(outside).ConfigureAwait(false);
                        EnumerationResult<TokenUsageRecord> listed = await db.TokenUsage.EnumerateAsync(TokenQuery(tenantId)).ConfigureAwait(false);
                        AssertWindow(listed, inside.Id, outside.Id, item => item.Id);
                    }
                }));

            return new TestSuiteDescriptor(SuiteId, "PostgreSQL UTC clock", cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, bool run, string skipReason, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive },
                skip: !run,
                skipReason: run ? null : skipReason);
        }

        private static async Task UseNonUtcSessionAsync(TestDatabase testDb, Func<DatabaseDriver, Task> body)
        {
            string connectionString = testDb.ConnectionString;
            string databaseName = await ScalarAsync(connectionString, "SELECT current_database()").ConfigureAwait(false);
            if (String.IsNullOrEmpty(databaseName) || databaseName.IndexOf('"') >= 0 || databaseName.IndexOf(';') >= 0)
                throw new InvalidOperationException("The test database name cannot be quoted for ALTER DATABASE.");

            string quoted = "\"" + databaseName + "\"";
            await ExecuteAsync(connectionString, "ALTER DATABASE " + quoted + " SET timezone TO '" + NonUtcZone + "'").ConfigureAwait(false);
            NpgsqlConnection.ClearAllPools();
            try
            {
                string zone = await ScalarAsync(connectionString, "SHOW timezone").ConfigureAwait(false);
                AssertEqual(NonUtcZone, zone, "the PostgreSQL session time zone is America/New_York");

                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                using (PostgresqlDatabaseDriver utcDriver = new PostgresqlDatabaseDriver(connectionString + ";Timezone=UTC", logging))
                {
                    string utcZone = await ScalarAsync(connectionString + ";Timezone=UTC", "SHOW timezone").ConfigureAwait(false);
                    AssertEqual("UTC", utcZone, "the comparison session time zone is UTC");
                    await body(testDb.Driver).ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    await ExecuteAsync(connectionString, "ALTER DATABASE " + quoted + " RESET timezone").ConfigureAwait(false);
                }
                finally
                {
                    NpgsqlConnection.ClearAllPools();
                }
            }
        }

        private static async Task<string> CreateTenantAsync(DatabaseDriver db)
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            TenantMetadata tenant = new TenantMetadata("UTC clock " + suffix);
            await db.Tenants.CreateAsync(tenant).ConfigureAwait(false);
            return tenant.Id;
        }

        private static async Task AssertCreatedRangeAsync(DatabaseDriver db, string tenantId)
        {
            CheckRun insideCheck = new CheckRun { TenantId = tenantId, CreatedUtc = _InsideUtc };
            CheckRun outsideCheck = new CheckRun { TenantId = tenantId, CreatedUtc = _OutsideUtc };
            await db.CheckRuns.CreateAsync(insideCheck).ConfigureAwait(false);
            await db.CheckRuns.CreateAsync(outsideCheck).ConfigureAwait(false);
            AssertWindow(await db.CheckRuns.EnumerateAsync(new CheckRunQuery { TenantId = tenantId, FromUtc = _FromUtc, ToUtc = _ToUtc, PageSize = 50 }).ConfigureAwait(false), insideCheck.Id, outsideCheck.Id, item => item.Id);

            Deployment insideDeployment = new Deployment { TenantId = tenantId, CreatedUtc = _InsideUtc };
            Deployment outsideDeployment = new Deployment { TenantId = tenantId, CreatedUtc = _OutsideUtc };
            await db.Deployments.CreateAsync(insideDeployment).ConfigureAwait(false);
            await db.Deployments.CreateAsync(outsideDeployment).ConfigureAwait(false);
            AssertWindow(await db.Deployments.EnumerateAsync(new DeploymentQuery { TenantId = tenantId, FromUtc = _FromUtc, ToUtc = _ToUtc, PageSize = 50 }).ConfigureAwait(false), insideDeployment.Id, outsideDeployment.Id, item => item.Id);

            Release insideRelease = new Release { TenantId = tenantId, CreatedUtc = _InsideUtc };
            Release outsideRelease = new Release { TenantId = tenantId, CreatedUtc = _OutsideUtc };
            await db.Releases.CreateAsync(insideRelease).ConfigureAwait(false);
            await db.Releases.CreateAsync(outsideRelease).ConfigureAwait(false);
            AssertWindow(await db.Releases.EnumerateAsync(new ReleaseQuery { TenantId = tenantId, FromUtc = _FromUtc, ToUtc = _ToUtc, PageSize = 50 }).ConfigureAwait(false), insideRelease.Id, outsideRelease.Id, item => item.Id);

            RequestHistoryEntry insideRequest = new RequestHistoryEntry { TenantId = tenantId, CreatedUtc = _InsideUtc };
            RequestHistoryEntry outsideRequest = new RequestHistoryEntry { TenantId = tenantId, CreatedUtc = _OutsideUtc };
            await db.RequestHistory.CreateAsync(insideRequest, null).ConfigureAwait(false);
            await db.RequestHistory.CreateAsync(outsideRequest, null).ConfigureAwait(false);
            AssertWindow(await db.RequestHistory.EnumerateAsync(new RequestHistoryQuery { TenantId = tenantId, FromUtc = _FromUtc, ToUtc = _ToUtc, PageSize = 50 }).ConfigureAwait(false), insideRequest.Id, outsideRequest.Id, item => item.Id);

            WorkflowProfile insideWorkflow = new WorkflowProfile { Name = "Inside workflow", TenantId = tenantId, CreatedUtc = _InsideUtc };
            WorkflowProfile outsideWorkflow = new WorkflowProfile { Name = "Outside workflow", TenantId = tenantId, CreatedUtc = _OutsideUtc };
            await db.WorkflowProfiles.CreateAsync(insideWorkflow).ConfigureAwait(false);
            await db.WorkflowProfiles.CreateAsync(outsideWorkflow).ConfigureAwait(false);
            AssertWindow(await db.WorkflowProfiles.EnumerateAsync(new WorkflowProfileQuery { TenantId = tenantId, FromUtc = _FromUtc, ToUtc = _ToUtc, PageSize = 50 }).ConfigureAwait(false), insideWorkflow.Id, outsideWorkflow.Id, item => item.Id);

            ProjectProfile insideProject = new ProjectProfile { Name = "Inside project", TenantId = tenantId, CreatedUtc = _InsideUtc };
            ProjectProfile outsideProject = new ProjectProfile { Name = "Outside project", TenantId = tenantId, CreatedUtc = _OutsideUtc };
            await db.ProjectProfiles.CreateAsync(insideProject).ConfigureAwait(false);
            await db.ProjectProfiles.CreateAsync(outsideProject).ConfigureAwait(false);
            AssertWindow(await db.ProjectProfiles.EnumerateAsync(new ProjectProfileQuery { TenantId = tenantId, FromUtc = _FromUtc, ToUtc = _ToUtc, PageSize = 50 }).ConfigureAwait(false), insideProject.Id, outsideProject.Id, item => item.Id);

            Skill insideSkill = new Skill { Name = "Inside range skill", TenantId = tenantId, CreatedUtc = _InsideUtc };
            Skill outsideSkill = new Skill { Name = "Outside range skill", TenantId = tenantId, CreatedUtc = _OutsideUtc };
            await db.Skills.CreateAsync(insideSkill).ConfigureAwait(false);
            await db.Skills.CreateAsync(outsideSkill).ConfigureAwait(false);
            AssertWindow(await db.Skills.EnumerateAsync(new SkillQuery { TenantId = tenantId, FromUtc = _FromUtc, ToUtc = _ToUtc, PageSize = 50 }).ConfigureAwait(false), insideSkill.Id, outsideSkill.Id, item => item.Id);

            TokenUsageRecord insideUsage = Token("tku_pg_inside", tenantId, _InsideUtc);
            TokenUsageRecord outsideUsage = Token("tku_pg_outside", tenantId, _OutsideUtc);
            await db.TokenUsage.CreateAsync(insideUsage).ConfigureAwait(false);
            await db.TokenUsage.CreateAsync(outsideUsage).ConfigureAwait(false);
            AssertWindow(await db.TokenUsage.EnumerateAsync(TokenQuery(tenantId)).ConfigureAwait(false), insideUsage.Id, outsideUsage.Id, item => item.Id);

            Vessel vessel = new Vessel("Range vessel", "https://example.test/range.git");
            vessel.TenantId = tenantId;
            await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            Mission insideMission = new Mission("Inside mission", "range") { TenantId = tenantId, VesselId = vessel.Id, CreatedUtc = _InsideUtc };
            Mission outsideMission = new Mission("Outside mission", "range") { TenantId = tenantId, VesselId = vessel.Id, CreatedUtc = _OutsideUtc };
            Mission upperMission = new Mission("Upper mission", "range") { TenantId = tenantId, VesselId = vessel.Id, CreatedUtc = _ToUtc };
            await db.Missions.CreateAsync(insideMission).ConfigureAwait(false);
            await db.Missions.CreateAsync(outsideMission).ConfigureAwait(false);
            await db.Missions.CreateAsync(upperMission).ConfigureAwait(false);

            EnumerationResult<MissionSummary> summaries = await db.Missions.EnumerateSummariesAsync(new EnumerationQuery
            {
                VesselId = vessel.Id,
                CreatedAfter = _FromUtc,
                CreatedBefore = _ToUtc,
                PageSize = 50
            }).ConfigureAwait(false);
            AssertWindow(summaries, insideMission.Id, outsideMission.Id, item => item.Id);
            AssertTrue(!IdList(summaries.Objects, item => item.Id).Contains(upperMission.Id), "the exclusive CreatedBefore bound omits the upper instant");

            List<MissionHistoryPoint> history = await db.Missions.EnumerateHistoryPointsAsync(new MissionHistoryQuery
            {
                VesselId = vessel.Id,
                FromUtc = _FromUtc,
                ToUtc = _ToUtc
            }).ConfigureAwait(false);
            int insidePoints = 0;
            int upperPoints = 0;
            foreach (MissionHistoryPoint point in history)
            {
                if (point.CreatedUtc.Ticks == _InsideUtc.Ticks) insidePoints++;
                if (point.CreatedUtc.Ticks == _ToUtc.Ticks) upperPoints++;
            }
            AssertEqual(1, insidePoints, "mission history includes the inside UTC instant");
            AssertEqual(0, upperPoints, "mission history keeps created_utc < ToUtc");
        }

        private static TokenUsageRecord Token(string id, string tenantId, DateTime createdUtc)
        {
            return new TokenUsageRecord
            {
                Id = id,
                TenantId = tenantId,
                Model = "example-model",
                Source = "mission",
                CreatedUtc = createdUtc
            };
        }

        private static TokenUsageQuery TokenQuery(string tenantId)
        {
            return new TokenUsageQuery
            {
                TenantId = tenantId,
                FromUtc = _FromUtc,
                ToUtc = _ToUtc,
                PageSize = 50
            };
        }

        private static async Task AssertClockAsync<T>(Task<T?> readTask, string id, DateTime expected) where T : class
        {
            T? read = await readTask.ConfigureAwait(false);
            AssertNotNull(read, id + " is readable");
            DateTime created = CreatedUtcOf(read!);
            AssertUtcClock(created, expected, id);
        }

        private static DateTime CreatedUtcOf(object entity)
        {
            if (entity is Vessel vessel) return vessel.CreatedUtc;
            if (entity is Mission mission) return mission.CreatedUtc;
            if (entity is WorkflowProfile profile) return profile.CreatedUtc;
            throw new InvalidOperationException("This clock check has no CreatedUtc reader.");
        }

        private static void AssertUtcClock(DateTime actual, DateTime expected, string label)
        {
            AssertEqual(DateTimeKind.Utc, actual.Kind, label + " kind is UTC");
            AssertEqual(expected.Ticks, actual.Ticks, label + " clock ticks match the UTC value");
        }

        private static void AssertWindow<T>(EnumerationResult<T> result, string insideId, string outsideId, Func<T, string> id)
        {
            List<string> ids = IdList(result.Objects, id);
            AssertTrue(ids.Contains(insideId), "the row inside the UTC window is returned");
            if (outsideId.Length > 0) AssertTrue(!ids.Contains(outsideId), "the row outside the UTC window stays out");
            AssertEqual(1L, result.TotalRecords, "the UTC window total is the inside row");
        }

        private static List<string> IdList<T>(List<T> items, Func<T, string> id)
        {
            List<string> ids = new List<string>();
            foreach (T item in items) ids.Add(id(item));
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        private static string JoinedIds<T>(List<T> items, Func<T, string> id)
        {
            return String.Join(",", IdList(items, id));
        }

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            using (NpgsqlConnection connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync().ConfigureAwait(false);
                using (NpgsqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task<string> ScalarAsync(string connectionString, string sql)
        {
            using (NpgsqlConnection connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync().ConfigureAwait(false);
                using (NpgsqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    object? value = await command.ExecuteScalarAsync().ConfigureAwait(false);
                    return value == null || value == DBNull.Value ? String.Empty : Convert.ToString(value) ?? String.Empty;
                }
            }
        }

        #endregion
    }
}
