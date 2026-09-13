namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for built-in persona and pipeline seeding via <see cref="PersonaSeedService"/>:
    /// creating new built-in personas and the expanded FullPipeline, upgrading the legacy built-in
    /// FullPipeline order in place, and renaming the legacy built-in TestEngineer persona to its
    /// canonical name. Each case builds a fresh SQLite store.
    /// </summary>
    public sealed class PersonaSeedServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.PersonaSeedService";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Persona Seed Service suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("seed_async_creates_new_built_in_personas_and_expanded_full_pipeline", "SeedAsync creates new built-in personas and expanded FullPipeline", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    PersonaSeedService service = new PersonaSeedService(testDb.Driver, logging);

                    await service.SeedAsync().ConfigureAwait(false);

                    Persona? productManager = await testDb.Driver.Personas.ReadByNameAsync("Product Manager").ConfigureAwait(false);
                    Persona? usabilityEngineer = await testDb.Driver.Personas.ReadByNameAsync("Usability Engineer").ConfigureAwait(false);
                    AssertNotNull(productManager, "Product Manager persona should be seeded");
                    AssertNotNull(usabilityEngineer, "Usability Engineer persona should be seeded");
                    AssertEqual("persona.product_manager", productManager!.PromptTemplateName, "Product Manager prompt template");
                    AssertEqual("persona.usability_engineer", usabilityEngineer!.PromptTemplateName, "Usability Engineer prompt template");
                    Persona? testEngineer = await testDb.Driver.Personas.ReadByNameAsync(PersonaCatalog.TestEngineer).ConfigureAwait(false);
                    AssertNotNull(testEngineer, "Test Engineer persona should be seeded");
                    Persona? linter = await testDb.Driver.Personas.ReadByNameAsync(PersonaCatalog.Linter).ConfigureAwait(false);
                    AssertNotNull(linter, "Linter persona should be seeded");
                    AssertTrue(linter!.IsBuiltIn, "Linter should be a built-in persona");
                    AssertEqual("persona.linter", linter.PromptTemplateName, "Linter prompt template");

                    Pipeline? fullPipeline = await testDb.Driver.Pipelines.ReadByNameAsync("FullPipeline").ConfigureAwait(false);
                    AssertNotNull(fullPipeline, "FullPipeline should be seeded");
                    string seededOrder = String.Join(" | ", fullPipeline!.Stages.OrderBy(s => s.Order).Select(s => s.PersonaName));
                    AssertEqual(
                        "Product Manager | Architect | Worker | Usability Engineer | Test Engineer | Linter | Judge | Recorder",
                        seededOrder,
                        "FullPipeline persona order");
                }
            }));

            cases.Add(CaseAsync("seed_async_upgrades_the_legacy_built_in_full_pipeline_order", "SeedAsync upgrades the legacy built-in FullPipeline order", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();

                    Pipeline legacy = new Pipeline("FullPipeline");
                    legacy.TenantId = Armada.Core.Constants.DefaultTenantId;
                    legacy.Description = "Architect then Worker then TestEngineer then Judge.";
                    legacy.IsBuiltIn = true;
                    legacy.Stages = new List<PipelineStage>
                    {
                        new PipelineStage(1, "Architect") { PipelineId = legacy.Id, RequiresReview = true },
                        new PipelineStage(2, "Worker") { PipelineId = legacy.Id, RequiresReview = true },
                        new PipelineStage(3, PersonaCatalog.LegacyTestEngineer) { PipelineId = legacy.Id, RequiresReview = true },
                        new PipelineStage(4, "Judge") { PipelineId = legacy.Id, RequiresReview = true, ReviewDenyAction = ReviewDenyActionEnum.FailPipeline }
                    };

                    await testDb.Driver.Pipelines.CreateAsync(legacy).ConfigureAwait(false);

                    PersonaSeedService service = new PersonaSeedService(testDb.Driver, logging);
                    await service.SeedAsync().ConfigureAwait(false);

                    Pipeline? upgraded = await testDb.Driver.Pipelines.ReadByNameAsync("FullPipeline").ConfigureAwait(false);
                    AssertNotNull(upgraded, "FullPipeline should still exist");
                    string upgradedOrder = String.Join(" | ", upgraded!.Stages.OrderBy(s => s.Order).Select(s => s.PersonaName));
                    AssertEqual(
                        "Product Manager | Architect | Worker | Usability Engineer | Test Engineer | Linter | Judge | Recorder",
                        upgradedOrder,
                        "Legacy FullPipeline should be upgraded in place");
                }
            }));

            cases.Add(CaseAsync("seed_async_renames_the_legacy_built_in_test_engineer_persona", "SeedAsync renames the legacy built-in TestEngineer persona", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();

                    Persona legacy = new Persona(PersonaCatalog.LegacyTestEngineer, "persona.test_engineer");
                    legacy.TenantId = Armada.Core.Constants.DefaultTenantId;
                    legacy.Description = "Writes and updates tests for mission changes.";
                    legacy.IsBuiltIn = true;
                    await testDb.Driver.Personas.CreateAsync(legacy).ConfigureAwait(false);

                    PersonaSeedService service = new PersonaSeedService(testDb.Driver, logging);
                    await service.SeedAsync().ConfigureAwait(false);

                    Persona? canonical = await testDb.Driver.Personas.ReadByNameAsync(PersonaCatalog.TestEngineer).ConfigureAwait(false);
                    Persona? legacyRecord = await testDb.Driver.Personas.ReadByNameAsync(PersonaCatalog.LegacyTestEngineer).ConfigureAwait(false);
                    AssertNotNull(canonical, "Canonical Test Engineer persona should exist");
                    AssertNull(legacyRecord, "Legacy TestEngineer persona should be renamed away");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Persona Seed Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
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

        #endregion
    }
}
