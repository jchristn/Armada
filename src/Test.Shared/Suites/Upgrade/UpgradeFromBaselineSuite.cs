namespace Test.Shared.Suites.Upgrade
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Upgrade test (V1 readiness W3.1). Starts an older Armada (the baseline, by default v0.9.0) out of process against
    /// a throwaway database on the configured provider, seeds representative data through its REST API (fleets,
    /// vessels, a captain, missions in every status, voyages, merge queue entries, personas and pipelines with edits,
    /// prompt template overrides, signals, a backlog objective, a second tenant with a user and credential; events and
    /// request history accrue on their own), stops it, then lets the current build take over the same database: the
    /// pre-migration backup check runs and migrations apply, every seeded record is verified in the database, and the
    /// current Admiral is started on it to verify the data through its API, that edited templates, personas, and
    /// pipelines were preserved, and that built-in upgrades (new templates, persona memory recall, the FullPipeline
    /// Recorder stage, fleet action built-ins) were applied.
    /// Requires ARMADA_UPGRADE_BASELINE_HOST (see scripts/common/run-upgrade-test.sh); skipped otherwise. Cases share
    /// state and run in order.
    /// </summary>
    public sealed class UpgradeFromBaselineSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Upgrade.FromBaseline";

        private static readonly MissionStatusEnum[] _AllStatuses = (MissionStatusEnum[])Enum.GetValues(typeof(MissionStatusEnum));

        private readonly UpgradeSeedManifest _Seed = new UpgradeSeedManifest();
        private string _WorkDir = "";
        private IsolatedTestDatabase? _Database;
        private BaselineArmadaProcess? _Baseline;
        private InProcessArmadaServer? _Current;
        private MigrationBackupResult? _BackupResult;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the upgrade suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("baseline_starts", "Baseline Admiral starts on a throwaway database", BaselineStartsAsync));
            cases.Add(Case("baseline_seeded", "Representative data is seeded through the baseline API", SeedAsync));
            cases.Add(Case("baseline_stopped", "Baseline Admiral stops cleanly", BaselineStopsAsync));
            cases.Add(Case("current_backs_up_and_migrates", "Current build backs up (SQLite) or warns (server providers), then migrates to the latest schema", MigrateAsync));
            cases.Add(Case("data_preserved_in_database", "Every seeded record is intact after migration", VerifyDatabaseAsync));
            cases.Add(Case("current_admiral_serves_upgraded_data", "Current Admiral starts on the upgraded database and serves the data", VerifyCurrentServerAsync));
            cases.Add(Case("edits_preserved_and_builtins_upgraded", "Edited templates, personas, and pipelines are preserved and built-in upgrades are applied", VerifyTemplatesAndBuiltInsAsync));
            cases.Add(Case("cleanup", "Stop the current Admiral and drop the throwaway database", CleanupAsync));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Upgrade From Baseline (" + BaselineLabel() + ")",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string? HostPath()
        {
            string? path = Environment.GetEnvironmentVariable(BaselineArmadaProcess.HostPathEnvVar);
            return String.IsNullOrWhiteSpace(path) ? null : path;
        }

        private static string BaselineLabel()
        {
            string? label = Environment.GetEnvironmentVariable(BaselineArmadaProcess.BaselineRefEnvVar);
            return String.IsNullOrWhiteSpace(label) ? "baseline" : label!;
        }

        private static LoggingModule QuietLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private async Task BaselineStartsAsync(CancellationToken ct)
        {
            _WorkDir = TestTemp.NewDirectory("upgrade");
            string dataDir = Path.Combine(_WorkDir, "data");
            Directory.CreateDirectory(dataDir);
            _Database = await IsolatedTestDatabase.CreateAsync("upgrade", dataDir, ct).ConfigureAwait(false);
            _Baseline = await BaselineArmadaProcess.StartAsync(HostPath()!, _WorkDir, _Database.Settings).ConfigureAwait(false);

            UpgradeHealthResponse health = await GetAsync<UpgradeHealthResponse>(_Baseline.Client, "/api/v1/status/health").ConfigureAwait(false);
            AssertNotNull(health.Version, "baseline reports a version");
            _Seed.BaselineVersion = health.Version!;
        }

        private async Task SeedAsync(CancellationToken ct)
        {
            HttpClient api = RequireBaseline().Client;

            // Fleet (edited), vessels.
            Fleet fleet = await SendAsync<Fleet>(api, HttpMethod.Post, "/api/v1/fleets", new { Name = "upgrade-fleet", Description = "seeded on the baseline" }).ConfigureAwait(false);
            _Seed.FleetId = fleet.Id;
            _Seed.FleetName = "upgrade-fleet";
            _Seed.FleetDescription = "edited on the baseline";
            await SendAsync<Fleet>(api, HttpMethod.Put, "/api/v1/fleets/" + fleet.Id, new { Name = _Seed.FleetName, Description = _Seed.FleetDescription }).ConfigureAwait(false);

            _Seed.VesselName = "upgrade-vessel";
            _Seed.VesselRepoUrl = "https://example.invalid/armada-upgrade.git";
            _Seed.VesselProjectContext = "Upgrade test project context.";
            Vessel vessel = await SendAsync<Vessel>(api, HttpMethod.Post, "/api/v1/vessels", new
            {
                Name = _Seed.VesselName,
                RepoUrl = _Seed.VesselRepoUrl,
                FleetId = fleet.Id,
                DefaultBranch = "main",
                ProjectContext = _Seed.VesselProjectContext,
                StyleGuide = "Upgrade test style guide."
            }).ConfigureAwait(false);
            _Seed.VesselId = vessel.Id;
            Vessel loose = await SendAsync<Vessel>(api, HttpMethod.Post, "/api/v1/vessels", new { Name = "upgrade-loose-vessel", RepoUrl = "https://example.invalid/loose.git", DefaultBranch = "main" }).ConfigureAwait(false);
            _Seed.LooseVesselId = loose.Id;

            // One mission per status, reached through the baseline's status transitions.
            foreach (MissionStatusEnum status in _AllStatuses)
            {
                string title = "upgrade-mission-" + status;
                UpgradeMissionCreateResponse created = await SendAsync<UpgradeMissionCreateResponse>(api, HttpMethod.Post, "/api/v1/missions", new
                {
                    Title = title,
                    Description = "Mission seeded on the baseline in status " + status + ".",
                    VesselId = vessel.Id
                }).ConfigureAwait(false);
                string missionId = created.MissionId ?? throw new AssertionException("mission create returned no id");
                foreach (MissionStatusEnum step in PathTo(status))
                {
                    await SendAsync<Mission>(api, HttpMethod.Put, "/api/v1/missions/" + missionId + "/status", new { Status = step.ToString() }).ConfigureAwait(false);
                }

                Mission readBack = await GetAsync<Mission>(api, "/api/v1/missions/" + missionId).ConfigureAwait(false);
                AssertEqual(status, readBack.Status, "baseline mission reached " + status);
                _Seed.MissionStatuses[missionId] = status;
                _Seed.MissionTitles[missionId] = title;
            }

            // Voyages: one dispatched with missions, one bare.
            Voyage voyage = await SendAsync<Voyage>(api, HttpMethod.Post, "/api/v1/voyages", new
            {
                Title = "upgrade-voyage",
                Description = "Voyage seeded on the baseline.",
                VesselId = vessel.Id,
                Missions = new[]
                {
                    new { Title = "upgrade-voyage-mission-1", Description = "First voyage mission." },
                    new { Title = "upgrade-voyage-mission-2", Description = "Second voyage mission." }
                }
            }).ConfigureAwait(false);
            _Seed.VoyageId = voyage.Id;
            EnumerationResult<Mission> voyageMissions = await SendAsync<EnumerationResult<Mission>>(api, HttpMethod.Post, "/api/v1/missions/enumerate", new { VoyageId = voyage.Id, PageSize = 100 }).ConfigureAwait(false);
            _Seed.VoyageMissionIds = voyageMissions.Objects.Where(m => m.VoyageId == voyage.Id).Select(m => m.Id).ToList();
            AssertEqual(2, _Seed.VoyageMissionIds.Count, "voyage dispatched two missions");
            foreach (Mission m in voyageMissions.Objects.Where(m => m.VoyageId == voyage.Id))
            {
                _Seed.MissionStatuses[m.Id] = m.Status;
                _Seed.MissionTitles[m.Id] = m.Title;
            }

            Voyage bare = await SendAsync<Voyage>(api, HttpMethod.Post, "/api/v1/voyages", new { Title = "upgrade-bare-voyage", Description = "No missions." }).ConfigureAwait(false);
            _Seed.BareVoyageId = bare.Id;

            // Merge queue: one queued entry, one cancelled.
            string workProduced = _Seed.MissionStatuses.First(kvp => kvp.Value == MissionStatusEnum.WorkProduced).Key;
            string landingFailed = _Seed.MissionStatuses.First(kvp => kvp.Value == MissionStatusEnum.LandingFailed).Key;
            MergeEntry queued = await SendAsync<MergeEntry>(api, HttpMethod.Post, "/api/v1/merge-queue", new { MissionId = workProduced, VesselId = vessel.Id, BranchName = "armada/upgrade-queued", TargetBranch = "main" }).ConfigureAwait(false);
            MergeEntry cancelled = await SendAsync<MergeEntry>(api, HttpMethod.Post, "/api/v1/merge-queue", new { MissionId = landingFailed, VesselId = vessel.Id, BranchName = "armada/upgrade-cancelled", TargetBranch = "main" }).ConfigureAwait(false);
            HttpResponseMessage cancel = await api.DeleteAsync("/api/v1/merge-queue/" + cancelled.Id).ConfigureAwait(false);
            AssertTrue(cancel.IsSuccessStatusCode, "cancel merge entry on the baseline");
            foreach (string entryId in new[] { queued.Id, cancelled.Id })
            {
                MergeEntry entry = await GetAsync<MergeEntry>(api, "/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
                _Seed.MergeEntryStatuses[entryId] = entry.Status.ToString();
            }

            // Personas and pipelines, with edits to custom and built-in records.
            _Seed.CustomPersonaName = "UpgradeReviewer";
            await SendAsync<Persona>(api, HttpMethod.Post, "/api/v1/personas", new { Name = _Seed.CustomPersonaName, Description = "Custom persona seeded on the baseline.", PromptTemplateName = "persona.judge" }).ConfigureAwait(false);
            _Seed.CustomPersonaDescription = "Custom persona edited on the baseline.";
            await SendAsync<Persona>(api, HttpMethod.Put, "/api/v1/personas/" + _Seed.CustomPersonaName, new { Description = _Seed.CustomPersonaDescription }).ConfigureAwait(false);
            _Seed.EditedBuiltInPersonaName = "Architect";
            _Seed.EditedBuiltInPersonaDescription = "Architect description edited on the baseline.";
            await SendAsync<Persona>(api, HttpMethod.Put, "/api/v1/personas/" + _Seed.EditedBuiltInPersonaName, new { Description = _Seed.EditedBuiltInPersonaDescription }).ConfigureAwait(false);

            _Seed.CustomPipelineName = "UpgradePipeline";
            await SendAsync<Pipeline>(api, HttpMethod.Post, "/api/v1/pipelines", new
            {
                Name = _Seed.CustomPipelineName,
                Description = "Custom pipeline seeded on the baseline.",
                Stages = new[] { new { Order = 1, PersonaName = "Worker" } }
            }).ConfigureAwait(false);
            _Seed.CustomPipelineDescription = "Custom pipeline edited on the baseline.";
            _Seed.CustomPipelineStagePersonas = new List<string> { "Worker", _Seed.CustomPersonaName };
            await SendAsync<Pipeline>(api, HttpMethod.Put, "/api/v1/pipelines/" + _Seed.CustomPipelineName, new
            {
                Name = _Seed.CustomPipelineName,
                Description = _Seed.CustomPipelineDescription,
                Stages = new object[]
                {
                    new { Order = 1, PersonaName = "Worker", RequiresReview = true },
                    new { Order = 2, PersonaName = _Seed.CustomPersonaName }
                }
            }).ConfigureAwait(false);
            _Seed.EditedBuiltInPipelineName = "Reviewed";
            _Seed.EditedBuiltInPipelineDescription = "Reviewed pipeline description edited on the baseline.";
            Pipeline reviewed = await GetAsync<Pipeline>(api, "/api/v1/pipelines/" + _Seed.EditedBuiltInPipelineName).ConfigureAwait(false);
            await SendAsync<Pipeline>(api, HttpMethod.Put, "/api/v1/pipelines/" + _Seed.EditedBuiltInPipelineName, new
            {
                Name = reviewed.Name,
                Description = _Seed.EditedBuiltInPipelineDescription,
                Stages = reviewed.Stages.Select(s => new { s.Order, s.PersonaName, s.IsOptional, s.Description, s.RequiresReview, ReviewDenyAction = s.ReviewDenyAction.ToString() }).ToArray()
            }).ConfigureAwait(false);

            // Prompt templates: override a built-in instruction template and a built-in persona template, add a custom one.
            EnumerationResult<PromptTemplate> templates = await GetAsync<EnumerationResult<PromptTemplate>>(api, "/api/v1/prompt-templates?pageSize=1000").ConfigureAwait(false);
            _Seed.BaselineTemplateNames = templates.Objects.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            AssertTrue(_Seed.BaselineTemplateNames.Contains("mission.rules"), "baseline ships mission.rules");
            AssertTrue(_Seed.BaselineTemplateNames.Contains("persona.architect"), "baseline ships persona.architect");
            AssertTrue(_Seed.BaselineTemplateNames.Contains("persona.worker"), "baseline ships persona.worker");

            _Seed.OverriddenTemplateName = "mission.rules";
            _Seed.OverriddenTemplateContent = "## Rules (operator override from the baseline)\n- Keep changes small.\n- Never push to main.\n";
            await SendAsync<PromptTemplate>(api, HttpMethod.Put, "/api/v1/prompt-templates/" + _Seed.OverriddenTemplateName, new { Content = _Seed.OverriddenTemplateContent }).ConfigureAwait(false);
            _Seed.OverriddenPersonaTemplateName = "persona.architect";
            _Seed.OverriddenPersonaTemplateContent = "You are the architect (operator override from the baseline). Plan in small, independent missions.\n";
            await SendAsync<PromptTemplate>(api, HttpMethod.Put, "/api/v1/prompt-templates/" + _Seed.OverriddenPersonaTemplateName, new { Content = _Seed.OverriddenPersonaTemplateContent }).ConfigureAwait(false);
            _Seed.UntouchedPersonaTemplateName = "persona.worker";
            _Seed.CustomTemplateName = "upgrade.custom_notes";
            _Seed.CustomTemplateContent = "Custom template created on the baseline: {{MissionTitle}}";
            await SendAsync<PromptTemplate>(api, HttpMethod.Post, "/api/v1/prompt-templates", new { Name = _Seed.CustomTemplateName, Content = _Seed.CustomTemplateContent, Description = "Upgrade test custom template", Category = "custom" }).ConfigureAwait(false);

            // Captain last, so nothing could be assigned while seeding; signals to it.
            Captain captain = await SendAsync<Captain>(api, HttpMethod.Post, "/api/v1/captains", new { Name = "upgrade-captain", Runtime = "ClaudeCode" }).ConfigureAwait(false);
            _Seed.CaptainId = captain.Id;
            Signal nudge = await SendAsync<Signal>(api, HttpMethod.Post, "/api/v1/signals", new { Type = "Nudge", Payload = "upgrade nudge", ToCaptainId = captain.Id }).ConfigureAwait(false);
            Signal mail = await SendAsync<Signal>(api, HttpMethod.Post, "/api/v1/signals", new { Type = "Mail", Payload = "upgrade mail" }).ConfigureAwait(false);
            _Seed.SignalIds = new List<string> { nudge.Id, mail.Id };

            // Backlog objective.
            _Seed.ObjectiveTitle = "Upgrade backlog objective";
            Objective objective = await SendAsync<Objective>(api, HttpMethod.Post, "/api/v1/objectives", new { Title = _Seed.ObjectiveTitle, Description = "Seeded on the baseline." }).ConfigureAwait(false);
            _Seed.ObjectiveId = objective.Id;

            // Second tenant with a tenant admin, a credential, and a fleet created with that credential.
            TenantMetadata tenant = await SendAsync<TenantMetadata>(api, HttpMethod.Post, "/api/v1/tenants", new { Name = "upgrade-tenant" }).ConfigureAwait(false);
            _Seed.TenantId = tenant.Id;
            UserMaster user = await SendAsync<UserMaster>(api, HttpMethod.Post, "/api/v1/users", new
            {
                TenantId = tenant.Id,
                Email = "upgrade-admin@example.invalid",
                Password = "Upgrade-Test-Password-1",
                IsTenantAdmin = true
            }).ConfigureAwait(false);
            _Seed.UserId = user.Id;
            Credential credential = await SendAsync<Credential>(api, HttpMethod.Post, "/api/v1/credentials", new { TenantId = tenant.Id, UserId = user.Id, Name = "upgrade-credential" }).ConfigureAwait(false);
            _Seed.CredentialId = credential.Id;
            _Seed.BearerToken = credential.BearerToken;
            using (HttpClient tenantApi = BearerClient(RequireBaseline().BaseUrl, credential.BearerToken))
            {
                Fleet tenantFleet = await SendAsync<Fleet>(tenantApi, HttpMethod.Post, "/api/v1/fleets", new { Name = "upgrade-tenant-fleet", Description = "Created with the tenant credential." }).ConfigureAwait(false);
                _Seed.TenantFleetId = tenantFleet.Id;
            }
        }

        private async Task BaselineStopsAsync(CancellationToken ct)
        {
            BaselineArmadaProcess baseline = RequireBaseline();
            bool clean = await baseline.StopAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            string output = baseline.Output;
            baseline.Dispose();
            _Baseline = null;
            AssertTrue(clean, "baseline exited cleanly. Output:\n" + output);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            IsolatedTestDatabase isolated = RequireDatabase();
            _Seed.EventCount = await isolated.CountAsync("events", ct).ConfigureAwait(false);
            _Seed.RequestHistoryCount = await isolated.CountAsync("request_history", ct).ConfigureAwait(false);
            AssertTrue(_Seed.EventCount > 0, "baseline recorded events");
        }

        private async Task MigrateAsync(CancellationToken ct)
        {
            IsolatedTestDatabase isolated = RequireDatabase();
            ArmadaSettings settings = new ArmadaSettings();
            settings.DataDirectory = Path.Combine(_WorkDir, "data");
            settings.Database = isolated.Settings;

            using (DatabaseDriver driver = DatabaseDriverFactory.Create(isolated.Settings, QuietLogging()))
            {
                _Seed.BaselineSchemaVersion = await driver.GetSchemaVersionAsync(ct).ConfigureAwait(false);
                int latest = driver.GetLatestSchemaVersion();
                AssertTrue(_Seed.BaselineSchemaVersion > 0, "baseline left a migrated schema");
                AssertTrue(_Seed.BaselineSchemaVersion < latest, "baseline schema v" + _Seed.BaselineSchemaVersion + " is older than the current v" + latest);

                // The same steps the Admiral runs at startup, before its health loop can touch the data.
                _BackupResult = await new MigrationBackupService(settings, QuietLogging(), null, _ => null).PrepareAsync(driver, ct).ConfigureAwait(false);
                AssertTrue(_BackupResult.MigrationsPending, "migrations pending");
                if (isolated.Type == DatabaseTypeEnum.Sqlite)
                {
                    AssertNotNull(_BackupResult.BackupDirectory, "SQLite pre-migration backup taken");
                    string copy = Path.Combine(_BackupResult.BackupDirectory!, Path.GetFileName(isolated.SqlitePath!));
                    AssertTrue(File.Exists(copy), "backup holds the database file");
                }
                else
                {
                    AssertNotNull(_BackupResult.DumpCommand, "server provider gets a dump command");
                }

                await driver.InitializeAsync(ct).ConfigureAwait(false);
                AssertEqual(latest, await driver.GetSchemaVersionAsync(ct).ConfigureAwait(false), "migrated to the latest schema");
                AssertEqual(0, await driver.GetPendingMigrationCountAsync(ct).ConfigureAwait(false), "nothing pending");
            }
        }

        private async Task VerifyDatabaseAsync(CancellationToken ct)
        {
            IsolatedTestDatabase isolated = RequireDatabase();
            using (DatabaseDriver db = DatabaseDriverFactory.Create(isolated.Settings, QuietLogging()))
            {
                Fleet? fleet = await db.Fleets.ReadAsync(_Seed.FleetId, ct).ConfigureAwait(false);
                AssertNotNull(fleet, "fleet");
                AssertEqual(_Seed.FleetName, fleet!.Name, "fleet name");
                AssertEqual(_Seed.FleetDescription, fleet.Description, "fleet edit");

                Vessel? vessel = await db.Vessels.ReadAsync(_Seed.VesselId, ct).ConfigureAwait(false);
                AssertNotNull(vessel, "vessel");
                AssertEqual(_Seed.VesselName, vessel!.Name, "vessel name");
                AssertEqual(_Seed.VesselRepoUrl, vessel.RepoUrl, "vessel repo url");
                AssertEqual(_Seed.FleetId, vessel.FleetId, "vessel fleet");
                AssertEqual(_Seed.VesselProjectContext, vessel.ProjectContext, "vessel project context");
                AssertNotNull(await db.Vessels.ReadAsync(_Seed.LooseVesselId, ct).ConfigureAwait(false), "loose vessel");
                AssertNotNull(await db.Captains.ReadAsync(_Seed.CaptainId, ct).ConfigureAwait(false), "captain");

                foreach (KeyValuePair<string, MissionStatusEnum> expected in _Seed.MissionStatuses)
                {
                    Mission? mission = await db.Missions.ReadAsync(expected.Key, ct).ConfigureAwait(false);
                    AssertNotNull(mission, "mission " + expected.Key);
                    AssertEqual(expected.Value, mission!.Status, "mission " + _Seed.MissionTitles[expected.Key] + " status");
                    AssertEqual(_Seed.MissionTitles[expected.Key], mission.Title, "mission title");
                    AssertEqual(_Seed.VesselId, mission.VesselId, "mission vessel");
                }

                AssertEqual(_AllStatuses.Length + 2, _Seed.MissionStatuses.Count, "one mission per status plus two voyage missions");
                AssertNotNull(await db.Voyages.ReadAsync(_Seed.VoyageId, ct).ConfigureAwait(false), "voyage");
                AssertNotNull(await db.Voyages.ReadAsync(_Seed.BareVoyageId, ct).ConfigureAwait(false), "bare voyage");
                foreach (string missionId in _Seed.VoyageMissionIds)
                {
                    AssertEqual(_Seed.VoyageId, (await db.Missions.ReadAsync(missionId, ct).ConfigureAwait(false))!.VoyageId, "voyage mission link");
                }

                foreach (KeyValuePair<string, string> entry in _Seed.MergeEntryStatuses)
                {
                    MergeEntry? stored = await db.MergeEntries.ReadAsync(entry.Key, ct).ConfigureAwait(false);
                    AssertNotNull(stored, "merge entry " + entry.Key);
                    AssertEqual(entry.Value, stored!.Status.ToString(), "merge entry status");
                }

                Persona? custom = await db.Personas.ReadByNameAsync(_Seed.CustomPersonaName, ct).ConfigureAwait(false);
                AssertNotNull(custom, "custom persona");
                AssertEqual(_Seed.CustomPersonaDescription, custom!.Description, "custom persona edit");
                Persona? editedBuiltIn = await db.Personas.ReadByNameAsync(_Seed.EditedBuiltInPersonaName, ct).ConfigureAwait(false);
                AssertEqual(_Seed.EditedBuiltInPersonaDescription, editedBuiltIn!.Description, "built-in persona edit");

                Pipeline? pipeline = await db.Pipelines.ReadByNameAsync(_Seed.CustomPipelineName, ct).ConfigureAwait(false);
                AssertNotNull(pipeline, "custom pipeline");
                AssertEqual(_Seed.CustomPipelineDescription, pipeline!.Description, "custom pipeline edit");
                AssertEqual(String.Join(",", _Seed.CustomPipelineStagePersonas), String.Join(",", pipeline.Stages.OrderBy(s => s.Order).Select(s => s.PersonaName)), "custom pipeline stages");
                Pipeline? editedPipeline = await db.Pipelines.ReadByNameAsync(_Seed.EditedBuiltInPipelineName, ct).ConfigureAwait(false);
                AssertEqual(_Seed.EditedBuiltInPipelineDescription, editedPipeline!.Description, "built-in pipeline edit");

                AssertEqual(_Seed.OverriddenTemplateContent, (await db.PromptTemplates.ReadByNameAsync(_Seed.OverriddenTemplateName, ct).ConfigureAwait(false))!.Content, "template override");
                AssertEqual(_Seed.OverriddenPersonaTemplateContent, (await db.PromptTemplates.ReadByNameAsync(_Seed.OverriddenPersonaTemplateName, ct).ConfigureAwait(false))!.Content, "persona template override");
                AssertEqual(_Seed.CustomTemplateContent, (await db.PromptTemplates.ReadByNameAsync(_Seed.CustomTemplateName, ct).ConfigureAwait(false))!.Content, "custom template");

                foreach (string signalId in _Seed.SignalIds)
                {
                    AssertNotNull(await db.Signals.ReadAsync(signalId, ct).ConfigureAwait(false), "signal " + signalId);
                }

                Objective? objective = await db.Objectives.ReadAsync(_Seed.ObjectiveId, ct).ConfigureAwait(false);
                AssertNotNull(objective, "objective");
                AssertEqual(_Seed.ObjectiveTitle, objective!.Title, "objective title");

                AssertNotNull(await db.Tenants.ReadAsync(_Seed.TenantId, ct).ConfigureAwait(false), "second tenant");
                AssertNotNull(await db.Users.ReadByIdAsync(_Seed.UserId, ct).ConfigureAwait(false), "tenant user");
                Credential? credential = await db.Credentials.ReadByIdAsync(_Seed.CredentialId, ct).ConfigureAwait(false);
                AssertEqual(_Seed.BearerToken, credential!.BearerToken, "credential token");
                AssertEqual(_Seed.TenantId, (await db.Fleets.ReadAsync(_Seed.TenantFleetId, ct).ConfigureAwait(false))!.TenantId, "tenant fleet ownership");

                AssertTrue(await isolated.CountAsync("events", ct).ConfigureAwait(false) >= _Seed.EventCount, "events kept");
                AssertTrue(await isolated.CountAsync("request_history", ct).ConfigureAwait(false) >= _Seed.RequestHistoryCount, "request history kept");
            }
        }

        private async Task VerifyCurrentServerAsync(CancellationToken ct)
        {
            IsolatedTestDatabase isolated = RequireDatabase();
            _Current = await InProcessArmadaServer.StartAsync(Path.Combine(_WorkDir, "data"), isolated.Settings, settings =>
            {
                foreach (AgentSettings agent in settings.Agents) agent.Command = "armada-upgrade-test-no-such-agent";
            }).ConfigureAwait(false);
            HttpClient api = _Current.Client;

            UpgradeHealthResponse health = await GetAsync<UpgradeHealthResponse>(api, "/api/v1/status/health").ConfigureAwait(false);
            AssertEqual(Constants.ProductVersion, health.Version, "current build serves the database");

            FleetDetailResponse fleet = await GetAsync<FleetDetailResponse>(api, "/api/v1/fleets/" + _Seed.FleetId).ConfigureAwait(false);
            AssertEqual(_Seed.FleetDescription, fleet.Fleet?.Description, "fleet via API");
            AssertTrue(fleet.Vessels != null && fleet.Vessels.Any(v => v.Id == _Seed.VesselId), "fleet lists its vessel via API");
            Vessel vessel = await GetAsync<Vessel>(api, "/api/v1/vessels/" + _Seed.VesselId).ConfigureAwait(false);
            AssertEqual(_Seed.VesselName, vessel.Name, "vessel via API");
            foreach (string missionId in _Seed.MissionStatuses.Keys)
            {
                Mission mission = await GetAsync<Mission>(api, "/api/v1/missions/" + missionId).ConfigureAwait(false);
                AssertEqual(_Seed.MissionTitles[missionId], mission.Title, "mission via API");
            }

            await GetAsync<Voyage>(api, "/api/v1/voyages/" + _Seed.VoyageId).ConfigureAwait(false);
            foreach (string entryId in _Seed.MergeEntryStatuses.Keys) await GetAsync<MergeEntry>(api, "/api/v1/merge-queue/" + entryId).ConfigureAwait(false);
            await GetAsync<Captain>(api, "/api/v1/captains/" + _Seed.CaptainId).ConfigureAwait(false);
            await GetAsync<Objective>(api, "/api/v1/objectives/" + _Seed.ObjectiveId).ConfigureAwait(false);

            // The seeded tenant's credential still authenticates, and stays scoped to its tenant.
            using (HttpClient tenantApi = BearerClient(_Current.BaseUrl, _Seed.BearerToken))
            {
                EnumerationResult<Fleet> fleets = await GetAsync<EnumerationResult<Fleet>>(tenantApi, "/api/v1/fleets?pageSize=100").ConfigureAwait(false);
                AssertTrue(fleets.Objects.Any(f => f.Id == _Seed.TenantFleetId), "tenant credential sees its fleet");
                AssertFalse(fleets.Objects.Any(f => f.Id == _Seed.FleetId), "tenant credential does not see the other tenant's fleet");
            }
        }

        private async Task VerifyTemplatesAndBuiltInsAsync(CancellationToken ct)
        {
            HttpClient api = RequireCurrent().Client;

            PromptTemplate overridden = await GetAsync<PromptTemplate>(api, "/api/v1/prompt-templates/" + _Seed.OverriddenTemplateName).ConfigureAwait(false);
            AssertEqual(_Seed.OverriddenTemplateContent, overridden.Content, "instruction template override preserved verbatim");
            PromptTemplate personaOverride = await GetAsync<PromptTemplate>(api, "/api/v1/prompt-templates/" + _Seed.OverriddenPersonaTemplateName).ConfigureAwait(false);
            AssertEqual(_Seed.OverriddenPersonaTemplateContent, personaOverride.Content, "persona template override preserved verbatim");
            PromptTemplate custom = await GetAsync<PromptTemplate>(api, "/api/v1/prompt-templates/" + _Seed.CustomTemplateName).ConfigureAwait(false);
            AssertEqual(_Seed.CustomTemplateContent, custom.Content, "custom template preserved");

            // Built-in upgrades: every template this build ships exists (new ones were seeded) and the untouched persona
            // template picked up the memory-recall guidance.
            using (DatabaseDriver db = DatabaseDriverFactory.Create(RequireDatabase().Settings, QuietLogging()))
            {
                PromptTemplateService templates = new PromptTemplateService(db, QuietLogging());
                List<string> shipped = templates.GetEmbeddedDefaultNames();
                List<string> added = shipped.Except(_Seed.BaselineTemplateNames, StringComparer.Ordinal).ToList();
                foreach (string name in shipped)
                {
                    PromptTemplate? template = await db.PromptTemplates.ReadByNameAsync(name, ct).ConfigureAwait(false);
                    AssertNotNull(template, "built-in template " + name + " present after upgrade");
                }

                AssertTrue(added.Count > 0, "the current build ships templates the baseline did not");
                PromptTemplate? untouched = await db.PromptTemplates.ReadByNameAsync(_Seed.UntouchedPersonaTemplateName, ct).ConfigureAwait(false);
                AssertContains("## Recall Existing Memory", untouched!.Content, "untouched built-in persona template upgraded with memory recall");
            }

            Pipeline full = await GetAsync<Pipeline>(api, "/api/v1/pipelines/FullPipeline").ConfigureAwait(false);
            AssertTrue(full.Stages.Any(s => s.PersonaName == "Recorder"), "built-in FullPipeline upgraded with the Recorder stage");
            Pipeline editedPipeline = await GetAsync<Pipeline>(api, "/api/v1/pipelines/" + _Seed.EditedBuiltInPipelineName).ConfigureAwait(false);
            AssertEqual(_Seed.EditedBuiltInPipelineDescription, editedPipeline.Description, "edited built-in pipeline kept after seeding");
            Persona editedPersona = await GetAsync<Persona>(api, "/api/v1/personas/" + _Seed.EditedBuiltInPersonaName).ConfigureAwait(false);
            AssertEqual(_Seed.EditedBuiltInPersonaDescription, editedPersona.Description, "edited built-in persona kept after seeding");
            Persona recorder = await GetAsync<Persona>(api, "/api/v1/personas/Recorder").ConfigureAwait(false);
            AssertEqual("Recorder", recorder.Name, "new built-in persona seeded");

            EnumerationResult<FleetAction> actions = await SendAsync<EnumerationResult<FleetAction>>(api, HttpMethod.Post, "/api/v1/fleet-actions/enumerate", new { PageSize = 100 }).ConfigureAwait(false);
            AssertTrue(actions.Objects.Any(a => a.IsBuiltIn), "built-in fleet actions seeded for existing tenants");
        }

        private Task CleanupAsync(CancellationToken ct)
        {
            try { _Current?.Dispose(); } catch { }
            _Current = null;
            try { _Baseline?.Dispose(); } catch { }
            _Baseline = null;
            try { _Database?.Dispose(); } catch { }
            _Database = null;
            return Task.CompletedTask;
        }

        private static IEnumerable<MissionStatusEnum> PathTo(MissionStatusEnum status)
        {
            switch (status)
            {
                case MissionStatusEnum.Pending: return new MissionStatusEnum[0];
                case MissionStatusEnum.Assigned: return new[] { MissionStatusEnum.Assigned };
                case MissionStatusEnum.InProgress: return new[] { MissionStatusEnum.Assigned, MissionStatusEnum.InProgress };
                case MissionStatusEnum.WorkProduced: return new[] { MissionStatusEnum.Assigned, MissionStatusEnum.InProgress, MissionStatusEnum.WorkProduced };
                case MissionStatusEnum.PullRequestOpen: return new[] { MissionStatusEnum.Assigned, MissionStatusEnum.InProgress, MissionStatusEnum.WorkProduced, MissionStatusEnum.PullRequestOpen };
                case MissionStatusEnum.Testing: return new[] { MissionStatusEnum.Assigned, MissionStatusEnum.InProgress, MissionStatusEnum.Testing };
                case MissionStatusEnum.Review: return new[] { MissionStatusEnum.Assigned, MissionStatusEnum.InProgress, MissionStatusEnum.Review };
                case MissionStatusEnum.Complete: return new[] { MissionStatusEnum.Assigned, MissionStatusEnum.InProgress, MissionStatusEnum.Complete };
                case MissionStatusEnum.Failed: return new[] { MissionStatusEnum.Assigned, MissionStatusEnum.InProgress, MissionStatusEnum.Failed };
                case MissionStatusEnum.LandingFailed: return new[] { MissionStatusEnum.Assigned, MissionStatusEnum.InProgress, MissionStatusEnum.WorkProduced, MissionStatusEnum.LandingFailed };
                case MissionStatusEnum.Cancelled: return new[] { MissionStatusEnum.Cancelled };
                default: throw new ArgumentOutOfRangeException(nameof(status), "No seeding path for mission status " + status);
            }
        }

        private static HttpClient BearerClient(string baseUrl, string token)
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.Timeout = TimeSpan.FromSeconds(60);
            return client;
        }

        private static async Task<T> GetAsync<T>(HttpClient client, string path)
        {
            HttpResponseMessage response = await client.GetAsync(path).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new AssertionException("GET " + path + " returned " + (int)response.StatusCode + ": " + Truncate(body));
            return JsonHelper.Deserialize<T>(body);
        }

        private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string path, object body)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, path))
            {
                request.Content = JsonHelper.ToJsonContent(body);
                HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
                string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw new AssertionException(method + " " + path + " returned " + (int)response.StatusCode + ": " + Truncate(text));
                return JsonHelper.Deserialize<T>(text);
            }
        }

        private static string Truncate(string text)
        {
            return text.Length > 600 ? text.Substring(0, 600) + "..." : text;
        }

        private BaselineArmadaProcess RequireBaseline()
        {
            return _Baseline ?? throw new AssertionException("baseline is not running (an earlier case failed)");
        }

        private IsolatedTestDatabase RequireDatabase()
        {
            return _Database ?? throw new AssertionException("no upgrade database (an earlier case failed)");
        }

        private InProcessArmadaServer RequireCurrent()
        {
            return _Current ?? throw new AssertionException("current Admiral is not running (an earlier case failed)");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            bool skip = HostPath() == null;
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: body,
                tags: new List<string> { TestTags.Positive, TestTags.EndToEnd, TestTags.Database },
                skip: skip,
                skipReason: skip ? "Set " + BaselineArmadaProcess.HostPathEnvVar + " (scripts/common/run-upgrade-test.sh builds the baseline and sets it)." : null);
        }

        #endregion
    }
}
