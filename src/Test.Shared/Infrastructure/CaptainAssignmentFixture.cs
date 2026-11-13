namespace Test.Shared.Infrastructure
{
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>Seed runtime test contexts through real mission rows and atomic slot operations.</summary>
    internal static class CaptainAssignmentFixture
    {
        internal static async Task<Captain> CreateAsync(DatabaseDriver db, Captain captain, CancellationToken token = default)
        {
            CaptainStateEnum state = captain.State; bool active = captain.Active;
            if (captain.Assignment != null) { captain.State = CaptainStateEnum.Idle; captain.Active = true; }
            await db.Captains.CreateAsync(captain, token);
            if (captain.Assignment != null) await SeedAsync(db, captain, state, active, token);
            return captain;
        }
        internal static async Task<Captain> UpdateAsync(DatabaseDriver db, Captain captain, CancellationToken token = default, bool updateActive = false, bool updateConcurrency = false)
        {
            await db.Captains.UpdateAsync(captain, token, updateActive, updateConcurrency);
            if (captain.Assignment != null) await SeedAsync(db, captain, captain.State, captain.Active, token);
            return captain;
        }
        private static async Task SeedAsync(DatabaseDriver db, Captain captain, CaptainStateEnum state, bool active, CancellationToken token)
        {
            Mission context = captain.Assignment!;
            Mission? mission = await db.Missions.ReadAsync(context.Id, token);
            if (mission == null && context.Title == "fixture assignment")
            {
                context.TenantId = captain.TenantId; context.UserId = captain.UserId;
                if (context.DockId != null) context.VesselId = (await db.Docks.ReadAsync(context.DockId, token))?.VesselId;
                mission = await db.Missions.CreateAsync(context, token);
            }
            int? processId = captain.ProcessId ?? context.ProcessId ?? mission?.ProcessId;
            MissionStatusEnum? originalStatus = mission?.Status;
            if (mission?.CaptainId != null && mission.CaptainId != captain.Id) return;
            Captain stored = (await db.Captains.ReadAsync(captain.Id, token))!;
            if (!stored.ActiveMissionIds.Contains(context.Id) && (mission == null || mission.Status == MissionStatusEnum.Pending || mission.Status == MissionStatusEnum.Assigned || mission.Status == MissionStatusEnum.InProgress))
            {
                await db.Captains.UpdateStateAsync(captain.Id, CaptainStateEnum.Idle, token);
                stored.State = CaptainStateEnum.Idle; stored.Active = true; await db.Captains.UpdateAsync(stored, token, updateActive: true);
                if (mission != null) { mission.Status = MissionStatusEnum.Pending; await db.Missions.UpdateAsync(mission, token); }
                await db.Captains.TryClaimAsync(captain.Id, context.Id, context.DockId, token);
            }
            if (mission != null)
            {
                mission.CaptainId = captain.Id;
                if (context.DockId != null) mission.DockId = context.DockId;
                mission.ProcessId = processId;
                mission.StallRecoveryAttempts = captain.RecoveryAttempts;
                mission.LastHeartbeatUtc = captain.LastHeartbeatUtc ?? mission.LastHeartbeatUtc;
                if (originalStatus.HasValue) mission.Status = originalStatus == MissionStatusEnum.Pending ? MissionStatusEnum.Assigned : originalStatus.Value;
                context.CaptainId = mission.CaptainId; context.Status = mission.Status; context.ProcessId = mission.ProcessId;
                context.StallRecoveryAttempts = mission.StallRecoveryAttempts; context.LastHeartbeatUtc = mission.LastHeartbeatUtc;
                await db.Missions.UpdateAsync(mission, token);
                await db.Missions.TryUpdateRuntimeContextAsync(mission.Id, captain.Id, mission.ProcessId, mission.LastHeartbeatUtc, mission.StallRecoveryAttempts, token);
            }
            if (context.DockId != null) await db.Captains.TrySetDockAsync(captain.Id, context.Id, context.DockId, token);
            if (processId.HasValue) await db.Captains.TrySetProcessAsync(captain.Id, context.Id, processId.Value, token);
            await db.Captains.UpdateStateAsync(captain.Id, state, token);
            captain.State = state; captain.Active = active;
            await db.Captains.UpdateAsync(captain, token, updateActive: true);
            Captain refreshed = (await db.Captains.ReadAsync(captain.Id, token))!;
            captain.ActiveMissionIds = refreshed.ActiveMissionIds; captain.ActiveMissionCount = refreshed.ActiveMissionCount;
        }
    }
}
