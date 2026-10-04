namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>
    /// Caller-scoped entity resolution for MCP tools. Every MCP tool that reads or acts on an entity by id resolves it
    /// through these methods, which apply the same rule as the REST handlers: a global admin sees every tenant, a tenant
    /// admin sees their tenant, and a regular user sees their own entities in their tenant. An entity outside the
    /// caller's scope resolves to null, so the tool answers with the same not-found error it gives for an id that does
    /// not exist (REST answers 404 in both cases).
    /// </summary>
    public static class McpCallerScope
    {
        #region Public-Methods

        /// <summary>
        /// Whether the caller sees every tenant (a global admin).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <returns>True for a global admin.</returns>
        public static bool IsGlobal(AuthContext caller)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            return caller.IsAdmin;
        }

        /// <summary>
        /// Whether an entity with the given owning tenant and user is inside the caller's scope.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="tenantId">Entity tenant.</param>
        /// <param name="userId">Entity owning user.</param>
        /// <returns>True when the caller may see or act on the entity.</returns>
        public static bool CanAccess(AuthContext caller, string? tenantId, string? userId)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (caller.IsAdmin) return true;
            if (!String.Equals(tenantId, caller.TenantId, StringComparison.Ordinal)) return false;
            if (caller.IsTenantAdmin) return true;
            return String.Equals(userId, caller.UserId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Read a fleet within the caller's scope.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Fleet id.</param>
        /// <returns>The fleet, or null when it does not exist or is outside the caller's scope.</returns>
        public static async Task<Fleet?> ReadFleetAsync(DatabaseDriver database, AuthContext caller, string? id)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(id)) return null;
            if (caller.IsAdmin) return await database.Fleets.ReadAsync(id).ConfigureAwait(false);
            if (caller.IsTenantAdmin) return await database.Fleets.ReadAsync(caller.TenantId!, id).ConfigureAwait(false);
            return await database.Fleets.ReadAsync(caller.TenantId!, caller.UserId!, id).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a vessel within the caller's scope.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Vessel id.</param>
        /// <returns>The vessel, or null when it does not exist or is outside the caller's scope.</returns>
        public static async Task<Vessel?> ReadVesselAsync(DatabaseDriver database, AuthContext caller, string? id)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(id)) return null;
            if (caller.IsAdmin) return await database.Vessels.ReadAsync(id).ConfigureAwait(false);
            if (caller.IsTenantAdmin) return await database.Vessels.ReadAsync(caller.TenantId!, id).ConfigureAwait(false);
            return await database.Vessels.ReadAsync(caller.TenantId!, caller.UserId!, id).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a voyage within the caller's scope.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Voyage id.</param>
        /// <returns>The voyage, or null when it does not exist or is outside the caller's scope.</returns>
        public static async Task<Voyage?> ReadVoyageAsync(DatabaseDriver database, AuthContext caller, string? id)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(id)) return null;
            if (caller.IsAdmin) return await database.Voyages.ReadAsync(id).ConfigureAwait(false);
            if (caller.IsTenantAdmin) return await database.Voyages.ReadAsync(caller.TenantId!, id).ConfigureAwait(false);
            return await database.Voyages.ReadAsync(caller.TenantId!, caller.UserId!, id).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a mission within the caller's scope.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Mission id.</param>
        /// <returns>The mission, or null when it does not exist or is outside the caller's scope.</returns>
        public static async Task<Mission?> ReadMissionAsync(DatabaseDriver database, AuthContext caller, string? id)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(id)) return null;
            if (caller.IsAdmin) return await database.Missions.ReadAsync(id).ConfigureAwait(false);
            if (caller.IsTenantAdmin) return await database.Missions.ReadAsync(caller.TenantId!, id).ConfigureAwait(false);
            return await database.Missions.ReadAsync(caller.TenantId!, caller.UserId!, id).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a captain within the caller's scope.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Captain id.</param>
        /// <returns>The captain, or null when it does not exist or is outside the caller's scope.</returns>
        public static async Task<Captain?> ReadCaptainAsync(DatabaseDriver database, AuthContext caller, string? id)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(id)) return null;
            if (caller.IsAdmin) return await database.Captains.ReadAsync(id).ConfigureAwait(false);
            if (caller.IsTenantAdmin) return await database.Captains.ReadAsync(caller.TenantId!, id).ConfigureAwait(false);
            return await database.Captains.ReadAsync(caller.TenantId!, caller.UserId!, id).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a dock within the caller's scope.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Dock id.</param>
        /// <returns>The dock, or null when it does not exist or is outside the caller's scope.</returns>
        public static async Task<Dock?> ReadDockAsync(DatabaseDriver database, AuthContext caller, string? id)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(id)) return null;
            if (caller.IsAdmin) return await database.Docks.ReadAsync(id).ConfigureAwait(false);
            if (caller.IsTenantAdmin) return await database.Docks.ReadAsync(caller.TenantId!, id).ConfigureAwait(false);
            return await database.Docks.ReadAsync(caller.TenantId!, caller.UserId!, id).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a merge queue entry within the caller's scope.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Merge entry id.</param>
        /// <returns>The entry, or null when it does not exist or is outside the caller's scope.</returns>
        public static async Task<MergeEntry?> ReadMergeEntryAsync(DatabaseDriver database, AuthContext caller, string? id)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(id)) return null;
            if (caller.IsAdmin) return await database.MergeEntries.ReadAsync(id).ConfigureAwait(false);
            if (caller.IsTenantAdmin) return await database.MergeEntries.ReadAsync(caller.TenantId!, id).ConfigureAwait(false);
            return await database.MergeEntries.ReadAsync(caller.TenantId!, caller.UserId!, id).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a signal within the caller's scope.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Signal id.</param>
        /// <returns>The signal, or null when it does not exist or is outside the caller's scope.</returns>
        public static async Task<Signal?> ReadSignalAsync(DatabaseDriver database, AuthContext caller, string? id)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(id)) return null;
            if (caller.IsAdmin) return await database.Signals.ReadAsync(id).ConfigureAwait(false);
            if (caller.IsTenantAdmin) return await database.Signals.ReadAsync(caller.TenantId!, id).ConfigureAwait(false);
            return await database.Signals.ReadAsync(caller.TenantId!, caller.UserId!, id).ConfigureAwait(false);
        }

        /// <summary>
        /// Read an objective (backlog item) within the caller's scope.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Objective id.</param>
        /// <returns>The objective, or null when it does not exist or is outside the caller's scope.</returns>
        public static async Task<Objective?> ReadObjectiveAsync(DatabaseDriver database, AuthContext caller, string? id)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(id)) return null;
            if (caller.IsAdmin) return await database.Objectives.ReadAsync(id).ConfigureAwait(false);
            if (caller.IsTenantAdmin) return await database.Objectives.ReadAsync(caller.TenantId!, id).ConfigureAwait(false);
            return await database.Objectives.ReadAsync(caller.TenantId!, caller.UserId!, id).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a check run within the caller's scope.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Check run id.</param>
        /// <returns>The check run, or null when it does not exist or is outside the caller's scope.</returns>
        public static async Task<CheckRun?> ReadCheckRunAsync(DatabaseDriver database, AuthContext caller, string? id)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(id)) return null;
            CheckRunQuery query = new CheckRunQuery();
            if (!caller.IsAdmin)
            {
                query.TenantId = caller.TenantId;
                if (!caller.IsTenantAdmin) query.UserId = caller.UserId;
            }

            return await database.CheckRuns.ReadAsync(id, query).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a playbook within the caller's scope (tenant, then the scoped-visibility rule for regular users).
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Playbook id.</param>
        /// <returns>The playbook, or null when it does not exist or is not visible to the caller.</returns>
        public static async Task<Playbook?> ReadPlaybookAsync(DatabaseDriver database, AuthContext caller, string? id)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(id)) return null;
            Playbook? playbook = caller.IsAdmin
                ? await database.Playbooks.ReadAsync(id).ConfigureAwait(false)
                : await database.Playbooks.ReadAsync(caller.TenantId!, id).ConfigureAwait(false);
            if (playbook == null) return null;
            return ScopedVisibility.CanView(caller, playbook.Scope, playbook.TenantId, playbook.UserId) ? playbook : null;
        }

        /// <summary>
        /// Read a persona by name within the caller's scope: the caller's tenant copy first, then (for a global admin)
        /// any tenant's copy; visibility follows <see cref="ScopedVisibility"/>, as the REST routes do.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="name">Persona name.</param>
        /// <returns>The persona, or null when it does not exist or is not visible to the caller.</returns>
        public static async Task<Persona?> ReadPersonaAsync(DatabaseDriver database, AuthContext caller, string? name)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(name)) return null;
            Persona? persona = null;
            if (!String.IsNullOrEmpty(caller.TenantId))
                persona = await database.Personas.ReadByNameAsync(caller.TenantId!, name).ConfigureAwait(false);
            if (persona == null && caller.IsAdmin)
                persona = await database.Personas.ReadByNameAsync(name).ConfigureAwait(false);
            if (persona == null) return null;
            return ScopedVisibility.CanView(caller, persona.Scope, persona.TenantId, persona.UserId) ? persona : null;
        }

        /// <summary>
        /// Read a pipeline by name within the caller's scope (same rule as <see cref="ReadPersonaAsync"/>).
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="name">Pipeline name.</param>
        /// <returns>The pipeline, or null when it does not exist or is not visible to the caller.</returns>
        public static async Task<Pipeline?> ReadPipelineAsync(DatabaseDriver database, AuthContext caller, string? name)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(name)) return null;
            Pipeline? pipeline = null;
            if (!String.IsNullOrEmpty(caller.TenantId))
                pipeline = await database.Pipelines.ReadByNameAsync(caller.TenantId!, name).ConfigureAwait(false);
            if (pipeline == null && caller.IsAdmin)
                pipeline = await database.Pipelines.ReadByNameAsync(name).ConfigureAwait(false);
            if (pipeline == null) return null;
            return ScopedVisibility.CanView(caller, pipeline.Scope, pipeline.TenantId, pipeline.UserId) ? pipeline : null;
        }

        /// <summary>
        /// Read a pipeline by id within the caller's scope.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Pipeline id.</param>
        /// <returns>The pipeline, or null when it does not exist or is not visible to the caller.</returns>
        public static async Task<Pipeline?> ReadPipelineByIdAsync(DatabaseDriver database, AuthContext caller, string? id)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(id)) return null;
            Pipeline? pipeline = await database.Pipelines.ReadAsync(id).ConfigureAwait(false);
            if (pipeline == null) return null;
            return ScopedVisibility.CanView(caller, pipeline.Scope, pipeline.TenantId, pipeline.UserId) ? pipeline : null;
        }

        /// <summary>
        /// Read a prompt template by name within the caller's scope (same rule as <see cref="ReadPersonaAsync"/>).
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="name">Template name.</param>
        /// <returns>The template, or null when it does not exist or is not visible to the caller.</returns>
        public static async Task<PromptTemplate?> ReadPromptTemplateAsync(DatabaseDriver database, AuthContext caller, string? name)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(name)) return null;
            PromptTemplate? template = null;
            if (!String.IsNullOrEmpty(caller.TenantId))
                template = await database.PromptTemplates.ReadByNameAsync(caller.TenantId!, name).ConfigureAwait(false);
            if (template == null && caller.IsAdmin)
                template = await database.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false);
            if (template == null) return null;
            return ScopedVisibility.CanView(caller, template.Scope, template.TenantId, template.UserId) ? template : null;
        }

        #endregion
    }
}
