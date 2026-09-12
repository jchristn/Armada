namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
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
            await UpgradeLegacyPersonaReferencesAsync(token).ConfigureAwait(false);
            await SeedPersonasAsync(token).ConfigureAwait(false);
            await SeedPipelinesAsync(token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task UpgradeLegacyPersonaReferencesAsync(CancellationToken token)
        {
            await UpgradeLegacyBuiltInPersonaAsync(token).ConfigureAwait(false);
            await UpgradeCaptainPersonaReferencesAsync(token).ConfigureAwait(false);
        }

        private async Task SeedPersonasAsync(CancellationToken token)
        {
            await SeedPersonaAsync(PersonaCatalog.Worker, "Standard mission executor -- writes code, makes changes, commits work.", "persona.worker", token).ConfigureAwait(false);
            await SeedPersonaAsync(PersonaCatalog.Architect, "Plans voyages and decomposes work into right-sized missions.", "persona.architect", token).ConfigureAwait(false);
            await SeedPersonaAsync(PersonaCatalog.ProductManager, "Shapes the whole product picture, clarifies user outcomes, and turns dispatched work into durable requirements.", "persona.product_manager", token).ConfigureAwait(false);
            await SeedPersonaAsync(PersonaCatalog.UsabilityEngineer, "Improves usability, edge-case experience, and consistency with the surrounding product.", "persona.usability_engineer", token).ConfigureAwait(false);
            await SeedPersonaAsync(PersonaCatalog.Judge, "Reviews completed mission diffs for correctness and completeness.", "persona.judge", token).ConfigureAwait(false);
            await SeedPersonaAsync(PersonaCatalog.TestEngineer, "Writes and updates tests for mission changes.", "persona.test_engineer", token).ConfigureAwait(false);
            await SeedPersonaAsync(PersonaCatalog.Recorder, "Reviews the voyage conversation and distills durable memories (episodic/semantic/procedural) into the vessel model context, the Armada memory store, and external memory facilities.", "persona.recorder", token).ConfigureAwait(false);
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
            _Logging.Debug(_Header + "seeded built-in persona: " + name);
        }

        private async Task SeedPipelinesAsync(CancellationToken token)
        {
            await SeedPipelineAsync(
                "WorkerOnly",
                "Single worker stage -- backward compatible default.",
                new List<PipelineStage> { new PipelineStage(1, PersonaCatalog.Worker) },
                token).ConfigureAwait(false);

            await SeedPipelineAsync(
                "Reviewed",
                "Worker then Judge review.",
                new List<PipelineStage>
                {
                    new PipelineStage(1, PersonaCatalog.Worker) { RequiresReview = true },
                    new PipelineStage(2, PersonaCatalog.Judge) { RequiresReview = true, ReviewDenyAction = ReviewDenyActionEnum.FailPipeline }
                },
                token).ConfigureAwait(false);

            await SeedPipelineAsync(
                "Tested",
                "Worker then Test Engineer then Judge.",
                new List<PipelineStage>
                {
                    new PipelineStage(1, PersonaCatalog.Worker) { RequiresReview = true },
                    new PipelineStage(2, PersonaCatalog.TestEngineer) { RequiresReview = true },
                    new PipelineStage(3, PersonaCatalog.Judge) { RequiresReview = true, ReviewDenyAction = ReviewDenyActionEnum.FailPipeline }
                },
                token).ConfigureAwait(false);

            await SeedPipelineAsync(
                "FullPipeline",
                "Product Manager then Architect then Worker then Usability Engineer then Test Engineer then Judge then Recorder.",
                new List<PipelineStage>
                {
                    new PipelineStage(1, PersonaCatalog.ProductManager) { RequiresReview = true },
                    new PipelineStage(2, PersonaCatalog.Architect) { RequiresReview = true },
                    new PipelineStage(3, PersonaCatalog.Worker) { RequiresReview = true },
                    new PipelineStage(4, PersonaCatalog.UsabilityEngineer) { RequiresReview = true },
                    new PipelineStage(5, PersonaCatalog.TestEngineer) { RequiresReview = true },
                    new PipelineStage(6, PersonaCatalog.Judge) { RequiresReview = true, ReviewDenyAction = ReviewDenyActionEnum.FailPipeline },
                    new PipelineStage(7, PersonaCatalog.Recorder)
                },
                token).ConfigureAwait(false);

            await SeedPipelineAsync(
                "Recorded",
                "Worker then Recorder -- do the work, then distill durable memories from it.",
                new List<PipelineStage>
                {
                    new PipelineStage(1, PersonaCatalog.Worker) { RequiresReview = true },
                    new PipelineStage(2, PersonaCatalog.Recorder)
                },
                token).ConfigureAwait(false);
        }

        private async Task SeedPipelineAsync(string name, string description, List<PipelineStage> stages, CancellationToken token)
        {
            Pipeline? existing = await _Database.Pipelines.ReadByNameAsync(name, token).ConfigureAwait(false);
            if (existing != null)
            {
                if (ShouldUpgradeBuiltInPipeline(existing, stages))
                {
                    existing.Description = description;
                    existing.Stages = CloneStages(existing.Id, stages);
                    existing.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Pipelines.UpdateAsync(existing, token).ConfigureAwait(false);
                    _Logging.Debug(_Header + "upgraded built-in pipeline: " + name);
                }
                return;
            }

            Pipeline pipeline = new Pipeline();
            pipeline.TenantId = Constants.DefaultTenantId;
            pipeline.Name = name;
            pipeline.Description = description;
            pipeline.IsBuiltIn = true;
            pipeline.Stages = CloneStages(pipeline.Id, stages);

            await _Database.Pipelines.CreateAsync(pipeline, token).ConfigureAwait(false);
            _Logging.Debug(_Header + "seeded built-in pipeline: " + name);
        }

        private static List<PipelineStage> CloneStages(string pipelineId, IEnumerable<PipelineStage> stages)
        {
            List<PipelineStage> clones = new List<PipelineStage>();
            foreach (PipelineStage stage in stages)
            {
                clones.Add(new PipelineStage(stage.Order, stage.PersonaName)
                {
                    PipelineId = pipelineId,
                    IsOptional = stage.IsOptional,
                    Description = stage.Description,
                    RequiresReview = stage.RequiresReview,
                    ReviewDenyAction = stage.ReviewDenyAction
                });
            }

            return clones;
        }

        private async Task UpgradeLegacyBuiltInPersonaAsync(CancellationToken token)
        {
            Persona? existingCanonical = await _Database.Personas.ReadByNameAsync(PersonaCatalog.TestEngineer, token).ConfigureAwait(false);
            Persona? legacy = await _Database.Personas.ReadByNameAsync(PersonaCatalog.LegacyTestEngineer, token).ConfigureAwait(false);
            if (legacy == null || !legacy.IsBuiltIn) return;
            if (existingCanonical != null) return;

            legacy.Name = PersonaCatalog.TestEngineer;
            legacy.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Personas.UpdateAsync(legacy, token).ConfigureAwait(false);
            _Logging.Debug(_Header + "renamed built-in persona: " + PersonaCatalog.LegacyTestEngineer + " -> " + PersonaCatalog.TestEngineer);
        }

        private async Task UpgradeCaptainPersonaReferencesAsync(CancellationToken token)
        {
            List<Captain> captains = await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false);
            foreach (Captain captain in captains)
            {
                bool changed = false;
                string? updatedAllowedPersonas = PersonaCatalog.ReplaceLegacyTestEngineer(captain.AllowedPersonas);
                if (!String.Equals(updatedAllowedPersonas, captain.AllowedPersonas, StringComparison.Ordinal))
                {
                    captain.AllowedPersonas = updatedAllowedPersonas;
                    changed = true;
                }

                string normalizedPreferredPersona = PersonaCatalog.NormalizeName(captain.PreferredPersona);
                string? updatedPreferredPersona = String.IsNullOrEmpty(normalizedPreferredPersona) ? null : normalizedPreferredPersona;
                if (!String.Equals(updatedPreferredPersona, captain.PreferredPersona, StringComparison.Ordinal))
                {
                    captain.PreferredPersona = updatedPreferredPersona;
                    changed = true;
                }

                if (!changed) continue;

                captain.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
                _Logging.Debug(_Header + "updated captain persona references: " + captain.Name);
            }
        }

        private static bool ShouldUpgradeBuiltInPipeline(Pipeline existing, IEnumerable<PipelineStage> desiredStages)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (!existing.IsBuiltIn) return false;
            if (existing.Stages == null || existing.Stages.Count == 0) return false;

            List<string> existingPersonaOrder = existing.Stages
                .OrderBy(stage => stage.Order)
                .Select(stage => PersonaCatalog.NormalizeName(stage.PersonaName))
                .ToList();

            List<string> desiredPersonaOrder = desiredStages
                .OrderBy(stage => stage.Order)
                .Select(stage => PersonaCatalog.NormalizeName(stage.PersonaName))
                .ToList();

            bool containsLegacyAlias = existing.Stages.Any(stage =>
                PersonaCatalog.Matches(stage.PersonaName, PersonaCatalog.TestEngineer) &&
                !String.Equals(stage.PersonaName, PersonaCatalog.TestEngineer, StringComparison.Ordinal));

            if (containsLegacyAlias && existingPersonaOrder.SequenceEqual(desiredPersonaOrder, StringComparer.Ordinal))
                return true;

            if (String.Equals(existing.Name, "FullPipeline", StringComparison.Ordinal))
            {
                // Upgrade the built-in FullPipeline whenever its stages differ from the seeded definition,
                // so existing installs pick up new stages (e.g. the Recorder appended after Judge).
                return !existingPersonaOrder.SequenceEqual(desiredPersonaOrder, StringComparer.Ordinal);
            }

            return false;
        }

        #endregion
    }
}
