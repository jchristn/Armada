namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Manual mission status change shared by REST, MCP, and WebSocket.
    /// WorkProduced and Complete of a mission that is not read-only land through <see cref="ManualMissionHandoff"/>.
    /// Complete of a claimable mission that still has a dependent voyage stage and no review to pass uses
    /// <see cref="IMissionService.HandleCompletionAsync(Captain, string, CancellationToken)"/> before any WorkProduced write.
    /// A mission that requires review lands instead, because the completion handler would return it to Review.
    /// </summary>
    public static class MissionStatusTransition
    {
        #region Private-Members

        private const string _Header = "[MissionStatusTransition] ";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Apply one manual status change and return the persisted mission.
        /// </summary>
        /// <param name="request">Caller, mission, and callbacks.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored mission, or a refusal.</returns>
        public static async Task<MissionStatusTransitionResult> ApplyAsync(MissionStatusTransitionRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Database == null) throw new ArgumentNullException(nameof(request.Database));
            if (request.Admiral == null) throw new ArgumentNullException(nameof(request.Admiral));

            DatabaseDriver database = request.Database;
            string missionId = request.MissionId ?? "";
            Mission? mission = await ReadMissionAsync(database, request.Caller, missionId, token).ConfigureAwait(false);
            if (mission == null)
                return Refuse(MissionStatusTransitionErrorEnum.NotFound, "Mission not found");

            if (String.IsNullOrEmpty(request.StatusText))
                return Refuse(MissionStatusTransitionErrorEnum.InvalidStatus, "Status is required");

            if (!Enum.TryParse<MissionStatusEnum>(request.StatusText, true, out MissionStatusEnum newStatus))
                return Refuse(MissionStatusTransitionErrorEnum.InvalidStatus, "Invalid status: " + request.StatusText);

            if (!MissionStateMachine.IsValidTransition(mission.Status, newStatus))
                return Refuse(MissionStatusTransitionErrorEnum.InvalidTransition, "Invalid transition from " + mission.Status + " to " + newStatus);

            if (request.RefuseReviewExitExceptCancel
                && mission.Status == MissionStatusEnum.Review
                && newStatus != MissionStatusEnum.Cancelled)
            {
                return Refuse(
                    MissionStatusTransitionErrorEnum.InvalidTransition,
                    "Mission " + missionId + " is waiting at a review gate; use approve_mission_review or deny_mission_review.");
            }

            string? handoffRefusal = ManualMissionHandoff.Refusal(mission, newStatus);
            if (handoffRefusal != null)
                return Refuse(MissionStatusTransitionErrorEnum.Conflict, handoffRefusal);

            MissionStatusEnum previousStatus = mission.Status;
            MissionStatusTransitionCallbacks callbacks = request.Callbacks ?? new MissionStatusTransitionCallbacks();

            if (newStatus == MissionStatusEnum.Complete
                && request.MissionService != null
                && !MissionStateMachine.IsTerminalOrPostWork(mission.Status)
                && !(mission.Status == MissionStatusEnum.Review && mission.RequiresReview))
            {
                bool hasDependent = await HasDependentPipelineStageAsync(database, mission, token).ConfigureAwait(false);
                Captain? captain = null;
                if (hasDependent && !String.IsNullOrEmpty(mission.CaptainId))
                    captain = await database.Captains.ReadAsync(mission.CaptainId, token).ConfigureAwait(false);

                if (hasDependent && captain != null)
                {
                    await request.MissionService.HandleCompletionAsync(captain, mission.Id, token).ConfigureAwait(false);
                    Mission? afterHandoff = await database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                    if (afterHandoff == null)
                        return Refuse(MissionStatusTransitionErrorEnum.NotFound, "Mission not found after completion");

                    // The completion handler returns without a status change when the stage check is unverified, the mission was
                    // already processed, or a concurrent writer won. None of those is a completed request.
                    if (afterHandoff.Status == previousStatus)
                    {
                        return Refuse(
                            MissionStatusTransitionErrorEnum.Conflict,
                            "Mission " + missionId + " stayed in " + previousStatus + ": the completion handler made no transition because its stage check is unverified, it was already processed, or it changed concurrently.");
                    }

                    string handoffText = "Mission " + missionId + " transitioned to " + afterHandoff.Status;
                    await RecordProgressAsync(
                        database,
                        callbacks,
                        afterHandoff,
                        previousStatus,
                        handoffText,
                        handoffText,
                        afterHandoff.Status.ToString(),
                        false,
                        token).ConfigureAwait(false);
                    return Stored(afterHandoff);
                }
            }

            if ((newStatus == MissionStatusEnum.WorkProduced || newStatus == MissionStatusEnum.Complete)
                && !MissionModeContract.IsReadOnly(mission.Mode))
            {
                Mission landed;
                try
                {
                    landed = await ManualMissionHandoff.LandAsync(database, request.Admiral, mission, newStatus, request.Git, request.Settings).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    return Refuse(MissionStatusTransitionErrorEnum.Conflict, ex.Message);
                }

                string landedText = "Mission " + missionId
                    + (newStatus == MissionStatusEnum.Complete ? " manually completed" : " manually marked " + newStatus)
                    + " - landed as " + landed.Status;
                await RecordProgressAsync(
                    database,
                    callbacks,
                    landed,
                    previousStatus,
                    "Mission " + missionId + " transitioned to " + newStatus,
                    landedText,
                    landed.Status.ToString(),
                    false,
                    token).ConfigureAwait(false);
                return Stored(landed);
            }

            mission.Status = newStatus;
            mission.LastUpdateUtc = DateTime.UtcNow;

            if (newStatus == MissionStatusEnum.InProgress && mission.StartedUtc == null)
                mission.StartedUtc = DateTime.UtcNow;

            if (newStatus == MissionStatusEnum.Complete
                || newStatus == MissionStatusEnum.Failed
                || newStatus == MissionStatusEnum.LandingFailed
                || newStatus == MissionStatusEnum.Cancelled)
            {
                mission.CompletedUtc = DateTime.UtcNow;
            }

            await database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

            if (newStatus == MissionStatusEnum.Complete)
            {
                if (callbacks.Logging != null)
                    callbacks.Logging.Warn(_Header + "mission " + missionId + " manually completed without active dock — landing pipeline was skipped");
                await EmitEventAsync(
                    database,
                    callbacks,
                    "mission.manual_complete_no_dock",
                    "Mission " + missionId + " manually marked Complete without an active dock (landing pipeline skipped)",
                    "mission",
                    missionId,
                    mission.CaptainId,
                    missionId,
                    mission.VesselId,
                    mission.VoyageId,
                    token).ConfigureAwait(false);
            }

            string progressText = "Mission " + missionId + " transitioned to " + newStatus;
            await RecordProgressAsync(
                database,
                callbacks,
                mission,
                previousStatus,
                progressText,
                progressText,
                newStatus.ToString(),
                newStatus == MissionStatusEnum.Review,
                token).ConfigureAwait(false);
            return Stored(mission);
        }

        #endregion

        #region Private-Methods

        private static async Task RecordProgressAsync(
            DatabaseDriver database,
            MissionStatusTransitionCallbacks callbacks,
            Mission mission,
            MissionStatusEnum previousStatus,
            string signalText,
            string statusMessage,
            string broadcastStatus,
            bool approval,
            CancellationToken token)
        {
            Signal signal = new Signal(SignalTypeEnum.Progress, signalText);
            if (!String.IsNullOrEmpty(mission.TenantId)) signal.TenantId = mission.TenantId;
            if (!String.IsNullOrEmpty(mission.UserId)) signal.UserId = mission.UserId;
            if (!String.IsNullOrEmpty(mission.CaptainId)) signal.FromCaptainId = mission.CaptainId;
            await database.Signals.CreateAsync(signal, token).ConfigureAwait(false);

            await EmitStatusAsync(database, callbacks, mission, previousStatus, statusMessage, token).ConfigureAwait(false);

            if (callbacks.BroadcastMissionChange != null)
                callbacks.BroadcastMissionChange(mission, broadcastStatus);
            if (approval && callbacks.BroadcastApprovalNeeded != null)
                callbacks.BroadcastApprovalNeeded(mission);
        }

        private static async Task EmitStatusAsync(
            DatabaseDriver database,
            MissionStatusTransitionCallbacks callbacks,
            Mission mission,
            MissionStatusEnum previousStatus,
            string message,
            CancellationToken token)
        {
            if (callbacks.EmitMissionStatusChanged != null)
            {
                await callbacks.EmitMissionStatusChanged(mission, previousStatus, message).ConfigureAwait(false);
                return;
            }

            await EmitEventAsync(
                database,
                callbacks,
                "mission.status_changed",
                message,
                "mission",
                mission.Id,
                mission.CaptainId,
                mission.Id,
                mission.VesselId,
                mission.VoyageId,
                token).ConfigureAwait(false);
        }

        private static async Task EmitEventAsync(
            DatabaseDriver database,
            MissionStatusTransitionCallbacks callbacks,
            string eventType,
            string message,
            string? entityType,
            string? entityId,
            string? captainId,
            string? missionId,
            string? vesselId,
            string? voyageId,
            CancellationToken token)
        {
            if (callbacks.EmitEvent != null)
            {
                await callbacks.EmitEvent(eventType, message, entityType, entityId, captainId, missionId, vesselId, voyageId).ConfigureAwait(false);
                return;
            }

            ArmadaEvent evt = new ArmadaEvent(eventType, message);
            evt.EntityType = entityType;
            evt.EntityId = entityId;
            evt.CaptainId = captainId;
            evt.MissionId = missionId;
            evt.VesselId = vesselId;
            evt.VoyageId = voyageId;
            await database.Events.CreateAsync(evt, token).ConfigureAwait(false);
        }

        private static async Task<bool> HasDependentPipelineStageAsync(DatabaseDriver database, Mission mission, CancellationToken token)
        {
            if (String.IsNullOrEmpty(mission.VoyageId)) return false;

            List<Mission> voyageMissions = await database.Missions.EnumerateByVoyageAsync(mission.VoyageId, token).ConfigureAwait(false);
            foreach (Mission candidate in voyageMissions)
            {
                if (candidate.DependsOnMissionId == mission.Id) return true;
            }

            return false;
        }

        private static async Task<Mission?> ReadMissionAsync(DatabaseDriver database, AuthContext? caller, string missionId, CancellationToken token)
        {
            if (caller == null || caller.IsAdmin)
                return await database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (caller.IsTenantAdmin)
                return await database.Missions.ReadAsync(caller.TenantId!, missionId, token).ConfigureAwait(false);
            return await database.Missions.ReadAsync(caller.TenantId!, caller.UserId!, missionId, token).ConfigureAwait(false);
        }

        private static MissionStatusTransitionResult Stored(Mission mission)
        {
            MissionStatusTransitionResult result = new MissionStatusTransitionResult();
            result.Mission = mission;
            result.Error = MissionStatusTransitionErrorEnum.None;
            return result;
        }

        private static MissionStatusTransitionResult Refuse(MissionStatusTransitionErrorEnum error, string message)
        {
            MissionStatusTransitionResult result = new MissionStatusTransitionResult();
            result.Error = error;
            result.Message = message;
            return result;
        }

        #endregion
    }
}
