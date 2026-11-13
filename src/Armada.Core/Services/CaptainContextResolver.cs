namespace Armada.Core.Services
{
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>Resolve an explicit mission/session context or an unambiguous credential host.</summary>
    public static class CaptainContextResolver
    {
        /// <summary>Resolve the assignment or session dock, or a common host without choosing a mission worktree.</summary>
        public static async Task<Dock?> ReadDockAsync(DatabaseDriver database, Captain captain, CancellationToken token = default)
        {
            if (captain.Assignment != null) return await MissionDockResolver.ReadAsync(database, captain.Assignment, token).ConfigureAwait(false);
            if (captain.CurrentSessionId != null)
            {
                // Backlog refinement has no dock. Never borrow one from a mission or vessel.
                if (captain.CurrentSessionId.StartsWith("ors_", StringComparison.Ordinal)) return null;
                PlanningSession? planning = await database.PlanningSessions.ReadAsync(captain.CurrentSessionId, token).ConfigureAwait(false);
                if (planning == null || planning.CaptainId != captain.Id || planning.TenantId != captain.TenantId || planning.DockId == null) return null;
                Dock? sessionDock = await database.Docks.ReadAsync(planning.DockId, token).ConfigureAwait(false);
                return sessionDock?.TenantId == captain.TenantId && sessionDock?.VesselId == planning.VesselId ? sessionDock : null;
            }
            List<Dock> docks = new();
            foreach (Mission mission in (await database.Missions.EnumerateByCaptainAsync(captain.Id, token).ConfigureAwait(false))
                .Where(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress || m.Status == MissionStatusEnum.Verifying))
            {
                Dock? dock = await MissionDockResolver.ReadAsync(database, mission, token).ConfigureAwait(false);
                if (dock != null) docks.Add(dock);
            }
            if (docks.Count == 0) return null;
            if (docks.Select(d => d.HarborId ?? "").Distinct(StringComparer.Ordinal).Count() != 1)
                throw new InvalidOperationException("Captain assignments span multiple hosts. Select a credential Harbor explicitly instead of guessing a mission host.");
            if (docks.Select(d => d.Id).Distinct().Count() == 1) return docks[0];
            return new Dock(docks[0].VesselId) { HarborId = docks[0].HarborId, TenantId = captain.TenantId };
        }
    }
}
