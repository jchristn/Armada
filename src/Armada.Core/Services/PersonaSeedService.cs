namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>
    /// Seeds built-in personas and pipelines into the database on startup.
    /// </summary>
    public class PersonaSeedService
    {
        #region Private-Members

        private string _Header = "[PersonaSeedService] ";
        private DatabaseDriver _Database;
        private LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        public PersonaSeedService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Seed all built-in personas and pipelines if they don't already exist.
        /// </summary>
        public async Task SeedAsync(CancellationToken token = default)
        {
            await SeedPersonasAsync(token).ConfigureAwait(false);
            await SeedPipelinesAsync(token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task SeedPersonasAsync(CancellationToken token)
        {
            await SeedPersonaAsync("Worker", "Standard mission executor -- writes code, makes changes, commits work.", "persona.worker", token).ConfigureAwait(false);
            await SeedPersonaAsync("Architect", "Plans voyages and decomposes work into right-sized missions.", "persona.architect", token).ConfigureAwait(false);
            await SeedPersonaAsync("Judge", "Reviews completed mission diffs for correctness and completeness.", "persona.judge", token).ConfigureAwait(false);
            await SeedPersonaAsync("TestEngineer", "Writes and updates tests for mission changes.", "persona.test_engineer", token).ConfigureAwait(false);
        }

        private async Task SeedPersonaAsync(string name, string description, string templateName, CancellationToken token)
        {
            bool exists = await _Database.Personas.ExistsByNameAsync(name, token).ConfigureAwait(false);
            if (exists) return;

            Persona persona = new Persona();
            persona.TenantId = Constants.DefaultTenantId;
            persona.Name = name;
            persona.Description = description;
            persona.PromptTemplateName = templateName;
            persona.IsBuiltIn = true;

            await _Database.Personas.CreateAsync(persona, token).ConfigureAwait(false);
            _Logging.Info(_Header + "seeded built-in persona: " + name);
        }

        private async Task SeedPipelinesAsync(CancellationToken token)
        {
            await SeedPipelineAsync(
                "WorkerOnly",
                "Single worker stage -- backward compatible default.",
                new List<PipelineStage> { new PipelineStage(1, "Worker") },
                token).ConfigureAwait(false);

            await SeedPipelineAsync(
                "Reviewed",
                "Worker then Judge review.",
                new List<PipelineStage> { new PipelineStage(1, "Worker"), new PipelineStage(2, "Judge") },
                token).ConfigureAwait(false);

            await SeedPipelineAsync(
                "Tested",
                "Worker then TestEngineer then Judge.",
                new List<PipelineStage> { new PipelineStage(1, "Worker"), new PipelineStage(2, "TestEngineer"), new PipelineStage(3, "Judge") },
                token).ConfigureAwait(false);

            await SeedPipelineAsync(
                "FullPipeline",
                "Architect then Worker then TestEngineer then Judge.",
                new List<PipelineStage> { new PipelineStage(1, "Architect"), new PipelineStage(2, "Worker"), new PipelineStage(3, "TestEngineer"), new PipelineStage(4, "Judge") },
                token).ConfigureAwait(false);
        }

        private async Task SeedPipelineAsync(string name, string description, List<PipelineStage> stages, CancellationToken token)
        {
            bool exists = await _Database.Pipelines.ExistsByNameAsync(name, token).ConfigureAwait(false);
            if (exists) return;

            Pipeline pipeline = new Pipeline();
            pipeline.TenantId = Constants.DefaultTenantId;
            pipeline.Name = name;
            pipeline.Description = description;
            pipeline.IsBuiltIn = true;
            pipeline.Stages = stages;

            foreach (PipelineStage stage in stages)
            {
                stage.PipelineId = pipeline.Id;
            }

            await _Database.Pipelines.CreateAsync(pipeline, token).ConfigureAwait(false);
            _Logging.Info(_Header + "seeded built-in pipeline: " + name);
        }

        #endregion
    }
}
