namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Settings;
    using Armada.Server.Routes;
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

            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "server_database_check_queries_the_live_driver",
                displayName: "Doctor reports a server database as Pass only after a query answers, naming the provider and host",
                executeAsync: CheckServerDatabaseQueriesDriverAsync,
                tags: new List<string> { TestTags.Positive, TestTags.EndToEnd }));

            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "database_probe_reports_errors_and_timeouts",
                displayName: "The doctor database probe reports a failing or hanging query instead of passing",
                executeAsync: CheckProbeFailuresAsync,
                tags: new List<string> { TestTags.Negative }));

            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "effective_port_not_written_to_settings",
                displayName: "The derived database port is not written to settings.json",
                executeAsync: CheckEffectivePortNotSerializedAsync,
                tags: new List<string> { TestTags.Positive }));

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

                    AssertEqual(DoctorCheckStatusEnum.Pass, check.Status, "a reachable PostgreSQL database passes");
                    AssertContains("Postgresql database at", check.Message, "message names the provider");
                    AssertContains("answered a query", check.Message, "message says the database answered");
                    AssertContains(isolated.Settings.Hostname, check.Message, "message names the database host");
                    AssertContains((isolated.Settings.Port > 0 ? isolated.Settings.Port : 5432).ToString(), check.Message, "message names the effective PostgreSQL port");
                    AssertContains(isolated.Settings.DatabaseName, check.Message, "message names the database");
                    AssertFalse(check.Message.Contains("Database not found", StringComparison.OrdinalIgnoreCase), "message does not report the unused SQLite file");
                }
            }
        }

        private static async Task CheckServerDatabaseQueriesDriverAsync(CancellationToken token)
        {
            string dataDirectory = TestTemp.NewDirectory("doctor_server_db");
            DatabaseSettings database = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(dataDirectory, "armada.db") };

            using (InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(dataDirectory, database).ConfigureAwait(false))
            {
                // Report a server provider while the live driver stays SQLite: the check must take the server-database
                // branch, query the live driver, and never look for the SQLite file.
                server.Settings.Database.Type = DatabaseTypeEnum.Mysql;
                server.Settings.Database.Hostname = "db.internal.example";
                server.Settings.Database.DatabaseName = "armada_prod";
                server.Settings.DatabasePath = Path.Combine(dataDirectory, "missing.db");

                DoctorCheck check = await ReadDatabaseCheckAsync(server, token).ConfigureAwait(false);
                AssertEqual(DoctorCheckStatusEnum.Pass, check.Status, "an answering database passes");
                AssertEqual("Mysql database at db.internal.example:3306/armada_prod answered a query", check.Message, "message names the provider, host, default port and database");
                AssertFalse(check.Message.Contains("not found", StringComparison.OrdinalIgnoreCase), "the SQLite file is not checked");
            }
        }

        private static async Task CheckProbeFailuresAsync(CancellationToken token)
        {
            string? passed = await DoctorDatabaseProbe.ProbeAsync(ct => Task.CompletedTask, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            AssertNull(passed, "an answering query passes");

            string? failed = await DoctorDatabaseProbe.ProbeAsync(
                async ct =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("connection refused by db.internal.example");
                },
                TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            AssertEqual("connection refused by db.internal.example", failed, "a failing query reports its error");

            string? thrown = await DoctorDatabaseProbe.ProbeAsync(ct => throw new InvalidOperationException("no driver"), TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            AssertEqual("no driver", thrown, "a query that throws before returning a task reports its error");

            DateTime started = DateTime.UtcNow;
            string? cancelled = await DoctorDatabaseProbe.ProbeAsync(ct => Task.Delay(Timeout.Infinite, ct), TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
            AssertContains("no response within", cancelled ?? "", "a query cancelled at the timeout reports a timeout");

            TaskCompletionSource<bool> neverAnswers = new TaskCompletionSource<bool>();
            string? hung = await DoctorDatabaseProbe.ProbeAsync(ct => neverAnswers.Task, TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
            AssertContains("no response within", hung ?? "", "a query that ignores cancellation still times out");
            AssertTrue(DateTime.UtcNow - started < TimeSpan.FromSeconds(5), "timeouts do not hold the response open");
        }

        private static Task CheckEffectivePortNotSerializedAsync(CancellationToken token)
        {
            DatabaseSettings database = new DatabaseSettings { Type = DatabaseTypeEnum.Postgresql, Hostname = "db" };
            AssertEqual(5432, database.EffectivePort, "PostgreSQL default port");
            string json = JsonSerializer.Serialize(database, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            AssertFalse(json.Contains("effectivePort", StringComparison.OrdinalIgnoreCase), "EffectivePort is not serialized: " + json);

            ArmadaSettings settings = new ArmadaSettings();
            settings.Database.Type = DatabaseTypeEnum.SqlServer;
            string settingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never });
            AssertFalse(settingsJson.Contains("effectivePort", StringComparison.OrdinalIgnoreCase), "settings.json does not carry EffectivePort");
            return Task.CompletedTask;
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
