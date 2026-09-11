namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors verifying that <see cref="PersonaSeedService"/> seeds the Recorder persona and wires it
    /// into the pipelines: FullPipeline ends with a non-gating Recorder stage, and a Recorded pipeline
    /// (Worker then Recorder) exists.
    /// </summary>
    public sealed class RecorderSeedingSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Recorder seeding suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("recorder_persona_seeded", "The built-in Recorder persona is seeded and points at persona.recorder", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await new PersonaSeedService(testDb.Driver, CreateLogging()).SeedAsync().ConfigureAwait(false);

                Persona? recorder = await testDb.Driver.Personas.ReadByNameAsync(PersonaCatalog.Recorder).ConfigureAwait(false);
                AssertNotNull(recorder, "Expected the Recorder persona to be seeded.");
                AssertTrue(recorder!.IsBuiltIn, "Expected Recorder to be a built-in persona.");
                AssertEqual("persona.recorder", recorder.PromptTemplateName);
            }));

            cases.Add(CaseAsync("full_pipeline_ends_with_nongating_recorder", "FullPipeline ends with a non-gating Recorder stage", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await new PersonaSeedService(testDb.Driver, CreateLogging()).SeedAsync().ConfigureAwait(false);

                Pipeline? full = await testDb.Driver.Pipelines.ReadByNameAsync("FullPipeline").ConfigureAwait(false);
                AssertNotNull(full, "Expected FullPipeline to be seeded.");
                PipelineStage last = full!.Stages.OrderBy(s => s.Order).Last();
                AssertTrue(PersonaCatalog.Matches(last.PersonaName, PersonaCatalog.Recorder), "Expected the last FullPipeline stage to be the Recorder.");
                AssertFalse(last.RequiresReview, "Expected the Recorder stage to be non-gating (no required review).");
            }));

            cases.Add(CaseAsync("recorded_pipeline_is_worker_then_recorder", "The Recorded pipeline is Worker then Recorder", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await new PersonaSeedService(testDb.Driver, CreateLogging()).SeedAsync().ConfigureAwait(false);

                Pipeline? recorded = await testDb.Driver.Pipelines.ReadByNameAsync("Recorded").ConfigureAwait(false);
                AssertNotNull(recorded, "Expected the Recorded pipeline to be seeded.");
                List<PipelineStage> stages = recorded!.Stages.OrderBy(s => s.Order).ToList();
                AssertEqual(2, stages.Count);
                AssertTrue(PersonaCatalog.Matches(stages[0].PersonaName, PersonaCatalog.Worker), "Expected the first stage to be the Worker.");
                AssertTrue(PersonaCatalog.Matches(stages[1].PersonaName, PersonaCatalog.Recorder), "Expected the second stage to be the Recorder.");
            }));

            cases.Add(CaseAsync("working_personas_get_memory_recall_note", "Seeded working personas carry the memory-recall note; the Recorder does not", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await new PromptTemplateService(testDb.Driver, CreateLogging()).SeedDefaultsAsync().ConfigureAwait(false);

                PromptTemplate? worker = await testDb.Driver.PromptTemplates.ReadByNameAsync("persona.worker").ConfigureAwait(false);
                AssertNotNull(worker, "Expected persona.worker to be seeded.");
                AssertContains("## Recall Existing Memory", worker!.Content);
                AssertContains("search_memory", worker.Content);

                PromptTemplate? recorder = await testDb.Driver.PromptTemplates.ReadByNameAsync("persona.recorder").ConfigureAwait(false);
                AssertNotNull(recorder, "Expected persona.recorder to be seeded.");
                AssertFalse(recorder!.Content.Contains("## Recall Existing Memory", StringComparison.Ordinal), "The Recorder template should not get the recall note.");
            }));

            cases.Add(CaseAsync("recall_note_upgrades_existing_template_idempotently", "The recall note is appended to a pre-existing built-in persona template exactly once", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                // Simulate a deployment created before the recall guidance existed: a built-in persona template
                // already present, without the note.
                PromptTemplate legacy = new PromptTemplate("persona.worker", "You are a worker. Do the work.")
                {
                    Category = "persona",
                    IsBuiltIn = true
                };
                await testDb.Driver.PromptTemplates.CreateAsync(legacy).ConfigureAwait(false);

                PromptTemplateService service = new PromptTemplateService(testDb.Driver, CreateLogging());
                await service.SeedDefaultsAsync().ConfigureAwait(false);
                await service.SeedDefaultsAsync().ConfigureAwait(false);

                PromptTemplate? worker = await testDb.Driver.PromptTemplates.ReadByNameAsync("persona.worker").ConfigureAwait(false);
                AssertNotNull(worker, "Expected persona.worker to exist.");
                int occurrences = worker!.Content.Split(new[] { "## Recall Existing Memory" }, StringSplitOptions.None).Length - 1;
                AssertEqual(1, occurrences);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.RecorderSeeding",
                displayName: "Recorder Seeding",
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
                suiteId: "Services.RecorderSeeding",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
