namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Retention settings over REST (V1 readiness W3.4): GET /api/v1/settings returns the Retention group with its
    /// defaults, and PUT replaces it, clamps out-of-range values, applies it live, and persists it to the server's
    /// settings file. Runs a private Admiral so the shared fixture's settings are never changed.
    /// </summary>
    public sealed class RetentionSettingsApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.RetentionSettingsApi";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Retention Settings API suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "get_put_retention_settings",
                displayName: "GET returns retention defaults; PUT replaces, clamps, applies live, and persists",
                executeAsync: async (CancellationToken ct) =>
                {
                    string dataDir = TestTemp.NewDirectory("retention_api");
                    DatabaseSettings db = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(dataDir, "armada.db") };
                    using (InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(dataDir, db).ConfigureAwait(false))
                    {
                        HttpResponseMessage get = await server.Client.GetAsync("/api/v1/settings", ct).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.OK, get, "GET settings");
                        RetentionSettingsEnvelope before = await JsonHelper.DeserializeAsync<RetentionSettingsEnvelope>(get).ConfigureAwait(false);
                        AssertNotNull(before.Retention, "Retention group returned");
                        AssertEqual(90, before.Retention!.AskThreadArchiveAfterDays, "archive default");
                        AssertEqual(0, before.Retention.AskThreadDeleteAfterDays, "delete default");
                        AssertEqual(30, before.Retention.JobRetentionDays, "jobs default");
                        AssertEqual(90, before.Retention.ImportBatchRetentionDays, "imports default");

                        HttpResponseMessage put = await server.Client.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new
                        {
                            Retention = new { AskThreadArchiveAfterDays = 30, AskThreadDeleteAfterDays = 99999, JobRetentionDays = -5 }
                        }), ct).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.OK, put, "PUT settings");
                        RetentionSettingsEnvelope after = await JsonHelper.DeserializeAsync<RetentionSettingsEnvelope>(put).ConfigureAwait(false);
                        AssertEqual(30, after.Retention!.AskThreadArchiveAfterDays, "archive updated");
                        AssertEqual(3650, after.Retention.AskThreadDeleteAfterDays, "delete clamped to 3650");
                        AssertEqual(0, after.Retention.JobRetentionDays, "negative clamped to 0 (never)");
                        AssertEqual(90, after.Retention.ImportBatchRetentionDays, "omitted field takes its default");

                        AssertEqual(30, server.Settings.Retention.AskThreadArchiveAfterDays, "applied live to the running server");
                        string settingsFile = Path.Combine(dataDir, "settings.json");
                        AssertTrue(File.Exists(settingsFile), "persisted to the server's settings file");
                        ArmadaSettings reloaded = await ArmadaSettings.LoadAsync(settingsFile).ConfigureAwait(false);
                        AssertEqual(30, reloaded.Retention.AskThreadArchiveAfterDays, "persisted archive days");
                        AssertEqual(3650, reloaded.Retention.AskThreadDeleteAfterDays, "persisted delete days");

                        HttpResponseMessage untouched = await server.Client.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { MaxCaptains = 3 }), ct).ConfigureAwait(false);
                        RetentionSettingsEnvelope kept = await JsonHelper.DeserializeAsync<RetentionSettingsEnvelope>(untouched).ConfigureAwait(false);
                        AssertEqual(30, kept.Retention!.AskThreadArchiveAfterDays, "a PUT without Retention leaves it unchanged");
                    }
                },
                tags: new List<string> { TestTags.Positive, TestTags.EndToEnd }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Retention Settings API",
                cases: cases);
        }

        #endregion
    }
}
