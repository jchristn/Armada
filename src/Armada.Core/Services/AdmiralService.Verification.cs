namespace Armada.Core.Services
{
    using System.Collections.Concurrent;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    public partial class AdmiralService
    {
        private readonly ConcurrentDictionary<string, byte> _VerificationsInProgress = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        private async Task FinishVerificationAsync(Captain captain, Mission mission, int processId, StageAttemptCapture capture, CancellationToken token)
        {
            if (!_VerificationsInProgress.TryAdd(mission.Id, 0)) { capture.Claimed = false; return; }
            try
            {
                DateTime version = mission.LastUpdateUtc;
                if (!await _Database.Captains.ReleaseVerificationCapacityAsync(captain.Id, mission.Id, processId, version, token).ConfigureAwait(false))
                { capture.Claimed = false; return; }
                captain = captain.ForMission(mission);
                captain.RecoveryAttempts = mission.StallRecoveryAttempts;
                EnumerationResult<ArmadaEvent> prior = await _Database.Events.EnumerateAsync(new EnumerationQuery
                { MissionId = mission.Id, EventType = StageCheckEvents.EventType, PageSize = 1, Order = EnumerationOrderEnum.CreatedDescending }, token).ConfigureAwait(false);
                StageCheckResult? check = null;
                ArmadaEvent? saved = prior.Objects.FirstOrDefault();
                if (saved?.Payload != null)
                {
                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(saved.Payload);
                        if (document.RootElement.TryGetProperty("verificationVersion", out JsonElement stamp) && stamp.GetDateTime() == version)
                            check = StageCheckEvents.Read(saved);
                    }
                    catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is InvalidOperationException) { }
                }
                if (check == null)
                {
                    await EmitEventAsync("mission.stage_check_started", "Stage check queued or running for " + mission.Id,
                        entityType: "mission", entityId: mission.Id, captainId: captain.Id, missionId: mission.Id,
                        vesselId: mission.VesselId, voyageId: mission.VoyageId, token: token).ConfigureAwait(false);
                    check = await _Missions.CheckStageWorkAsync(mission, captain, token).ConfigureAwait(false);
                    await RecordStageCheckAsync(mission, captain, check, token).ConfigureAwait(false);
                }
                capture.StageCheck = check;
                Mission? after = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                if (after == null || after.Status != MissionStatusEnum.Verifying || after.ProcessId.HasValue
                    || after.LastUpdateUtc != version || after.StartedUtc != mission.StartedUtc)
                {
                    await EmitEventAsync("mission.stage_check_stale", "Ignoring a late stage check result for " + mission.Id,
                        entityType: "mission", entityId: mission.Id, missionId: mission.Id, token: token).ConfigureAwait(false);
                    return;
                }
                capture.StartedUtc = mission.StartedUtc;
                capture.DockId = mission.DockId;
                capture.OutputRepairAttemptsBefore = mission.OutputRepairAttempts;
                capture.RedispatchAttemptsBefore = mission.RedispatchAttempts;
                capture.Claimed = true;
                capture.Captain = captain;
                if (check.IsBlocking)
                {
                    string failure = check.FailureMessage ?? "The stage check failed (" + check.Status + "/" + check.Reason + "). Rerun the configured checks.";
                    if (captain.RecoveryAttempts < _Settings.MaxRecoveryAttempts)
                    {
                        if (!await _Missions.TryAdmitVerificationResumeAsync(after, captain, token).ConfigureAwait(false))
                        { capture.Claimed = false; return; }
                        Mission? resumed = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                        if (resumed == null) return;
                        after = resumed;
                        if (await TryResumeInPlaceAsync(captain, resumed, failure, token).ConfigureAwait(false)) return;
                    }
                    if (await TryEscalateToPlannerAsync(captain, after, failure, capture, token).ConfigureAwait(false))
                    { await DispatchPendingMissionsAsync(token).ConfigureAwait(false); return; }
                    await HandleTerminalProcessExitFailureAsync(captain, after, after.Id, failure, RuntimeFailureKindEnum.TaskFailure, token,
                        step: BlackBoxStepEnum.StageCheck, check: check, exitCode: 0).ConfigureAwait(false);
                    return;
                }
                _TransientOutageStarts.TryRemove(mission.Id, out _);
                await _Missions.HandleClaimedCompletionAsync(captain, mission.Id, version, token).ConfigureAwait(false);
            }
            finally { _VerificationsInProgress.TryRemove(mission.Id, out _); }
        }
    }
}
