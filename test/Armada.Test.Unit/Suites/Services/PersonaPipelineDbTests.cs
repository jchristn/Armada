namespace Armada.Test.Unit.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    /// <summary>
    /// Tests for persona and pipeline database CRUD operations including stage management.
    /// </summary>
    public class PersonaPipelineDbTests : TestSuite
    {
        /// <summary>
        /// Suite name.
        /// </summary>
        public override string Name => "Persona and Pipeline Database Operations";

        /// <summary>
        /// Run all tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Create and read persona", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Persona persona = new Persona("TestWorker", "persona.worker");
                    persona.Description = "A test worker persona";
                    persona = await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);

                    Persona? read = await testDb.Driver.Personas.ReadAsync(persona.Id).ConfigureAwait(false);
                    AssertNotNull(read, "Persona should be readable by ID");
                    AssertEqual("TestWorker", read!.Name, "Persona name");
                    AssertEqual("persona.worker", read.PromptTemplateName, "Persona prompt template name");
                    AssertEqual("A test worker persona", read.Description, "Persona description");
                }
            });

            await RunTest("Read persona by name", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Persona persona = new Persona("UniquePersona", "persona.worker");
                    persona.Description = "Unique persona for name lookup";
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);

                    Persona? read = await testDb.Driver.Personas.ReadByNameAsync("UniquePersona").ConfigureAwait(false);
                    AssertNotNull(read, "Persona should be readable by name");
                    AssertEqual("UniquePersona", read!.Name, "Persona name");
                }
            });

            await RunTest("Update persona", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Persona persona = new Persona("UpdateTarget", "persona.worker");
                    persona.Description = "Original description";
                    persona = await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);

                    persona.Description = "Updated description";
                    await testDb.Driver.Personas.UpdateAsync(persona).ConfigureAwait(false);

                    Persona? read = await testDb.Driver.Personas.ReadAsync(persona.Id).ConfigureAwait(false);
                    AssertNotNull(read, "Updated persona should be readable");
                    AssertEqual("Updated description", read!.Description, "Persona description after update");
                }
            });

            await RunTest("Delete persona", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Persona persona = new Persona("DeleteTarget", "persona.worker");
                    persona = await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                    string personaId = persona.Id;

                    await testDb.Driver.Personas.DeleteAsync(personaId).ConfigureAwait(false);

                    Persona? read = await testDb.Driver.Personas.ReadAsync(personaId).ConfigureAwait(false);
                    AssertNull(read, "Deleted persona should not be found");
                }
            });

            await RunTest("ExistsByName returns true for existing", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Persona persona = new Persona("ExistsCheck", "persona.worker");
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);

                    bool exists = await testDb.Driver.Personas.ExistsByNameAsync("ExistsCheck").ConfigureAwait(false);
                    AssertTrue(exists, "ExistsByName should return true for existing persona");

                    bool notExists = await testDb.Driver.Personas.ExistsByNameAsync("Nonexistent").ConfigureAwait(false);
                    AssertFalse(notExists, "ExistsByName should return false for nonexistent persona");
                }
            });

            await RunTest("Create and read pipeline with stages", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Pipeline pipeline = new Pipeline("ThreeStage");
                    pipeline.Description = "A pipeline with three stages";
                    pipeline.Stages = new List<PipelineStage>
                    {
                        new PipelineStage(1, "Architect"),
                        new PipelineStage(2, "Worker"),
                        new PipelineStage(3, "Judge")
                    };
                    pipeline = await testDb.Driver.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);

                    Pipeline? read = await testDb.Driver.Pipelines.ReadAsync(pipeline.Id).ConfigureAwait(false);
                    AssertNotNull(read, "Pipeline should be readable by ID");
                    AssertEqual("ThreeStage", read!.Name, "Pipeline name");
                    AssertEqual(3, read.Stages.Count, "Pipeline should have 3 stages");

                    // Verify stage ordering
                    List<PipelineStage> ordered = read.Stages.OrderBy(s => s.Order).ToList();
                    AssertEqual("Architect", ordered[0].PersonaName, "Stage 1 persona");
                    AssertEqual("Worker", ordered[1].PersonaName, "Stage 2 persona");
                    AssertEqual("Judge", ordered[2].PersonaName, "Stage 3 persona");
                }
            });

            await RunTest("Update pipeline replaces stages", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Pipeline pipeline = new Pipeline("TwoStage");
                    pipeline.Stages = new List<PipelineStage>
                    {
                        new PipelineStage(1, "Architect"),
                        new PipelineStage(2, "Worker")
                    };
                    pipeline = await testDb.Driver.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);

                    // Update with 3 stages
                    pipeline.Stages = new List<PipelineStage>
                    {
                        new PipelineStage(1, "Architect"),
                        new PipelineStage(2, "Worker"),
                        new PipelineStage(3, "Judge")
                    };
                    await testDb.Driver.Pipelines.UpdateAsync(pipeline).ConfigureAwait(false);

                    Pipeline? read = await testDb.Driver.Pipelines.ReadAsync(pipeline.Id).ConfigureAwait(false);
                    AssertNotNull(read, "Updated pipeline should be readable");
                    AssertEqual(3, read!.Stages.Count, "Pipeline should now have 3 stages");
                }
            });

            await RunTest("Delete pipeline cascades to stages", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Pipeline pipeline = new Pipeline("DeleteCascade");
                    pipeline.Stages = new List<PipelineStage>
                    {
                        new PipelineStage(1, "Worker"),
                        new PipelineStage(2, "Judge")
                    };
                    pipeline = await testDb.Driver.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);
                    string pipelineId = pipeline.Id;

                    await testDb.Driver.Pipelines.DeleteAsync(pipelineId).ConfigureAwait(false);

                    Pipeline? read = await testDb.Driver.Pipelines.ReadAsync(pipelineId).ConfigureAwait(false);
                    AssertNull(read, "Deleted pipeline should not be found");
                }
            });

            await RunTest("Pipeline ReadByName includes stages", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Pipeline pipeline = new Pipeline("NameLookup");
                    pipeline.Stages = new List<PipelineStage>
                    {
                        new PipelineStage(1, "Architect"),
                        new PipelineStage(2, "Worker")
                    };
                    await testDb.Driver.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);

                    Pipeline? read = await testDb.Driver.Pipelines.ReadByNameAsync("NameLookup").ConfigureAwait(false);
                    AssertNotNull(read, "Pipeline should be readable by name");
                    AssertEqual("NameLookup", read!.Name, "Pipeline name");
                    AssertEqual(2, read.Stages.Count, "Pipeline read by name should include stages");
                }
            });
        }
    }
}
