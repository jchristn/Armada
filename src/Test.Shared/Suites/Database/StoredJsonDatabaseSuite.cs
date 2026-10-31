namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Microsoft.Data.Sqlite;
    using MySqlConnector;
    using Npgsql;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Verifies that invalid stored JSON remains a read failure and is not replaced by a default value.
    /// </summary>
    public sealed class StoredJsonDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.StoredJson";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the stored JSON regression cases.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("project_profile_invalid_skills_json_fails_without_rewrite", "Invalid project profile JSON fails without rewrite", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    ProjectProfile profile = new ProjectProfile { Name = UniqueName("stored-json-project") };
                    await testDb.Driver.ProjectProfiles.CreateAsync(profile);
                    await SetInvalidJsonAsync(testDb, "project_profiles", "skills_json", profile.Id);
                    await AssertReadFailsAsync(() => testDb.Driver.ProjectProfiles.ReadAsync(profile.Id), "skills_json", profile.Id);
                    await AssertStoredJsonAsync(testDb, "project_profiles", "skills_json", profile.Id);
                }
            }));

            cases.Add(CaseAsync("project_profile_empty_json_keeps_default", "Empty project profile JSON keeps its default", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    ProjectProfile profile = new ProjectProfile { Name = UniqueName("stored-json-project-empty") };
                    await testDb.Driver.ProjectProfiles.CreateAsync(profile);
                    using (DbConnection connection = CreateConnection(testDb))
                    {
                        await connection.OpenAsync();
                        using (DbCommand command = connection.CreateCommand())
                        {
                            command.CommandText = "UPDATE project_profiles SET skills_json = '' WHERE id = @id";
                            AddParameter(command, "@id", profile.Id);
                            AssertEqual(1, await command.ExecuteNonQueryAsync(), "one row has an empty JSON value");
                        }
                    }

                    ProjectProfile? read = await testDb.Driver.ProjectProfiles.ReadAsync(profile.Id);
                    AssertNotNull(read);
                    AssertEqual(0, read!.Skills.Count, "empty JSON keeps the empty-list default");
                }
            }));

            cases.Add(CaseAsync("workflow_profile_invalid_json_fails", "Invalid workflow profile JSON fails", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    WorkflowProfile profile = new WorkflowProfile { Name = UniqueName("stored-json-workflow") };
                    await testDb.Driver.WorkflowProfiles.CreateAsync(profile);
                    await SetInvalidJsonAsync(testDb, "workflow_profiles", "language_hints_json", profile.Id);
                    await AssertReadFailsAsync(() => testDb.Driver.WorkflowProfiles.ReadAsync(profile.Id), "language_hints_json", profile.Id);
                    await AssertStoredJsonAsync(testDb, "workflow_profiles", "language_hints_json", profile.Id);
                }
            }));

            cases.Add(CaseAsync("deployment_invalid_json_fails", "Invalid deployment JSON fails", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DeploymentEnvironment environment = new DeploymentEnvironment { Name = UniqueName("stored-json-environment") };
                    await testDb.Driver.Environments.CreateAsync(environment);
                    Deployment deployment = new Deployment { EnvironmentId = environment.Id, Title = UniqueName("stored-json-deployment") };
                    await testDb.Driver.Deployments.CreateAsync(deployment);
                    await SetInvalidJsonAsync(testDb, "deployments", "check_run_ids_json", deployment.Id);
                    await AssertReadFailsAsync(() => testDb.Driver.Deployments.ReadAsync(deployment.Id), "check_run_ids_json", deployment.Id);
                    await AssertStoredJsonAsync(testDb, "deployments", "check_run_ids_json", deployment.Id);
                }
            }));

            cases.Add(CaseAsync("release_invalid_json_fails", "Invalid release JSON fails", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Release release = new Release { Title = UniqueName("stored-json-release") };
                    await testDb.Driver.Releases.CreateAsync(release);
                    await SetInvalidJsonAsync(testDb, "releases", "voyage_ids_json", release.Id);
                    await AssertReadFailsAsync(() => testDb.Driver.Releases.ReadAsync(release.Id), "voyage_ids_json", release.Id);
                    await AssertStoredJsonAsync(testDb, "releases", "voyage_ids_json", release.Id);
                }
            }));

            cases.Add(CaseAsync("deployment_environment_invalid_json_fails", "Invalid deployment environment JSON fails", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DeploymentEnvironment environment = new DeploymentEnvironment { Name = UniqueName("stored-json-environment-read"), Kind = EnvironmentKindEnum.Development };
                    await testDb.Driver.Environments.CreateAsync(environment);
                    await SetInvalidJsonAsync(testDb, "environments", "verification_definitions_json", environment.Id);
                    await AssertReadFailsAsync(() => testDb.Driver.Environments.ReadAsync(environment.Id), "verification_definitions_json", environment.Id);
                    await AssertStoredJsonAsync(testDb, "environments", "verification_definitions_json", environment.Id);
                }
            }));

            cases.Add(CaseAsync("vessel_invalid_path_json_fails", "Invalid vessel path JSON fails", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    string name = UniqueName("stored-json-vessel");
                    Vessel vessel = new Vessel { Name = name, RepoUrl = "https://example.invalid/" + name };
                    await testDb.Driver.Vessels.CreateAsync(vessel);
                    await SetInvalidJsonAsync(testDb, "vessels", "protected_path_patterns_json", vessel.Id);
                    await AssertReadFailsAsync(() => testDb.Driver.Vessels.ReadAsync(vessel.Id), "protected_path_patterns_json", vessel.Id);
                    await AssertStoredJsonAsync(testDb, "vessels", "protected_path_patterns_json", vessel.Id);
                }
            }));

            // Planning-session persistence exists on SQLite only. The other providers throw
            // NotSupportedException and have no reader, so this case is an explicit skip there.
            bool skipPlanning = !TestDatabaseConfig.IsSqlite;
            cases.Add(CaseAsync(
                "planning_session_invalid_playbooks_json_fails",
                "Invalid selected playbooks JSON fails",
                TestTags.Negative,
                async () =>
                {
                    using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                    {
                        PlanningSession session = new PlanningSession
                        {
                            CaptainId = "cpt_test",
                            VesselId = "vsl_test",
                            Title = UniqueName("stored-json-planning")
                        };
                        await testDb.Driver.PlanningSessions.CreateAsync(session);
                        await SetInvalidJsonAsync(testDb, "planning_sessions", "selected_playbooks_json", session.Id);
                        await AssertReadFailsAsync(() => testDb.Driver.PlanningSessions.ReadAsync(session.Id), "selected_playbooks_json", session.Id);
                        await AssertStoredJsonAsync(testDb, "planning_sessions", "selected_playbooks_json", session.Id);
                    }
                },
                skipPlanning,
                skipPlanning ? "Planning sessions are implemented for SQLite-backed deployments only." : null));

            return new TestSuiteDescriptor(SuiteId, "Stored JSON persistence", cases);
        }

        #endregion

        #region Private-Methods

        private static string UniqueName(string label)
        {
            return label + "-" + Guid.NewGuid().ToString("N");
        }

        private static async Task SetInvalidJsonAsync(TestDatabase testDb, string table, string column, string id)
        {
            using (DbConnection connection = CreateConnection(testDb))
            {
                await connection.OpenAsync();
                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE " + table + " SET " + column + " = @value WHERE id = @id";
                    AddParameter(command, "@value", "{invalid");
                    AddParameter(command, "@id", id);
                    AssertEqual(1, await command.ExecuteNonQueryAsync(), "one row was corrupted for the test");
                }
            }
        }

        private static async Task AssertStoredJsonAsync(TestDatabase testDb, string table, string column, string id)
        {
            using (DbConnection connection = CreateConnection(testDb))
            {
                await connection.OpenAsync();
                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT " + column + " FROM " + table + " WHERE id = @id";
                    AddParameter(command, "@id", id);
                    object? stored = await command.ExecuteScalarAsync();
                    AssertEqual("{invalid", stored as string, "invalid stored text is unchanged");
                }
            }
        }

        private static DbConnection CreateConnection(TestDatabase testDb)
        {
            switch (TestDatabaseConfig.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    return new SqliteConnection(testDb.ConnectionString);
                case DatabaseTypeEnum.Postgresql:
                    return new NpgsqlConnection(testDb.ConnectionString);
                case DatabaseTypeEnum.Mysql:
                    return new MySqlConnection(testDb.ConnectionString);
                case DatabaseTypeEnum.SqlServer:
                    return new SqlConnection(testDb.ConnectionString);
                default:
                    throw new NotSupportedException("The configured database provider is not supported by this suite.");
            }
        }

        private static void AddParameter(DbCommand command, string name, string value)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        private static async Task AssertReadFailsAsync<T>(Func<Task<T?>> read, string column, string id)
        {
            try
            {
                await read();
            }
            catch (InvalidOperationException exception)
            {
                AssertContains(column, exception.Message, "exception names the column");
                AssertContains(id, exception.Message, "exception names the row id");
                return;
            }

            throw new InvalidOperationException("Expected invalid stored JSON to fail for " + column + " on " + id + ".");
        }

        private static TestCaseDescriptor CaseAsync(
            string caseId,
            string displayName,
            string tag,
            Func<Task> body,
            bool skip = false,
            string? skipReason = null)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag },
                skip: skip,
                skipReason: skipReason);
        }

        #endregion
    }
}
