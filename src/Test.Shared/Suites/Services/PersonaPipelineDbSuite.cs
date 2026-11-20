namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Microsoft.Data.Sqlite;
    using MySqlConnector;
    using Npgsql;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for persona and pipeline database CRUD operations including stage management:
    /// persona create/read/read-by-name/update/delete/exists, and pipeline create-with-stages,
    /// update-replaces-stages, delete-cascades-to-stages, and read-by-name-includes-stages.
    /// Failed create, update, and delete cases keep the stored pipeline unchanged. Each case
    /// opens the database selected by <see cref="TestDatabaseConfig"/>.
    /// </summary>
    public sealed class PersonaPipelineDbSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.PersonaPipelineDb";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Persona and Pipeline Database suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_and_read_persona", "Create and read persona", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("read_persona_by_name", "Read persona by name", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("update_persona", "Update persona", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("delete_persona", "Delete persona", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("exists_by_name_returns_true_for_existing", "ExistsByName returns true for existing", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("create_and_read_pipeline_with_stages", "Create and read pipeline with stages", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Pipeline pipeline = new Pipeline("ThreeStage");
                    pipeline.Description = "A pipeline with three stages";
                    pipeline.Stages = new List<PipelineStage>
                    {
                        new PipelineStage(1, "Architect"),
                        new PipelineStage(2, "Worker") { RequiresReview = true },
                        new PipelineStage(3, "Judge") { RequiresReview = true, ReviewDenyAction = Armada.Core.Enums.ReviewDenyActionEnum.FailPipeline }
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
                    AssertTrue(ordered[1].RequiresReview, "Stage 2 review gate should persist");
                    AssertEqual(Armada.Core.Enums.ReviewDenyActionEnum.FailPipeline, ordered[2].ReviewDenyAction, "Stage 3 deny action should persist");
                }
            }));

            cases.Add(CaseAsync("update_pipeline_replaces_stages", "Update pipeline replaces stages", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("delete_pipeline_cascades_to_stages", "Delete pipeline cascades to stages", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("pipeline_read_by_name_includes_stages", "Pipeline ReadByName includes stages", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("failed_update_keeps_previous_stages", "Failed update keeps the previous stages", TestTags.Negative, FailedUpdateKeepsPreviousStagesAsync));
            cases.Add(CaseAsync("failed_create_leaves_no_pipeline", "Failed create leaves no pipeline row", TestTags.Negative, FailedCreateLeavesNoPipelineAsync));
            cases.Add(CaseAsync("failed_delete_keeps_pipeline_and_stages", "Failed delete keeps the pipeline and its stages", TestTags.Negative, FailedDeleteKeepsPipelineAndStagesAsync));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Persona and Pipeline Database Operations",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private static async Task FailedUpdateKeepsPreviousStagesAsync()
        {
            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
            {
                PipelineStage stageA = new PipelineStage(1, "StageA");
                string stageAId = stageA.Id;
                Pipeline pipeline = new Pipeline("AtomicUpdate");
                pipeline.Description = "original description";
                pipeline.Stages = new List<PipelineStage> { stageA };
                pipeline = await testDb.Driver.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);

                Pipeline changed = (await testDb.Driver.Pipelines.ReadAsync(pipeline.Id).ConfigureAwait(false))!;
                changed.Description = "changed description";
                PipelineStage replacement = new PipelineStage(1, "Replacement");
                changed.Stages = new List<PipelineStage>
                {
                    replacement,
                    new PipelineStage(2, "Other") { Id = replacement.Id }
                };

                await AssertThrowsAsync<Exception>(
                    () => testDb.Driver.Pipelines.UpdateAsync(changed),
                    "An update whose stage insert fails must throw").ConfigureAwait(false);

                Pipeline? stored = await testDb.Driver.Pipelines.ReadAsync(pipeline.Id).ConfigureAwait(false);
                AssertNotNull(stored, "The pipeline survives a failed update");
                AssertEqual(1, stored!.Stages.Count, "stage A is still stored");
                AssertEqual(stageAId, stored.Stages[0].Id, "stage A id");
                AssertEqual("StageA", stored.Stages[0].PersonaName, "stage A persona");
                AssertEqual("original description", stored.Description, "the parent row is unchanged");
            }
        }

        private static async Task FailedCreateLeavesNoPipelineAsync()
        {
            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
            {
                Pipeline pipeline = new Pipeline("AtomicCreate");
                PipelineStage first = new PipelineStage(1, "Worker");
                pipeline.Stages = new List<PipelineStage>
                {
                    first,
                    new PipelineStage(2, "Judge") { Id = first.Id }
                };

                await AssertThrowsAsync<Exception>(
                    () => testDb.Driver.Pipelines.CreateAsync(pipeline),
                    "A create whose stage insert fails must throw").ConfigureAwait(false);

                Pipeline? stored = await testDb.Driver.Pipelines.ReadAsync(pipeline.Id).ConfigureAwait(false);
                AssertNull(stored, "A failed create leaves no pipeline row");
            }
        }

        private static async Task FailedDeleteKeepsPipelineAndStagesAsync()
        {
            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
            {
                Pipeline pipeline = new Pipeline("AtomicDelete");
                pipeline.Stages = new List<PipelineStage>
                {
                    new PipelineStage(1, "StageA"),
                    new PipelineStage(2, "StageB")
                };
                pipeline = await testDb.Driver.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);

                try
                {
                    await InstallPipelineDeleteBlockAsync(testDb.ConnectionString, pipeline.Id).ConfigureAwait(false);

                    await AssertThrowsAsync<Exception>(
                        () => testDb.Driver.Pipelines.DeleteAsync(pipeline.Id),
                        "A delete whose parent delete fails must throw").ConfigureAwait(false);

                    Pipeline? stored = await testDb.Driver.Pipelines.ReadAsync(pipeline.Id).ConfigureAwait(false);
                    AssertNotNull(stored, "The pipeline survives a failed delete");
                    AssertEqual(2, stored!.Stages.Count, "The stages survive a failed delete");
                    List<PipelineStage> ordered = stored.Stages.OrderBy(stage => stage.Order).ToList();
                    AssertEqual("StageA", ordered[0].PersonaName, "stage A survives a failed delete");
                    AssertEqual("StageB", ordered[1].PersonaName, "stage B survives a failed delete");
                }
                finally
                {
                    await DropPipelineDeleteBlockAsync(testDb.ConnectionString).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Install a trigger that rejects deletion of one pipeline row. The trigger is provider-specific
        /// and exists only for the delete case. A provider with no statement fails the case.
        /// </summary>
        private static async Task InstallPipelineDeleteBlockAsync(string connectionString, string pipelineId)
        {
            string id = SqlLiteral(pipelineId);
            switch (TestDatabaseConfig.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    await ExecuteAsync(connectionString, "DROP TRIGGER IF EXISTS armada_test_block_pipeline_delete;").ConfigureAwait(false);
                    await ExecuteAsync(
                        connectionString,
                        "CREATE TRIGGER armada_test_block_pipeline_delete BEFORE DELETE ON pipelines WHEN OLD.id = " + id
                        + " BEGIN SELECT RAISE(ABORT, 'pipeline delete blocked'); END;").ConfigureAwait(false);
                    return;
                case DatabaseTypeEnum.Postgresql:
                    await ExecuteAsync(connectionString, "DROP TRIGGER IF EXISTS armada_test_block_pipeline_delete ON pipelines;").ConfigureAwait(false);
                    await ExecuteAsync(connectionString, "DROP FUNCTION IF EXISTS armada_test_block_pipeline_delete_fn();").ConfigureAwait(false);
                    await ExecuteAsync(
                        connectionString,
                        "CREATE FUNCTION armada_test_block_pipeline_delete_fn() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'pipeline delete blocked'; END $$;").ConfigureAwait(false);
                    await ExecuteAsync(
                        connectionString,
                        "CREATE TRIGGER armada_test_block_pipeline_delete BEFORE DELETE ON pipelines FOR EACH ROW WHEN (OLD.id = " + id
                        + ") EXECUTE FUNCTION armada_test_block_pipeline_delete_fn();").ConfigureAwait(false);
                    return;
                case DatabaseTypeEnum.Mysql:
                    await ExecuteAsync(connectionString, "DROP TRIGGER IF EXISTS armada_test_block_pipeline_delete;").ConfigureAwait(false);
                    await ExecuteAsync(
                        connectionString,
                        "CREATE TRIGGER armada_test_block_pipeline_delete BEFORE DELETE ON pipelines FOR EACH ROW BEGIN IF OLD.id = " + id
                        + " THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'pipeline delete blocked'; END IF; END").ConfigureAwait(false);
                    return;
                case DatabaseTypeEnum.SqlServer:
                    await ExecuteAsync(connectionString, "DROP TRIGGER IF EXISTS armada_test_block_pipeline_delete;").ConfigureAwait(false);
                    await ExecuteAsync(
                        connectionString,
                        "CREATE TRIGGER armada_test_block_pipeline_delete ON pipelines AFTER DELETE AS BEGIN IF EXISTS (SELECT 1 FROM deleted WHERE id = " + id
                        + ") THROW 50001, 'pipeline delete blocked', 1; END").ConfigureAwait(false);
                    return;
                default:
                    throw new InvalidOperationException("Pipeline delete block has no statement for " + TestDatabaseConfig.Type + ".");
            }
        }

        private static async Task DropPipelineDeleteBlockAsync(string connectionString)
        {
            switch (TestDatabaseConfig.Type)
            {
                case DatabaseTypeEnum.Postgresql:
                    await ExecuteAsync(connectionString, "DROP TRIGGER IF EXISTS armada_test_block_pipeline_delete ON pipelines;").ConfigureAwait(false);
                    await ExecuteAsync(connectionString, "DROP FUNCTION IF EXISTS armada_test_block_pipeline_delete_fn();").ConfigureAwait(false);
                    return;
                case DatabaseTypeEnum.Sqlite:
                case DatabaseTypeEnum.Mysql:
                case DatabaseTypeEnum.SqlServer:
                    await ExecuteAsync(connectionString, "DROP TRIGGER IF EXISTS armada_test_block_pipeline_delete;").ConfigureAwait(false);
                    return;
                default:
                    throw new InvalidOperationException("Pipeline delete block has no statement for " + TestDatabaseConfig.Type + ".");
            }
        }

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            switch (TestDatabaseConfig.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    {
                        using (SqliteConnection connection = new SqliteConnection(connectionString))
                        {
                            await connection.OpenAsync().ConfigureAwait(false);
                            using (SqliteCommand command = connection.CreateCommand())
                            {
                                command.CommandText = sql;
                                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                            }
                        }
                        return;
                    }
                case DatabaseTypeEnum.Postgresql:
                    {
                        using (NpgsqlConnection connection = new NpgsqlConnection(connectionString))
                        {
                            await connection.OpenAsync().ConfigureAwait(false);
                            using (NpgsqlCommand command = connection.CreateCommand())
                            {
                                command.CommandText = sql;
                                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                            }
                        }
                        return;
                    }
                case DatabaseTypeEnum.Mysql:
                    {
                        using (MySqlConnection connection = new MySqlConnection(connectionString))
                        {
                            await connection.OpenAsync().ConfigureAwait(false);
                            using (MySqlCommand command = connection.CreateCommand())
                            {
                                command.CommandText = sql;
                                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                            }
                        }
                        return;
                    }
                case DatabaseTypeEnum.SqlServer:
                    {
                        using (SqlConnection connection = new SqlConnection(connectionString))
                        {
                            await connection.OpenAsync().ConfigureAwait(false);
                            using (SqlCommand command = connection.CreateCommand())
                            {
                                command.CommandText = sql;
                                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                            }
                        }
                        return;
                    }
                default:
                    throw new InvalidOperationException("Pipeline delete block has no statement for " + TestDatabaseConfig.Type + ".");
            }
        }

        private static string SqlLiteral(string value)
        {
            if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(value));
            return "'" + value.Replace("'", "''") + "'";
        }

        #endregion
    }
}
