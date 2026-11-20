namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// GET /api/v1/doctor reports the active database configuration and keeps SQLite file checks.
    /// </summary>
    public sealed class DoctorDatabaseSuite : IArmadaTestSuite
    {
        private const string SuiteId = "E2E.DoctorDatabase";

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "sqlite_file_check",
                displayName: "Doctor checks missing and existing SQLite database files",
                executeAsync: CheckSqliteDatabaseAsync,
                tags: new List<string> { TestTags.Positive, TestTags.EndToEnd }));

            bool postgresOnlySkip = TestDatabaseConfig.Type != DatabaseTypeEnum.Postgresql;
            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "postgres_configuration_without_sqlite_file",
                displayName: "Doctor reports configured PostgreSQL without probing a SQLite file",
                executeAsync: CheckPostgresqlConfigurationAsync,
                tags: new List<string> { TestTags.Positive, TestTags.EndToEnd },
                skip: postgresOnlySkip,
                skipReason: postgresOnlySkip ? "Requires the PostgreSQL test provider; run with ARMADA_TEST_DB_TYPE=postgresql and its connection settings." : null));

            return new TestSuiteDescriptor(SuiteId, "Doctor Database", cases);
        }

        private static async Task CheckSqliteDatabaseAsync(CancellationToken token)
        {
            string dataDirectory = TestTemp.NewDirectory("doctor_sqlite");
            string databasePath = Path.Combine(dataDirectory, "armada.db");
            DatabaseSettings database = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = databasePath };

            using (InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(dataDirectory, database).ConfigureAwait(false))
            {
                string missingPath = Path.Combine(dataDirectory, "missing.db");
                AssertFalse(File.Exists(missingPath), "missing SQLite file fixture");
                server.Settings.DatabasePath = missingPath;
                DoctorCheck missing = await ReadDatabaseCheckAsync(server, token).ConfigureAwait(false);
                AssertEqual(DoctorCheckStatusEnum.Warn, missing.Status, "missing SQLite database warns");
                AssertContains("Database not found", missing.Message, "missing SQLite message");

                AssertTrue(File.Exists(databasePath), "SQLite database file exists");
                server.Settings.DatabasePath = databasePath;
                DoctorCheck existing = await ReadDatabaseCheckAsync(server, token).ConfigureAwait(false);
                AssertEqual(DoctorCheckStatusEnum.Pass, existing.Status, "existing SQLite database passes");
                AssertContains("Database exists", existing.Message, "existing SQLite message");
            }
        }

        private static async Task CheckPostgresqlConfigurationAsync(CancellationToken token)
        {
            string dataDirectory = TestTemp.NewDirectory("doctor_postgresql");
            string unusedSqlitePath = Path.Combine(dataDirectory, "unused.db");

            using (IsolatedTestDatabase isolated = await IsolatedTestDatabase.CreateAsync("doctor", dataDirectory, token).ConfigureAwait(false))
            {
                using (InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(
                    dataDirectory,
                    isolated.Settings,
                    settings => settings.DatabasePath = unusedSqlitePath).ConfigureAwait(false))
                {
                    AssertFalse(File.Exists(unusedSqlitePath), "unused SQLite file is absent");
                    DoctorCheck check = await ReadDatabaseCheckAsync(server, token).ConfigureAwait(false);

                    AssertEqual(DoctorCheckStatusEnum.Pass, check.Status, "configured PostgreSQL database passes");
                    AssertContains("configuration", check.Message, "message identifies a configuration check");
                    AssertContains(isolated.Settings.Hostname, check.Message, "message names the database host");
                    AssertContains((isolated.Settings.Port > 0 ? isolated.Settings.Port : 5432).ToString(), check.Message, "message names the effective PostgreSQL port");
                    AssertContains(isolated.Settings.DatabaseName, check.Message, "message names the database");
                    AssertFalse(check.Message.Contains("Database not found", StringComparison.OrdinalIgnoreCase), "message does not report the unused SQLite file");
                    AssertFalse(check.Message.Contains("connected", StringComparison.OrdinalIgnoreCase), "message does not claim a successful connection");
                }
            }
        }

        private static async Task<DoctorCheck> ReadDatabaseCheckAsync(InProcessArmadaServer server, CancellationToken token)
        {
            using (HttpResponseMessage response = await server.Client.GetAsync("/api/v1/doctor", token).ConfigureAwait(false))
            {
                AssertEqual(HttpStatusCode.OK, response.StatusCode, "doctor response");
                List<DoctorCheck> checks = await JsonHelper.DeserializeAsync<List<DoctorCheck>>(response).ConfigureAwait(false);
                foreach (DoctorCheck check in checks)
                {
                    if (check.Name == "Database") return check;
                }

                throw new InvalidOperationException("Doctor response did not include the Database check.");
            }
        }
    }
}
