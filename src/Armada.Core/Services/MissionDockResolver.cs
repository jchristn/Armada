namespace Armada.Core.Services
{
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>Resolve only a mission's recorded dock, without borrowing a captain or branch worktree.</summary>
    public static class MissionDockResolver
    {
        /// <summary>Read the recorded dock within the mission's ownership scope, or null when unavailable/inconsistent.</summary>
        public static async Task<Dock?> ReadAsync(DatabaseDriver database, Mission mission, CancellationToken token = default, AuthContext? caller = null)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (String.IsNullOrEmpty(mission.DockId)) return null;
            Dock? dock = caller == null || caller.IsAdmin
                ? await database.Docks.ReadAsync(mission.DockId, token).ConfigureAwait(false)
                : caller.IsTenantAdmin
                    ? await database.Docks.ReadAsync(caller.TenantId!, mission.DockId, token).ConfigureAwait(false)
                    : await database.Docks.ReadAsync(caller.TenantId!, caller.UserId!, mission.DockId, token).ConfigureAwait(false);
            if (dock == null || dock.TenantId != mission.TenantId
                || (!String.IsNullOrEmpty(mission.VesselId) && dock.VesselId != mission.VesselId)) return null;
            return dock;
        }
    }
}
