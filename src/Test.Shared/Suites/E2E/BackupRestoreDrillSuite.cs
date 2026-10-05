namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Restore drill (V1 readiness W3.5) for SQLite: take a backup through GET /api/v1/backup, change data, restore it
    /// through POST /api/v1/restore, restart the Admiral, and verify the data is exactly as it was at backup time,
    /// with a pre-restore safety backup left under the data directory. Also checks that the built-in backup endpoints
    /// refuse server providers (which use their own dump tools; see docs/UPGRADING.md) instead of silently backing
    /// up an unrelated SQLite file. Each case runs its own Admiral on a private data directory.
    /// </summary>
    public sealed class BackupRestoreDrillSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.BackupRestoreDrill";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Backup Restore Drill suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("sqlite_backup_mutate_restore_restart", "SQLite: backup via API, change data, restore via API, restart, data matches the backup", async ct =>
            {
                string dataDir = TestTemp.NewDirectory("restore_drill");
                DatabaseSettings db = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(dataDir, "armada.db") };

                using (InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(dataDir, db).ConfigureAwait(false))
                {
                    Fleet kept = await CreateFleetAsync(server.Client, "drill-kept", "before backup").ConfigureAwait(false);
                    Fleet renamed = await CreateFleetAsync(server.Client, "drill-original-name", "before backup").ConfigureAwait(false);
                    await server.Settings.SaveAsync().ConfigureAwait(false);

                    HttpResponseMessage backup = await server.Client.GetAsync("/api/v1/backup", ct).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, backup, "backup download");
                    byte[] zipBytes = await backup.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                    AssertTrue(zipBytes.Length > 0, "backup has content");
                    using (ZipArchive zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
                    {
                        AssertNotNull(zip.GetEntry("armada.db"), "zip holds the database");
                        AssertNotNull(zip.GetEntry("manifest.json"), "zip holds the manifest");
                        AssertNotNull(zip.GetEntry("settings.json"), "zip holds the settings file from the data directory");
                    }

                    AssertTrue(Directory.GetFiles(Path.Combine(dataDir, "backups"), "armada-backup-*.zip").Length == 1, "backup written under the server's data directory");

                    // Change data after the backup: add a fleet, rename one, delete one.
                    Fleet added = await CreateFleetAsync(server.Client, "drill-added-after-backup", "after backup").ConfigureAwait(false);
                    HttpResponseMessage rename = await server.Client.PutAsync("/api/v1/fleets/" + renamed.Id,
                        JsonHelper.ToJsonContent(new { Name = "drill-renamed-after-backup", Description = "after backup" }), ct).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, rename, "rename after backup");
                    HttpResponseMessage delete = await server.Client.DeleteAsync("/api/v1/fleets/" + kept.Id, ct).ConfigureAwait(false);
                    AssertTrue(delete.IsSuccessStatusCode, "delete after backup");

                    ByteArrayContent upload = new ByteArrayContent(zipBytes);
                    upload.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                    upload.Headers.Add("X-Original-Filename", "drill.zip");
                    HttpResponseMessage restore = await server.Client.PostAsync("/api/v1/restore", upload, ct).ConfigureAwait(false);
                    string restoreBody = await restore.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, restore, "restore: " + restoreBody);
                    E2eRestoreResult restored = JsonHelper.Deserialize<E2eRestoreResult>(restoreBody);
                    AssertEqual("restored", restored.Status, "restore status");
                    AssertFalse(String.IsNullOrEmpty(restored.BackupPath), "restore reports the safety backup path");
                    AssertTrue(Directory.GetFiles(Path.Combine(dataDir, "backups"), "pre-restore-*.zip").Length == 1, "safety backup of the replaced state under the data directory");

                    server.Stop();
                    await server.StartAgainAsync().ConfigureAwait(false);

                    List<Fleet> fleets = await ListFleetsAsync(server.Client).ConfigureAwait(false);
                    AssertNotNull(fleets.FirstOrDefault(f => f.Id == kept.Id), "fleet deleted after the backup is back");
                    Fleet? original = fleets.FirstOrDefault(f => f.Id == renamed.Id);
                    AssertNotNull(original, "renamed fleet present");
                    AssertEqual("drill-original-name", original!.Name, "rename after the backup is undone");
                    AssertNull(fleets.FirstOrDefault(f => f.Id == added.Id), "fleet added after the backup is gone");

                    HttpResponseMessage health = await server.Client.GetAsync("/api/v1/status/health", ct).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, health, "server healthy after restore and restart");
                }
            }, sqliteOnly: true));

            cases.Add(Case("restore_rejects_non_backup_zip", "Restore rejects a ZIP without an Armada database and leaves data untouched", async ct =>
            {
                string dataDir = TestTemp.NewDirectory("restore_reject");
                DatabaseSettings db = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(dataDir, "armada.db") };
                using (InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(dataDir, db).ConfigureAwait(false))
                {
                    Fleet fleet = await CreateFleetAsync(server.Client, "reject-kept", "kept").ConfigureAwait(false);

                    MemoryStream bogus = new MemoryStream();
                    using (ZipArchive zip = new ZipArchive(bogus, ZipArchiveMode.Create, true))
                    {
                        ZipArchiveEntry entry = zip.CreateEntry("readme.txt");
                        using (StreamWriter writer = new StreamWriter(entry.Open())) writer.Write("not a backup");
                    }

                    ByteArrayContent upload = new ByteArrayContent(bogus.ToArray());
                    upload.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                    HttpResponseMessage restore = await server.Client.PostAsync("/api/v1/restore", upload, ct).ConfigureAwait(false);
                    string restoreBody = await restore.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    AssertFalse(restore.IsSuccessStatusCode, "invalid backup rejected");
                    // TODO(R5, production): the validation failure is an InvalidOperationException the route does not map,
                    // so it arrives as 500 InternalError instead of 400. Until then, prove the refusal is the backup
                    // validation (exact message) rather than an unrelated crash.
                    ApiErrorProbe restoreError = ApiErrorProbe.From(restoreBody);
                    AssertNotNull(restoreError.Error, "typed error body: " + restoreBody);
                    AssertEqual("ZIP does not contain armada.db entry", restoreError.Message, "rejected by backup validation");

                    List<Fleet> fleets = await ListFleetsAsync(server.Client).ConfigureAwait(false);
                    AssertNotNull(fleets.FirstOrDefault(f => f.Id == fleet.Id), "data untouched");
                }
            }, sqliteOnly: true));

            cases.Add(Case("server_provider_backup_refused", "Built-in backup and restore refuse server providers with instructions", async ct =>
            {
                string dataDir = TestTemp.NewDirectory("restore_server_provider");
                using (IsolatedTestDatabase isolated = await IsolatedTestDatabase.CreateAsync("restoredrill", dataDir, ct).ConfigureAwait(false))
                using (InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(dataDir, isolated.Settings).ConfigureAwait(false))
                {
                    HttpResponseMessage backup = await server.Client.GetAsync("/api/v1/backup", ct).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.BadRequest, backup, "backup refused");
                    ApiErrorProbe backupError = ApiErrorProbe.From(await backup.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                    AssertEqual(WatsonWebserver.Core.ApiResultEnum.BadRequest, backupError.Error, "backup refused with a typed error");
                    AssertContains("docs/UPGRADING.md", backupError.Message ?? "", "the message points at the procedure");

                    ByteArrayContent upload = new ByteArrayContent(new byte[] { 1, 2, 3 });
                    HttpResponseMessage restore = await server.Client.PostAsync("/api/v1/restore", upload, ct).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.BadRequest, restore, "restore refused");
                    AssertEqual(WatsonWebserver.Core.ApiResultEnum.BadRequest, ApiErrorProbe.From(await restore.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Error, "restore refused with a typed error");
                    AssertFalse(Directory.Exists(Path.Combine(dataDir, "backups")) && Directory.GetFiles(Path.Combine(dataDir, "backups")).Length > 0, "nothing written");
                }
            }, serverOnly: true));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Backup Restore Drill",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Fleet> CreateFleetAsync(HttpClient client, string name, string description)
        {
            HttpResponseMessage response = await client.PostAsync("/api/v1/fleets", JsonHelper.ToJsonContent(new { Name = name, Description = description })).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.Created, response, "create fleet " + name);
            return await JsonHelper.DeserializeAsync<Fleet>(response).ConfigureAwait(false);
        }

        private static async Task<List<Fleet>> ListFleetsAsync(HttpClient client)
        {
            HttpResponseMessage response = await client.GetAsync("/api/v1/fleets?pageSize=1000").ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.OK, response, "list fleets");
            EnumerationResult<Fleet> page = await JsonHelper.DeserializeAsync<EnumerationResult<Fleet>>(response).ConfigureAwait(false);
            return page.Objects;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body, bool sqliteOnly = false, bool serverOnly = false)
        {
            bool skip = (sqliteOnly && !TestDatabaseConfig.IsSqlite) || (serverOnly && TestDatabaseConfig.IsSqlite);
            string? reason = null;
            if (skip) reason = sqliteOnly
                ? "Built-in backup and restore are SQLite only; server providers use their own dump tools (docs/UPGRADING.md)."
                : "Runs only against a server provider (PostgreSQL, MySQL, SQL Server).";

            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: body,
                tags: new List<string> { TestTags.Positive, TestTags.EndToEnd },
                skip: skip,
                skipReason: reason);
        }

        #endregion
    }
}
