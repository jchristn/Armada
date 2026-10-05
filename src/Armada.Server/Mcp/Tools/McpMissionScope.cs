namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>
    /// What a captain holding a mission-scoped MCP token may do beyond the mission owner's own permissions. The token
    /// acts as the mission's owner; the one addition is the mission workflow's own vessel-context update
    /// (<c>update_vessel_context</c>, TenantAdmin otherwise), which is allowed only for the mission's vessel.
    /// </summary>
    public static class McpMissionScope
    {
        #region Private-Members

        private const string UpdateVesselContextTool = "update_vessel_context";

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when a mission-scoped caller may call a tool its owner's role would not allow: only
        /// <c>update_vessel_context</c> for the vessel of the caller's own mission.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="toolName">Tool name.</param>
        /// <param name="args">Tool arguments.</param>
        /// <returns>True when allowed.</returns>
        public static async Task<bool> AllowsAsync(DatabaseDriver database, AuthContext caller, string toolName, JsonElement? args)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(caller.MissionId) || String.IsNullOrEmpty(caller.TenantId)) return false;
            if (!String.Equals(toolName, UpdateVesselContextTool, StringComparison.Ordinal)) return false;
            if (args == null) return false;

            VesselContextArgs? request;
            try
            {
                request = JsonSerializer.Deserialize<VesselContextArgs>(args.Value, _JsonOptions);
            }
            catch (JsonException)
            {
                return false;
            }

            if (request == null || String.IsNullOrEmpty(request.VesselId)) return false;
            Vessel? vessel = await ReadMissionVesselAsync(database, caller, request.VesselId).ConfigureAwait(false);
            return vessel != null;
        }

        /// <summary>
        /// Read the vessel of a mission-scoped caller's own mission, or null when the caller is not mission-scoped or the
        /// id is not that mission's vessel.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="vesselId">Vessel id.</param>
        /// <returns>The vessel, or null.</returns>
        public static async Task<Vessel?> ReadMissionVesselAsync(DatabaseDriver database, AuthContext caller, string? vesselId)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrEmpty(caller.MissionId) || String.IsNullOrEmpty(caller.TenantId) || String.IsNullOrEmpty(vesselId)) return null;
            Mission? mission = await database.Missions.ReadAsync(caller.TenantId!, caller.MissionId!).ConfigureAwait(false);
            if (mission == null || !String.Equals(mission.VesselId, vesselId, StringComparison.Ordinal)) return null;
            return await database.Vessels.ReadAsync(caller.TenantId!, vesselId!).ConfigureAwait(false);
        }

        #endregion
    }
}
