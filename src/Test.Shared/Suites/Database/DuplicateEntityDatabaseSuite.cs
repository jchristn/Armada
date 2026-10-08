namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Server.Routes;
    using Armada.Server.WebSocket;
    using Microsoft.Data.Sqlite;
    using Npgsql;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;
    using static Test.Shared.Infrastructure.DuplicateEntityAsserts;

    /// <summary>
    /// Duplicate values that must be unique, on every provider (run through scripts/common/run-db-parity-tests.sh):
    /// the user-facing checks in <see cref="DuplicateEntityGuard"/> refuse a taken name, email, or file name with a
    /// typed <see cref="DuplicateEntityException"/> naming the field; and the database layer translates a provider
    /// unique-constraint violation that bypasses those checks (a direct insert, a concurrent insert, a reused id) into
    /// the same typed exception, recognized by error code (SQLite 19/2067 and 19/1555, PostgreSQL 23505, MySQL 1062,
    /// SQL Server 2627/2601), so provider text never reaches a client. Before this, every one of these surfaced as
    /// the raw provider exception (for example "SQLite Error 19: UNIQUE constraint failed: captains.name").
    /// </summary>
    public sealed class DuplicateEntityDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.DuplicateEntities";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("classify_by_error_code", "Provider unique violations are recognized by error code, other constraint errors are not", TestTags.Positive, () =>
            {
                AssertTrue(UniqueConstraintViolation.IsUniqueViolation(new SqliteException("x", 19, 2067)), "SQLite UNIQUE (19/2067)");
                AssertTrue(UniqueConstraintViolation.IsUniqueViolation(new SqliteException("x", 19, 1555)), "SQLite PRIMARY KEY (19/1555)");
                AssertFalse(UniqueConstraintViolation.IsUniqueViolation(new SqliteException("UNIQUE constraint failed", 19, 1299)), "SQLite NOT NULL (19/1299) is not a duplicate, whatever its text says");
                AssertFalse(UniqueConstraintViolation.IsUniqueViolation(new SqliteException("x", 19, 787)), "SQLite FOREIGN KEY (19/787)");
                AssertFalse(UniqueConstraintViolation.IsUniqueViolation(new SqliteException("x", 5, 5)), "SQLite BUSY");
                AssertTrue(UniqueConstraintViolation.IsUniqueViolation(new PostgresException("x", "ERROR", "ERROR", "23505")), "PostgreSQL 23505");
                AssertFalse(UniqueConstraintViolation.IsUniqueViolation(new PostgresException("duplicate key", "ERROR", "ERROR", "23503")), "PostgreSQL 23503 (foreign key)");
                AssertFalse(UniqueConstraintViolation.IsUniqueViolation(new InvalidOperationException("UNIQUE constraint failed: captains.name")), "message text is never matched");

                DuplicateEntityException? wrapped = UniqueConstraintViolation.Translate(new InvalidOperationException("outer", new SqliteException("SQLite Error 19: UNIQUE constraint failed: captains.name", 19, 2067)), "Captain");
                AssertNotNull(wrapped, "a wrapped violation is found");
                AssertEqual("Captain", wrapped!.EntityType, "entity type");
                AssertEqual("A captain with the same name or ID already exists.", wrapped.Message, "Armada's own message");
                AssertNull(UniqueConstraintViolation.Translate(new InvalidOperationException("x")), "anything else is not translated");
                AssertEqual("VesselHealthOverride", UniqueConstraintViolation.EntityTypeFor(typeof(IVesselHealthOverrideMethods)), "entity type from interface");
                AssertEqual("A vessel health override with the same unique key already exists.", UniqueConstraintViolation.MessageFor("VesselHealthOverride"), "generic message");
            }));

            cases.Add(CaseAsync("raw_provider_error_maps_everywhere", "A real provider violation is classified, and REST, MCP, and WebSocket mapping return 409/Conflict without provider text", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                AssertTrue(db.Personas is DuplicateEntityTranslationProxy, "entity accessors are wrapped");
                IPersonaMethods raw = (IPersonaMethods)((DuplicateEntityTranslationProxy)(object)db.Personas).GetTarget();

                string name = "dup-raw-" + Suffix();
                await raw.CreateAsync(NewPersona(name)).ConfigureAwait(false);
                Exception? providerError = null;
                try { await raw.CreateAsync(NewPersona(name)).ConfigureAwait(false); }
                catch (Exception ex) { providerError = ex; }
                AssertNotNull(providerError, "the unwrapped provider rejects the duplicate");
                AssertFalse(providerError is DuplicateEntityException, "the unwrapped provider throws its own exception");
                AssertTrue(UniqueConstraintViolation.IsUniqueViolation(providerError), "classified by code: " + providerError!.GetType().Name);

                AssertEqual(409, RouteErrorMapper.StatusCodeFor(providerError), "REST status");
                AssertTrue(RouteErrorMapper.IsMapped(providerError), "REST maps it");
                McpToolError mcp = McpToolError.FromException(providerError);
                AssertEqual(McpToolErrorCodeEnum.Conflict, mcp.ErrorCode, "MCP category");
                AssertEqual(DuplicateEntityException.ErrorCode, mcp.Code, "MCP code");
                AssertNoProviderText(mcp.Error, "MCP message");
                WebSocketCommandError ws = WebSocketCommandError.FromException("create_persona", providerError);
                AssertEqual(WebSocketCommandErrorCodeEnum.Conflict, ws.Code, "WebSocket code");
                AssertNoProviderText(ws.Error, "WebSocket message");
            }));

            cases.Add(CaseAsync("forced_violations_are_translated", "Inserts that bypass the checks surface each unique index as a typed DuplicateEntityException", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string s = Suffix();

                string personaName = "dup-persona-" + s;
                await db.Personas.CreateAsync(NewPersona(personaName)).ConfigureAwait(false);
                AssertEqual("Persona", (await ExpectDuplicateAsync(() => db.Personas.CreateAsync(NewPersona(personaName)), "persona name").ConfigureAwait(false)).EntityType, "persona");

                string pipelineName = "dup-pipeline-" + s;
                await db.Pipelines.CreateAsync(NewPipeline(pipelineName)).ConfigureAwait(false);
                AssertEqual("Pipeline", (await ExpectDuplicateAsync(() => db.Pipelines.CreateAsync(NewPipeline(pipelineName)), "pipeline name").ConfigureAwait(false)).EntityType, "pipeline");

                string templateName = "dup.template." + s;
                await db.PromptTemplates.CreateAsync(NewTemplate(templateName)).ConfigureAwait(false);
                AssertEqual("PromptTemplate", (await ExpectDuplicateAsync(() => db.PromptTemplates.CreateAsync(NewTemplate(templateName)), "template name").ConfigureAwait(false)).EntityType, "template");

                string fileName = "dup-" + s + ".md";
                await db.Playbooks.CreateAsync(NewPlaybook(fileName)).ConfigureAwait(false);
                AssertEqual("Playbook", (await ExpectDuplicateAsync(() => db.Playbooks.CreateAsync(NewPlaybook(fileName)), "playbook file name").ConfigureAwait(false)).EntityType, "playbook");

                string email = "dup-" + s + "@example.com";
                UserMaster user = await db.Users.CreateAsync(new UserMaster(Constants.DefaultTenantId, email, "password-" + s)).ConfigureAwait(false);
                AssertEqual("User", (await ExpectDuplicateAsync(() => db.Users.CreateAsync(new UserMaster(Constants.DefaultTenantId, email, "other-" + s)), "user email").ConfigureAwait(false)).EntityType, "user");

                Credential credential = await db.Credentials.CreateAsync(new Credential(Constants.DefaultTenantId, user.Id)).ConfigureAwait(false);
                Credential sameToken = new Credential(Constants.DefaultTenantId, user.Id);
                sameToken.BearerToken = credential.BearerToken;
                AssertEqual("Credential", (await ExpectDuplicateAsync(() => db.Credentials.CreateAsync(sameToken), "bearer token").ConfigureAwait(false)).EntityType, "credential");

                PushDevice device = await db.PushDevices.CreateAsync(NewDevice(user.Id)).ConfigureAwait(false);
                PushDevice sameDevice = NewDevice(user.Id);
                sameDevice.ExpoPushToken = device.ExpoPushToken;
                AssertEqual("PushDevice", (await ExpectDuplicateAsync(() => db.PushDevices.CreateAsync(sameDevice), "push token").ConfigureAwait(false)).EntityType, "push device");

                // Primary keys: a reused id is a duplicate on every provider.
                Fleet fleet = await db.Fleets.CreateAsync(NewFleet("dup-fleet-" + s)).ConfigureAwait(false);
                Fleet fleetSameId = NewFleet("dup-fleet-other-" + s);
                fleetSameId.Id = fleet.Id;
                DuplicateEntityException fleetDuplicate = await ExpectDuplicateAsync(() => db.Fleets.CreateAsync(fleetSameId), "fleet id").ConfigureAwait(false);
                AssertEqual("Fleet", fleetDuplicate.EntityType, "fleet");
                AssertNull(fleetDuplicate.Field, "the database does not say which field");
                AssertTrue(UniqueConstraintViolation.IsUniqueViolation(fleetDuplicate.InnerException), "the provider exception is kept for logs");

                Captain captain = await db.Captains.CreateAsync(NewCaptain("dup-captain-" + s)).ConfigureAwait(false);
                Captain captainSameId = NewCaptain("dup-captain-other-" + s);
                captainSameId.Id = captain.Id;
                AssertEqual("Captain", (await ExpectDuplicateAsync(() => db.Captains.CreateAsync(captainSameId), "captain id").ConfigureAwait(false)).EntityType, "captain");

                Vessel vessel = await db.Vessels.CreateAsync(NewVessel("dup-vessel-" + s)).ConfigureAwait(false);
                Vessel vesselSameId = NewVessel("dup-vessel-other-" + s);
                vesselSameId.Id = vessel.Id;
                AssertEqual("Vessel", (await ExpectDuplicateAsync(() => db.Vessels.CreateAsync(vesselSameId), "vessel id").ConfigureAwait(false)).EntityType, "vessel");

                // SQLite and MySQL also index fleet, vessel, and captain names (across tenants); PostgreSQL and SQL
                // Server do not, so there only the checks in DuplicateEntityGuard apply.
                if (TestDatabaseConfig.Type == DatabaseTypeEnum.Sqlite || TestDatabaseConfig.Type == DatabaseTypeEnum.Mysql)
                {
                    AssertEqual("Captain", (await ExpectDuplicateAsync(() => db.Captains.CreateAsync(NewCaptain(captain.Name)), "captain name").ConfigureAwait(false)).EntityType, "captain name");
                    AssertEqual("Fleet", (await ExpectDuplicateAsync(() => db.Fleets.CreateAsync(NewFleet(fleet.Name)), "fleet name").ConfigureAwait(false)).EntityType, "fleet name");
                    AssertEqual("Vessel", (await ExpectDuplicateAsync(() => db.Vessels.CreateAsync(NewVessel(vessel.Name)), "vessel name").ConfigureAwait(false)).EntityType, "vessel name");

                    Captain renamed = NewCaptain("dup-captain-rename-" + s);
                    renamed = await db.Captains.CreateAsync(renamed).ConfigureAwait(false);
                    renamed.Name = captain.Name;
                    AssertEqual("Captain", (await ExpectDuplicateAsync(() => db.Captains.UpdateAsync(renamed), "captain rename").ConfigureAwait(false)).EntityType, "captain update");
                }
            }));

            cases.Add(CaseAsync("concurrent_inserts_one_wins", "Concurrent inserts of one name: exactly one succeeds, the rest fail with DuplicateEntityException", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string name = "dup-race-" + Suffix();
                const int contenders = 6;

                List<Task<Exception?>> attempts = new List<Task<Exception?>>();
                for (int i = 0; i < contenders; i++)
                {
                    attempts.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await db.Personas.CreateAsync(NewPersona(name)).ConfigureAwait(false);
                            return (Exception?)null;
                        }
                        catch (Exception ex)
                        {
                            return ex;
                        }
                    }));
                }

                Exception?[] outcomes = await Task.WhenAll(attempts).ConfigureAwait(false);
                AssertEqual(1, outcomes.Count(o => o == null), "exactly one insert wins");
                foreach (Exception? outcome in outcomes.Where(o => o != null))
                {
                    AssertTrue(outcome is DuplicateEntityException, "a losing insert is typed: " + outcome!.GetType().FullName + ": " + outcome.Message);
                    AssertNoProviderText(outcome.Message, "losing insert message");
                }
            }));

            cases.Add(CaseAsync("guard_names_the_field", "The user-facing checks refuse a taken name/email/file name with a message naming the field", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string s = Suffix();

                Fleet fleet = await db.Fleets.CreateAsync(NewFleet("guard-fleet-" + s)).ConfigureAwait(false);
                DuplicateEntityException fleetDup = await ExpectDuplicateAsync(() => DuplicateEntityGuard.EnsureFleetNameAvailableAsync(db, NewFleet(fleet.Name)), "fleet").ConfigureAwait(false);
                AssertEqual("Fleet", fleetDup.EntityType, "fleet type");
                AssertEqual("Name", fleetDup.Field, "fleet field");
                AssertEqual(fleet.Name, fleetDup.Value, "fleet value");
                AssertEqual("A fleet named '" + fleet.Name + "' already exists.", fleetDup.Message, "fleet message");

                Vessel vessel = await db.Vessels.CreateAsync(NewVessel("guard-vessel-" + s)).ConfigureAwait(false);
                AssertEqual("A vessel named '" + vessel.Name + "' already exists.",
                    (await ExpectDuplicateAsync(() => DuplicateEntityGuard.EnsureVesselNameAvailableAsync(db, NewVessel(vessel.Name)), "vessel").ConfigureAwait(false)).Message, "vessel message");

                Captain captain = await db.Captains.CreateAsync(NewCaptain("guard-captain-" + s)).ConfigureAwait(false);
                AssertEqual("A captain named '" + captain.Name + "' already exists.",
                    (await ExpectDuplicateAsync(() => DuplicateEntityGuard.EnsureCaptainNameAvailableAsync(db, NewCaptain(captain.Name)), "captain").ConfigureAwait(false)).Message, "captain message");

                Persona persona = await db.Personas.CreateAsync(NewPersona("guard-persona-" + s)).ConfigureAwait(false);
                AssertEqual("A persona named '" + persona.Name + "' already exists.",
                    (await ExpectDuplicateAsync(() => DuplicateEntityGuard.EnsurePersonaNameAvailableAsync(db, NewPersona(persona.Name)), "persona").ConfigureAwait(false)).Message, "persona message");

                Pipeline pipeline = await db.Pipelines.CreateAsync(NewPipeline("guard-pipeline-" + s)).ConfigureAwait(false);
                AssertEqual("A pipeline named '" + pipeline.Name + "' already exists.",
                    (await ExpectDuplicateAsync(() => DuplicateEntityGuard.EnsurePipelineNameAvailableAsync(db, NewPipeline(pipeline.Name)), "pipeline").ConfigureAwait(false)).Message, "pipeline message");

                PromptTemplate template = await db.PromptTemplates.CreateAsync(NewTemplate("guard.template." + s)).ConfigureAwait(false);
                AssertEqual("A prompt template named '" + template.Name + "' already exists.",
                    (await ExpectDuplicateAsync(() => DuplicateEntityGuard.EnsurePromptTemplateNameAvailableAsync(db, NewTemplate(template.Name)), "template").ConfigureAwait(false)).Message, "template message");

                Playbook playbook = await db.Playbooks.CreateAsync(NewPlaybook("guard-" + s + ".md")).ConfigureAwait(false);
                DuplicateEntityException playbookDup = await ExpectDuplicateAsync(() => DuplicateEntityGuard.EnsurePlaybookFileNameAvailableAsync(db, NewPlaybook(playbook.FileName)), "playbook").ConfigureAwait(false);
                AssertEqual("FileName", playbookDup.Field, "playbook field");
                AssertEqual("A playbook with file name '" + playbook.FileName + "' already exists.", playbookDup.Message, "playbook message");

                UserMaster user = await db.Users.CreateAsync(new UserMaster(Constants.DefaultTenantId, "guard-" + s + "@example.com", "password-" + s)).ConfigureAwait(false);
                DuplicateEntityException userDup = await ExpectDuplicateAsync(() => DuplicateEntityGuard.EnsureUserEmailAvailableAsync(db, new UserMaster(Constants.DefaultTenantId, user.Email, "x-" + s)), "user").ConfigureAwait(false);
                AssertEqual("Email", userDup.Field, "user field");
                AssertEqual("A user with email '" + user.Email + "' already exists in this tenant.", userDup.Message, "user message");
            }));

            cases.Add(CaseAsync("guard_allows_own_value_and_other_tenants", "Keeping an entity's own value, a free value, or the same name in another tenant passes the checks", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string s = Suffix();
                TenantMetadata other = await db.Tenants.CreateAsync(new TenantMetadata("guard-other-" + s)).ConfigureAwait(false);

                Fleet fleet = await db.Fleets.CreateAsync(NewFleet("own-fleet-" + s)).ConfigureAwait(false);
                await DuplicateEntityGuard.EnsureFleetNameAvailableAsync(db, fleet).ConfigureAwait(false);
                await DuplicateEntityGuard.EnsureFleetNameAvailableAsync(db, NewFleet("free-fleet-" + s)).ConfigureAwait(false);
                Fleet otherFleet = NewFleet(fleet.Name);
                otherFleet.TenantId = other.Id;
                await DuplicateEntityGuard.EnsureFleetNameAvailableAsync(db, otherFleet).ConfigureAwait(false);

                Captain captain = await db.Captains.CreateAsync(NewCaptain("own-captain-" + s)).ConfigureAwait(false);
                await DuplicateEntityGuard.EnsureCaptainNameAvailableAsync(db, captain).ConfigureAwait(false);
                Captain otherCaptain = NewCaptain(captain.Name);
                otherCaptain.TenantId = other.Id;
                await DuplicateEntityGuard.EnsureCaptainNameAvailableAsync(db, otherCaptain).ConfigureAwait(false);

                Vessel vessel = await db.Vessels.CreateAsync(NewVessel("own-vessel-" + s)).ConfigureAwait(false);
                await DuplicateEntityGuard.EnsureVesselNameAvailableAsync(db, vessel).ConfigureAwait(false);

                Persona persona = await db.Personas.CreateAsync(NewPersona("own-persona-" + s)).ConfigureAwait(false);
                await DuplicateEntityGuard.EnsurePersonaNameAvailableAsync(db, persona).ConfigureAwait(false);
                Persona otherPersona = NewPersona(persona.Name);
                otherPersona.TenantId = other.Id;
                await DuplicateEntityGuard.EnsurePersonaNameAvailableAsync(db, otherPersona).ConfigureAwait(false);
                await db.Personas.CreateAsync(otherPersona).ConfigureAwait(false);

                Playbook playbook = await db.Playbooks.CreateAsync(NewPlaybook("own-" + s + ".md")).ConfigureAwait(false);
                await DuplicateEntityGuard.EnsurePlaybookFileNameAvailableAsync(db, playbook).ConfigureAwait(false);

                UserMaster user = await db.Users.CreateAsync(new UserMaster(Constants.DefaultTenantId, "own-" + s + "@example.com", "password-" + s)).ConfigureAwait(false);
                await DuplicateEntityGuard.EnsureUserEmailAvailableAsync(db, user).ConfigureAwait(false);
                await DuplicateEntityGuard.EnsureUserEmailAvailableAsync(db, new UserMaster(other.Id, user.Email, "x-" + s)).ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Duplicate entities (checks and provider unique-constraint translation)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string Suffix()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 10);
        }

        private static Fleet NewFleet(string name)
        {
            Fleet fleet = new Fleet(name);
            fleet.TenantId = Constants.DefaultTenantId;
            return fleet;
        }

        private static Vessel NewVessel(string name)
        {
            Vessel vessel = new Vessel(name, "https://example.com/" + name + ".git");
            vessel.TenantId = Constants.DefaultTenantId;
            return vessel;
        }

        private static Captain NewCaptain(string name)
        {
            Captain captain = new Captain(name);
            captain.TenantId = Constants.DefaultTenantId;
            return captain;
        }

        private static Persona NewPersona(string name)
        {
            Persona persona = new Persona(name, "persona.worker");
            persona.TenantId = Constants.DefaultTenantId;
            return persona;
        }

        private static Pipeline NewPipeline(string name)
        {
            Pipeline pipeline = new Pipeline(name);
            pipeline.TenantId = Constants.DefaultTenantId;
            pipeline.Stages = new List<PipelineStage> { new PipelineStage(1, "Worker") };
            return pipeline;
        }

        private static PromptTemplate NewTemplate(string name)
        {
            PromptTemplate template = new PromptTemplate(name, "content");
            template.TenantId = Constants.DefaultTenantId;
            return template;
        }

        private static Playbook NewPlaybook(string fileName)
        {
            Playbook playbook = new Playbook(fileName, "# content");
            playbook.TenantId = Constants.DefaultTenantId;
            return playbook;
        }

        private static PushDevice NewDevice(string userId)
        {
            PushDevice device = new PushDevice();
            device.TenantId = Constants.DefaultTenantId;
            device.UserId = userId;
            device.ExpoPushToken = "ExponentPushToken[" + Guid.NewGuid().ToString("N") + "]";
            device.Categories = new List<PushCategoryEnum> { PushCategoryEnum.MissionFailed };
            return device;
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

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
