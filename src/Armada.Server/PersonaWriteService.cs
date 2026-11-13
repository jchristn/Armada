namespace Armada.Server
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Server.Mcp.Tools;

    /// <summary>
    /// Persona create, update, and delete shared by REST, MCP, and WebSocket, so every surface applies the same
    /// validation, scoping, and field semantics. A default captain must be visible to the caller; routing fields are
    /// validated; a new persona is owned by the caller in the scope it may create, and is never built in.
    /// </summary>
    public sealed class PersonaWriteService
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public PersonaWriteService(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a persona owned by the caller.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="persona">Persona to create; Name and PromptTemplateName are required. Scope is a request that is
        /// resolved against what the caller may create; IsBuiltIn is ignored.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller or persona is null.</exception>
        /// <exception cref="DuplicateEntityException">Thrown when a persona with that name already exists.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when CaptainPriorities holds more than <see cref="CollectionLimits.MaxConfigItems"/> entries.</exception>
        public async Task<PersonaWriteResult> CreateAsync(AuthContext caller, Persona persona, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (persona == null) throw new ArgumentNullException(nameof(persona));

            if (String.IsNullOrWhiteSpace(persona.Name)) return PersonaWriteResult.Fail(PersonaWriteStatusEnum.Invalid, "Name is required");
            if (String.IsNullOrWhiteSpace(persona.PromptTemplateName)) return PersonaWriteResult.Fail(PersonaWriteStatusEnum.Invalid, "PromptTemplateName is required");
            CollectionBounds.Validate(persona);
            PersonaWriteResult? captainError = await CheckDefaultCaptainAsync(caller, persona.DefaultCaptainId).ConfigureAwait(false);
            if (captainError != null) return captainError;
            string? routingError = PersonaCaptainPrioritySelector.Validate(persona);
            if (routingError != null) return PersonaWriteResult.Fail(PersonaWriteStatusEnum.Invalid, routingError);

            persona.IsBuiltIn = false;
            persona.TenantId = String.IsNullOrEmpty(caller.TenantId) ? Armada.Core.Constants.DefaultTenantId : caller.TenantId;
            persona.UserId = caller.UserId;
            persona.Scope = ScopedVisibility.ResolveCreateScope(caller, persona.Scope);
            await DuplicateEntityGuard.EnsurePersonaNameAvailableAsync(_Database, persona).ConfigureAwait(false);
            return PersonaWriteResult.Success(await _Database.Personas.CreateAsync(persona, token).ConfigureAwait(false));
        }

        /// <summary>
        /// Apply a partial update. Omitted fields are kept; an empty CaptainPriorities list clears the hierarchy; the
        /// default captain changes only when <paramref name="defaultCaptainIdPresent"/> is true, and a null or empty
        /// value then clears it. Scope changes only for an administrator.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="name">Persona name.</param>
        /// <param name="update">Fields to change.</param>
        /// <param name="defaultCaptainIdPresent">Whether the request carried DefaultCaptainId (including an explicit null).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller or update is null.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when CaptainPriorities holds more than <see cref="CollectionLimits.MaxConfigItems"/> entries.</exception>
        public async Task<PersonaWriteResult> UpdateAsync(AuthContext caller, string name, PersonaUpdateRequest update, bool defaultCaptainIdPresent, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (update == null) throw new ArgumentNullException(nameof(update));
            CollectionLimits.RequireConfigItems(update.CaptainPriorities, "CaptainPriorities");

            Persona? existing = await ReadVisibleAsync(caller, name, token).ConfigureAwait(false);
            if (existing == null) return PersonaWriteResult.Fail(PersonaWriteStatusEnum.NotFound, "Persona not found: " + name);
            if (!ScopedVisibility.CanEdit(caller, existing.Scope, existing.TenantId, existing.UserId))
                return PersonaWriteResult.Fail(PersonaWriteStatusEnum.Forbidden, "You may only modify your own personas; a tenant-wide persona requires a tenant admin.");
            if (defaultCaptainIdPresent)
            {
                PersonaWriteResult? captainError = await CheckDefaultCaptainAsync(caller, update.DefaultCaptainId).ConfigureAwait(false);
                if (captainError != null) return captainError;
            }

            if (update.Description != null) existing.Description = update.Description;
            if (update.PromptTemplateName != null)
            {
                if (String.IsNullOrWhiteSpace(update.PromptTemplateName)) return PersonaWriteResult.Fail(PersonaWriteStatusEnum.Invalid, "PromptTemplateName cannot be empty");
                existing.PromptTemplateName = update.PromptTemplateName;
            }

            if (defaultCaptainIdPresent) existing.DefaultCaptainId = String.IsNullOrEmpty(update.DefaultCaptainId) ? null : update.DefaultCaptainId;
            if ((caller.IsAdmin || caller.IsTenantAdmin) && update.Scope.HasValue) existing.Scope = update.Scope.Value;
            if (update.CaptainPriorities != null) existing.CaptainPriorities = update.CaptainPriorities;
            if (update.PriorityAvailability.HasValue) existing.PriorityAvailability = update.PriorityAvailability.Value;
            if (update.PriorityExhaustion.HasValue) existing.PriorityExhaustion = update.PriorityExhaustion.Value;
            string? routingError = PersonaCaptainPrioritySelector.Validate(existing);
            if (routingError != null) return PersonaWriteResult.Fail(PersonaWriteStatusEnum.Invalid, routingError);

            existing.LastUpdateUtc = DateTime.UtcNow;
            return PersonaWriteResult.Success(await _Database.Personas.UpdateAsync(existing, token).ConfigureAwait(false));
        }

        /// <summary>
        /// Delete a persona. Built-in personas cannot be deleted.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="name">Persona name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result; Persona is the deleted persona.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        public async Task<PersonaWriteResult> DeleteAsync(AuthContext caller, string name, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));

            Persona? existing = await ReadVisibleAsync(caller, name, token).ConfigureAwait(false);
            if (existing == null) return PersonaWriteResult.Fail(PersonaWriteStatusEnum.NotFound, "Persona not found: " + name);
            if (!ScopedVisibility.CanEdit(caller, existing.Scope, existing.TenantId, existing.UserId))
                return PersonaWriteResult.Fail(PersonaWriteStatusEnum.Forbidden, "You may only delete your own personas; a tenant-wide persona requires a tenant admin.");
            if (existing.IsBuiltIn) return PersonaWriteResult.Fail(PersonaWriteStatusEnum.Invalid, "Built-in personas cannot be deleted");

            await _Database.Personas.DeleteAsync(existing.Id, token).ConfigureAwait(false);
            return PersonaWriteResult.Success(existing);
        }

        #endregion

        #region Private-Methods

        private async Task<Persona?> ReadVisibleAsync(AuthContext caller, string name, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(name)) return null;
            Persona? persona = await _Database.Personas.ReadByNameAsync(name, token).ConfigureAwait(false);
            if (persona == null || !ScopedVisibility.CanView(caller, persona.Scope, persona.TenantId, persona.UserId)) return null;
            return persona;
        }

        private async Task<PersonaWriteResult?> CheckDefaultCaptainAsync(AuthContext caller, string? captainId)
        {
            if (String.IsNullOrEmpty(captainId)) return null;
            if (await McpCallerScope.ReadCaptainAsync(_Database, caller, captainId).ConfigureAwait(false) != null) return null;
            return PersonaWriteResult.Fail(PersonaWriteStatusEnum.Invalid, "Default captain '" + captainId + "' not found");
        }

        #endregion
    }
}
