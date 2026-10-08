namespace Armada.Core.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>
    /// User-facing uniqueness checks run before a create or update writes, so a taken name, email, or file name is
    /// refused with a <see cref="DuplicateEntityException"/> that names the field ("A captain named 'X' already
    /// exists.") instead of reaching the database. Names are unique within the entity's tenant (or across all
    /// entities when it has none), compared the way the provider compares them. Each check ignores the entity's own
    /// row, so an update that keeps its value passes. The check is not race-safe on its own: a concurrent create that
    /// slips past it is caught by the provider's unique constraint, which the database layer translates
    /// (<see cref="DuplicateEntityTranslationProxy"/>).
    /// </summary>
    public static class DuplicateEntityGuard
    {
        #region Public-Methods

        /// <summary>
        /// Refuse a fleet whose name is already used by another fleet in its tenant.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="fleet">Fleet about to be created or updated.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="DuplicateEntityException">Thrown when the name is taken.</exception>
        public static async Task EnsureFleetNameAvailableAsync(DatabaseDriver database, Fleet fleet, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (fleet == null) throw new ArgumentNullException(nameof(fleet));
            if (String.IsNullOrEmpty(fleet.Name)) return;
            Fleet? other = String.IsNullOrEmpty(fleet.TenantId)
                ? await database.Fleets.ReadByNameAsync(fleet.Name, token).ConfigureAwait(false)
                : await database.Fleets.ReadByNameAsync(fleet.TenantId!, fleet.Name, token).ConfigureAwait(false);
            if (IsOther(other?.Id, fleet.Id)) throw Named("Fleet", "fleet", fleet.Name);
        }

        /// <summary>
        /// Refuse a vessel whose name is already used by another vessel in its tenant.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="vessel">Vessel about to be created or updated.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="DuplicateEntityException">Thrown when the name is taken.</exception>
        public static async Task EnsureVesselNameAvailableAsync(DatabaseDriver database, Vessel vessel, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (String.IsNullOrEmpty(vessel.Name)) return;
            Vessel? other = String.IsNullOrEmpty(vessel.TenantId)
                ? await database.Vessels.ReadByNameAsync(vessel.Name, token).ConfigureAwait(false)
                : await database.Vessels.ReadByNameAsync(vessel.TenantId!, vessel.Name, token).ConfigureAwait(false);
            if (IsOther(other?.Id, vessel.Id)) throw Named("Vessel", "vessel", vessel.Name);
        }

        /// <summary>
        /// Refuse a captain whose name is already used by another captain in its tenant.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="captain">Captain about to be created or updated.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="DuplicateEntityException">Thrown when the name is taken.</exception>
        public static async Task EnsureCaptainNameAvailableAsync(DatabaseDriver database, Captain captain, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (String.IsNullOrEmpty(captain.Name)) return;
            Captain? other = String.IsNullOrEmpty(captain.TenantId)
                ? await database.Captains.ReadByNameAsync(captain.Name, token).ConfigureAwait(false)
                : await database.Captains.ReadByNameAsync(captain.TenantId!, captain.Name, token).ConfigureAwait(false);
            if (IsOther(other?.Id, captain.Id)) throw Named("Captain", "captain", captain.Name);
        }

        /// <summary>
        /// Refuse a persona whose name is already used by another persona in its tenant.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="persona">Persona about to be created or updated.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="DuplicateEntityException">Thrown when the name is taken.</exception>
        public static async Task EnsurePersonaNameAvailableAsync(DatabaseDriver database, Persona persona, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (persona == null) throw new ArgumentNullException(nameof(persona));
            if (String.IsNullOrEmpty(persona.Name)) return;
            Persona? other = String.IsNullOrEmpty(persona.TenantId)
                ? await database.Personas.ReadByNameAsync(persona.Name, token).ConfigureAwait(false)
                : await database.Personas.ReadByNameAsync(persona.TenantId!, persona.Name, token).ConfigureAwait(false);
            if (IsOther(other?.Id, persona.Id)) throw Named("Persona", "persona", persona.Name);
        }

        /// <summary>
        /// Refuse a pipeline whose name is already used by another pipeline in its tenant.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="pipeline">Pipeline about to be created or updated.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="DuplicateEntityException">Thrown when the name is taken.</exception>
        public static async Task EnsurePipelineNameAvailableAsync(DatabaseDriver database, Pipeline pipeline, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (String.IsNullOrEmpty(pipeline.Name)) return;
            Pipeline? other = String.IsNullOrEmpty(pipeline.TenantId)
                ? await database.Pipelines.ReadByNameAsync(pipeline.Name, token).ConfigureAwait(false)
                : await database.Pipelines.ReadByNameAsync(pipeline.TenantId!, pipeline.Name, token).ConfigureAwait(false);
            if (IsOther(other?.Id, pipeline.Id)) throw Named("Pipeline", "pipeline", pipeline.Name);
        }

        /// <summary>
        /// Refuse a prompt template whose name is already used by another template in its tenant.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="template">Template about to be created or updated.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="DuplicateEntityException">Thrown when the name is taken.</exception>
        public static async Task EnsurePromptTemplateNameAvailableAsync(DatabaseDriver database, PromptTemplate template, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (String.IsNullOrEmpty(template.Name)) return;
            PromptTemplate? other = String.IsNullOrEmpty(template.TenantId)
                ? await database.PromptTemplates.ReadByNameAsync(template.Name, token).ConfigureAwait(false)
                : await database.PromptTemplates.ReadByNameAsync(template.TenantId!, template.Name, token).ConfigureAwait(false);
            if (IsOther(other?.Id, template.Id)) throw Named("PromptTemplate", "prompt template", template.Name);
        }

        /// <summary>
        /// Refuse a playbook whose file name is already used by another playbook in its tenant.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="playbook">Playbook about to be created or updated.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="DuplicateEntityException">Thrown when the file name is taken.</exception>
        public static async Task EnsurePlaybookFileNameAvailableAsync(DatabaseDriver database, Playbook playbook, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (playbook == null) throw new ArgumentNullException(nameof(playbook));
            if (String.IsNullOrEmpty(playbook.FileName)) return;
            string tenantId = String.IsNullOrEmpty(playbook.TenantId) ? Constants.DefaultTenantId : playbook.TenantId!;
            Playbook? other = await database.Playbooks.ReadByFileNameAsync(tenantId, playbook.FileName, token).ConfigureAwait(false);
            if (IsOther(other?.Id, playbook.Id))
            {
                throw new DuplicateEntityException("Playbook", "FileName", playbook.FileName,
                    "A playbook with file name '" + playbook.FileName + "' already exists.");
            }
        }

        /// <summary>
        /// Refuse a user whose email is already used by another user in the same tenant.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="user">User about to be created or updated.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="DuplicateEntityException">Thrown when the email is taken.</exception>
        public static async Task EnsureUserEmailAvailableAsync(DatabaseDriver database, UserMaster user, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (String.IsNullOrEmpty(user.Email) || String.IsNullOrEmpty(user.TenantId)) return;
            UserMaster? other = await database.Users.ReadByEmailAsync(user.TenantId, user.Email, token).ConfigureAwait(false);
            if (IsOther(other?.Id, user.Id))
            {
                throw new DuplicateEntityException("User", "Email", user.Email,
                    "A user with email '" + user.Email + "' already exists in this tenant.");
            }
        }

        /// <summary>
        /// Build the exception for a taken name, for example "A captain named 'X' already exists.".
        /// </summary>
        /// <param name="entityType">Entity type, for example Captain.</param>
        /// <param name="label">Lower-case label for the message, for example captain.</param>
        /// <param name="name">The taken name.</param>
        /// <returns>Exception.</returns>
        public static DuplicateEntityException NameTaken(string entityType, string label, string name)
        {
            return new DuplicateEntityException(entityType, "Name", name, "A " + label + " named '" + name + "' already exists.");
        }

        #endregion

        #region Private-Methods

        private static bool IsOther(string? existingId, string? id)
        {
            if (existingId == null) return false;
            return !String.Equals(existingId, id, StringComparison.Ordinal);
        }

        private static DuplicateEntityException Named(string entityType, string label, string name)
        {
            return NameTaken(entityType, label, name);
        }

        #endregion
    }
}
