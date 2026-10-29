namespace Armada.Core.Services
{
    using System.Text.Json;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;

    /// <summary>Preserve diagnostic evidence before cancellation or deletion removes the original mission/attempts. The
    /// snapshot records the mission's current black box entry id (BlackBoxEntryId) so an archive links to its black box.</summary>
    public static class MissionArchive
    {
        /// <summary>Write a tenant-scoped snapshot first; if storage fails, the destructive operation must not proceed.</summary>
        public static async Task RecordAsync(IEventMethods events, IStageOutcomeMethods outcomes, Mission? mission,
            string operation, CancellationToken token = default)
        {
            if (mission == null) return;
            List<StageOutcome> attempts = new();
            for (int page = 1; ; page++)
            {
                EnumerationResult<StageOutcome> result = await outcomes.EnumerateAsync(new StageOutcomeQuery
                    { MissionId = mission.Id, TenantId = mission.TenantId, PageNumber = page, PageSize = 500 }, token).ConfigureAwait(false);
                attempts.AddRange(result.Objects);
                if (page >= result.TotalPages) break;
            }
            List<ArmadaEvent> diagnostics = await events.EnumerateByMissionAsync(mission.Id, 500, token).ConfigureAwait(false);
            await events.CreateAsync(new ArmadaEvent("mission.archive", "Mission " + operation + " requested: " + mission.Title + "; this does not establish resolution")
            {
                TenantId = mission.TenantId, UserId = mission.UserId, EntityType = "mission_archive", EntityId = mission.Id,
                MissionId = mission.Id, VesselId = mission.VesselId, VoyageId = mission.VoyageId, CaptainId = mission.CaptainId,
                Payload = JsonSerializer.Serialize(new
                {
                    Operation = operation, RecordedUtc = DateTime.UtcNow, ResolutionVerified = false,
                    mission.Id, mission.Title, mission.Status, mission.FailureKind, mission.FailureReason,
                    mission.BranchName, mission.CommitHash, mission.DockId, mission.AssignedHarborId,
                    mission.CaptainId, mission.VoyageId, mission.VesselId, mission.CreatedUtc, mission.StartedUtc, mission.CompletedUtc,
                    BlackBoxEntryId = mission.LastBlackBoxEntryId,
                    StageOutcomes = attempts,
                    Diagnostics = diagnostics.Where(e => e.EventType != "mission.archive").ToList(),
                    DiagnosticsTruncated = diagnostics.Count >= 500
                })
            }, token).ConfigureAwait(false);
        }
    }
}
