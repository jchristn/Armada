namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Text.Json;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Captains that are unreachable (their Harbor lost the link) or whose stop was not confirmed. An unreachable captain
    /// may still be working, so missing output is not judged a stall while the link is down (for up to
    /// HarborDisconnectGraceMinutes). A stop that cannot be confirmed is recorded once and retried with backoff instead of
    /// on every health check, and is delivered again when the Harbor reconnects. Each episode is recorded by typed events
    /// (captain.unreachable, captain.stop_unconfirmed, captain.stop_redelivered, captain.reconciled) from which it is
    /// rebuilt after an Admiral restart.
    /// </summary>
    public partial class AdmiralService
    {
        #region Public-Members

        /// <summary>
        /// The Harbor link manager, or null when no Harbor can host captains. The health check reads which Harbor runs a
        /// captain's process and whether that Harbor is linked, so a captain on a disconnected Harbor is held as unreachable
        /// rather than judged stalled, and reconciled when the link returns.
        /// </summary>
        public HarborConnectionManager? HarborLinks { get; set; } = null;

        #endregion

        #region Private-Members

        private const string _UnconfirmedStopReason = "Recovery deferred: the previous job has not confirmed its exit. No replacement was launched. Remediation: reconnect the owning Harbor and confirm the job has stopped before retrying.";
        private const string _StallRemediation = "Remediation: inspect provider/tool progress and pending background work; correct blocked configuration or job lifetime before restarting.";
        private const int _MaxStopRetryDelayMinutes = 15;
        private static readonly TimeSpan _ReconnectSettle = TimeSpan.FromSeconds(30);
        private readonly ConcurrentDictionary<string, CaptainLinkEpisode> _LinkEpisodes = new ConcurrentDictionary<string, CaptainLinkEpisode>(StringComparer.Ordinal);

        #endregion

        #region Private-Methods

        private DateTime UtcNow()
        {
            return _Time.GetUtcNow().UtcDateTime;
        }

        private string? ProcessHarbor(int? processId)
        {
            if (HarborLinks == null || !processId.HasValue) return null;
            return HarborLinks.TryGetHarborForProcess(processId.Value, out string? harborId) ? harborId : null;
        }

        private static int NextRetryDelayMinutes(CaptainLinkEpisode episode)
        {
            if (!episode.StopPending || episode.RetryDelayMinutes < 1) return 1;
            return Math.Min(episode.RetryDelayMinutes * 2, _MaxStopRetryDelayMinutes);
        }

        /// <summary>
        /// Run the unreachable and stop-pending part of a live captain's health check. Returns true when the health check
        /// must stop here (the captain is held as unreachable, is waiting on an unconfirmed stop, or was reconciled on this
        /// tick); false lets the max-runtime and stall checks run.
        /// </summary>
        private async Task<bool> CheckLinkEpisodeAsync(Captain captain, Mission? mission, int processId, CancellationToken token)
        {
            DateTime nowUtc = UtcNow();
            CaptainLinkEpisode episode = await LoadEpisodeAsync(captain, mission, processId, token).ConfigureAwait(false);
            string? harborId = ProcessHarbor(processId);

            if (harborId != null && HarborLinks!.GetLinkState(harborId) == HarborLinkStateEnum.NotLinked)
            {
                if (await HoldUnreachableCaptainAsync(captain, mission, harborId, processId, episode, nowUtc, token).ConfigureAwait(false)) return true;
            }
            else if (harborId != null && episode.IsOpen)
            {
                if (await ReconcileAfterReconnectAsync(captain, mission, harborId, processId, episode, nowUtc, token).ConfigureAwait(false)) return true;
            }

            if (!episode.StopPending) return false;

            // The captain reported activity after its stop was requested: it is working, so the pending stop is cancelled.
            if (captain.LastHeartbeatUtc.HasValue && captain.LastHeartbeatUtc.Value > episode.StopRequestedUtc!.Value)
            {
                _Logging.Info(_Header + "captain " + captain.Id + " reported activity after its unconfirmed stop; cancelling the pending stop");
                await CloseEpisodeAsync(captain, mission, episode, CaptainReconcileOutcomeEnum.Resumed, null, token).ConfigureAwait(false);
                return false;
            }

            // Waiting on the unconfirmed stop: no recovery is re-entered until the backoff elapses.
            return nowUtc < episode.NextStopRetryUtc;
        }

        private async Task<bool> HoldUnreachableCaptainAsync(Captain captain, Mission? mission, string harborId, int processId, CaptainLinkEpisode episode, DateTime nowUtc, CancellationToken token)
        {
            int grace = _Settings.HarborDisconnectGraceMinutes;
            if (!episode.UnreachableSinceUtc.HasValue)
            {
                DateTime since = nowUtc;
                try
                {
                    Harbor? harbor = await _Database.Harbors.ReadAsync(harborId, token).ConfigureAwait(false);
                    if (harbor?.LastSeenUtc != null && harbor.LastSeenUtc.Value < nowUtc) since = harbor.LastSeenUtc.Value;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    _Logging.Debug(_Header + "could not read Harbor " + harborId + ": " + ex.Message);
                }

                episode.HarborId = harborId;
                episode.ProcessId = processId;
                episode.UnreachableSinceUtc = nowUtc;
                episode.Escalated = false;
                CaptainUnreachablePayload payload = new CaptainUnreachablePayload
                {
                    CaptainId = captain.Id,
                    MissionId = mission?.Id,
                    HarborId = harborId,
                    ProcessId = processId,
                    Since = since,
                    DetectedUtc = nowUtc,
                    LastActivityUtc = captain.LastHeartbeatUtc,
                    GraceMinutes = grace
                };
                string message = "Captain " + captain.Name + " is unreachable: Harbor " + harborId + " has not been linked since "
                    + since.ToString("u") + ", so the captain may still be working there. It is not judged stalled and no recovery runs while the link is down"
                    + (grace > 0
                        ? " (for up to " + grace + " min, HarborDisconnectGraceMinutes); after that it is handed to stall recovery."
                        : "; HarborDisconnectGraceMinutes is 0, so it is judged by the stall threshold now.")
                    + " Monitoring resumes when the Harbor reconnects.";
                _Logging.Warn(_Header + message);
                await EmitEpisodeEventAsync("captain.unreachable", message, captain, mission, payload, token).ConfigureAwait(false);
            }

            if (grace > 0 && nowUtc - episode.UnreachableSinceUtc.Value < TimeSpan.FromMinutes(grace)) return true;
            if (!episode.Escalated)
            {
                episode.Escalated = true;
                _Logging.Warn(_Header + "captain " + captain.Id + " has been unreachable for " + (int)(nowUtc - episode.UnreachableSinceUtc.Value).TotalMinutes
                    + " min (HarborDisconnectGraceMinutes " + grace + "); handing it to stall recovery");
            }

            return false;
        }

        private async Task<bool> ReconcileAfterReconnectAsync(Captain captain, Mission? mission, string harborId, int processId, CaptainLinkEpisode episode, DateTime nowUtc, CancellationToken token)
        {
            HarborConnectionManager links = HarborLinks!;
            DateTime linkedSince = links.GetLinkedSinceUtc(harborId) ?? nowUtc;
            DateTime episodeStart = episode.UnreachableSinceUtc.HasValue && episode.StopRequestedUtc.HasValue
                ? (episode.UnreachableSinceUtc.Value < episode.StopRequestedUtc.Value ? episode.UnreachableSinceUtc.Value : episode.StopRequestedUtc.Value)
                : (episode.UnreachableSinceUtc ?? episode.StopRequestedUtc!.Value);

            // The link was up for the whole episode (a stop the Harbor did not confirm in time): the backoff applies.
            if (!episode.UnreachableSinceUtc.HasValue && linkedSince <= episodeStart) return false;

            // Let the Harbor replay its job outbox (which may carry the job's exit) and report what still runs first.
            if (!links.HasReportedSinceLink(harborId) && nowUtc - linkedSince < _ReconnectSettle) return true;

            // The job ended while the link was down: its replayed exit (or the vanished-job report) is applied through
            // the normal exit path, which closes the episode.
            if (links.IsProcessLive(processId) != true) return true;

            if (episode.StopPending)
            {
                if (captain.LastHeartbeatUtc.HasValue && captain.LastHeartbeatUtc.Value > episode.StopRequestedUtc!.Value)
                {
                    _Logging.Info(_Header + "captain " + captain.Id + " is still working on reconnected Harbor " + harborId + "; cancelling its pending stop");
                    await ResetStallClockAsync(captain, linkedSince, token).ConfigureAwait(false);
                    await CloseEpisodeAsync(captain, mission, episode, CaptainReconcileOutcomeEnum.Resumed, linkedSince, token).ConfigureAwait(false);
                    return true;
                }

                // Still running and silent since the stop was requested: it is stuck, so the stop is delivered now.
                bool confirmed = false;
                string? error = null;
                try
                {
                    if (_Captains.OnStopAgent != null)
                    {
                        await _Captains.OnStopAgent.Invoke(captain).ConfigureAwait(false);
                        confirmed = true;
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    error = ex.Message;
                }

                CaptainStopRedeliveredPayload redelivered = new CaptainStopRedeliveredPayload
                {
                    CaptainId = captain.Id,
                    MissionId = mission?.Id,
                    HarborId = harborId,
                    ProcessId = processId,
                    StopRequestedUtc = episode.StopRequestedUtc!.Value,
                    ReconnectedUtc = linkedSince,
                    Confirmed = confirmed
                };
                string message = "Re-sent the pending stop of captain " + captain.Name + " after Harbor " + harborId + " reconnected at " + linkedSince.ToString("u")
                    + ": the job was still running with no activity since the stop was requested at " + episode.StopRequestedUtc.Value.ToString("u") + ". "
                    + (confirmed ? "The Harbor confirmed the stop; recovery continues." : "The stop is still not confirmed (" + (error ?? "no stop handler") + "); retrying with backoff.");
                await EmitEpisodeEventAsync("captain.stop_redelivered", message, captain, mission, redelivered, token).ConfigureAwait(false);

                if (!confirmed)
                {
                    episode.RetryDelayMinutes = NextRetryDelayMinutes(episode);
                    episode.NextStopRetryUtc = nowUtc.AddMinutes(episode.RetryDelayMinutes);
                    return true;
                }

                CaptainStopPurposeEnum purpose = episode.Purpose;
                await CloseEpisodeAsync(captain, mission, episode, CaptainReconcileOutcomeEnum.Stopped, linkedSince, token).ConfigureAwait(false);
                await ContinueAfterConfirmedStopAsync(captain, mission, purpose, token).ConfigureAwait(false);
                return true;
            }

            // Unreachable only: the captain was working through the outage, so monitoring resumes with the stall clock
            // restarted at the reconnect (activity reported after it counts; the outage does not).
            _Logging.Info(_Header + "Harbor " + harborId + " reconnected; resuming stall monitoring of captain " + captain.Id + " from " + linkedSince.ToString("o"));
            await ResetStallClockAsync(captain, linkedSince, token).ConfigureAwait(false);
            await CloseEpisodeAsync(captain, mission, episode, CaptainReconcileOutcomeEnum.Resumed, linkedSince, token).ConfigureAwait(false);
            return true;
        }

        private async Task ResetStallClockAsync(Captain captain, DateTime linkedSince, CancellationToken token)
        {
            if (captain.LastHeartbeatUtc.HasValue && captain.LastHeartbeatUtc.Value >= linkedSince) return;
            captain.LastHeartbeatUtc = linkedSince;
            captain.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
        }

        private async Task ContinueAfterConfirmedStopAsync(Captain captain, Mission? mission, CaptainStopPurposeEnum purpose, CancellationToken token)
        {
            if (purpose == CaptainStopPurposeEnum.MaxRuntime)
            {
                if (mission != null) await FailMaxRuntimeAsync(captain, mission, token).ConfigureAwait(false);
                return;
            }

            if (purpose == CaptainStopPurposeEnum.Recovery && captain.RecoveryAttempts > 0)
            {
                // The attempt was counted when its stop could not be confirmed; TryRecoverAsync counts it again as it
                // relaunches, so it is handed the count from before that attempt.
                captain.RecoveryAttempts--;
                await _Captains.TryRecoverAsync(captain, token).ConfigureAwait(false);
                return;
            }

            DateTime? heartbeat = captain.LastHeartbeatUtc ?? mission?.StartedUtc;
            double idle = heartbeat.HasValue ? (UtcNow() - heartbeat.Value).TotalMinutes : 0;
            await FailStallRecoveryExhaustedAsync(captain, mission, StallReason(idle), heartbeat, token).ConfigureAwait(false);
        }

        private string StallReason(double idleMinutes)
        {
            return "No activity for " + idleMinutes.ToString("F1") + " min (configured stall threshold: " + _Settings.StallThresholdMinutes + " min).";
        }

        /// <summary>
        /// Record a stop the owning host did not confirm. Ownership is retained (the mission keeps its status and no
        /// replacement is launched). The first time in an episode the mission is marked, the black box records it and one
        /// captain.stop_unconfirmed event is written; later failures only extend the backoff (1, 2, 4 ... 15 minutes).
        /// </summary>
        private async Task RecordUnconfirmedStopAsync(Captain captain, Mission? mission, CaptainStopPurposeEnum purpose, Exception? error, CancellationToken token)
        {
            DateTime nowUtc = UtcNow();
            int? processId = captain.ProcessId ?? mission?.ProcessId;
            CaptainLinkEpisode episode = await LoadEpisodeAsync(captain, mission, processId, token).ConfigureAwait(false);
            string? harborId = ProcessHarbor(processId) ?? episode.HarborId;
            bool harborDown = harborId != null && HarborLinks != null && HarborLinks.GetLinkState(harborId) == HarborLinkStateEnum.NotLinked;

            if (!episode.StopPending) episode.RecoveryAttemptsBefore = captain.RecoveryAttempts;
            if (purpose == CaptainStopPurposeEnum.Recovery)
            {
                // A real recovery attempt was made, so it counts even though the stop could not be confirmed.
                captain.RecoveryAttempts++;
                captain.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
            }

            bool first = !episode.StopPending;
            int delay = NextRetryDelayMinutes(episode);
            if (first) episode.StopRequestedUtc = nowUtc;
            episode.Purpose = purpose;
            episode.HarborId = harborId;
            episode.ProcessId = processId;
            episode.RetryDelayMinutes = delay;
            episode.NextStopRetryUtc = nowUtc.AddMinutes(delay);

            _Logging.Warn(_Header + "stop of captain " + captain.Id + " not confirmed" + (error != null ? " (" + error.Message + ")" : "")
                + "; recovery waits " + delay + " min" + (harborId != null ? " or until Harbor " + harborId + " reconnects" : ""));
            if (!first) return;

            if (mission != null)
            {
                // The owning host has not confirmed the job stopped: an environment problem, not the work.
                bool alreadyRecorded = String.Equals(mission.FailureReason, _UnconfirmedStopReason, StringComparison.Ordinal);
                mission.FailureKind = MissionFailureKindEnum.Infra;
                mission.FailureReason = _UnconfirmedStopReason;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                // Non-terminal (the mission keeps its status); recorded once per episode, not on every health check.
                if (!alreadyRecorded)
                    await BlackBox.RecordMissionFailureAsync(mission, mission.Status, BlackBoxStepEnum.UnconfirmedStop, context =>
                    {
                        context.Captain = captain;
                        context.Exception = error;
                        if (harborDown) context.Note = "Harbor " + harborId + " was not linked when the stop was requested; the captain may still be working.";
                    }, token).ConfigureAwait(false);
            }

            CaptainStopUnconfirmedPayload payload = new CaptainStopUnconfirmedPayload
            {
                CaptainId = captain.Id,
                MissionId = mission?.Id,
                HarborId = harborId,
                ProcessId = processId,
                Purpose = purpose,
                Attempt = captain.RecoveryAttempts,
                MaxAttempts = _Settings.MaxRecoveryAttempts,
                StopRequestedUtc = nowUtc,
                RetryDelayMinutes = delay,
                NextRetryUtc = episode.NextStopRetryUtc,
                Reason = error?.Message
            };
            string step = purpose == CaptainStopPurposeEnum.Recovery
                ? "Automatic recovery " + captain.RecoveryAttempts + " of " + _Settings.MaxRecoveryAttempts + " is waiting"
                : (purpose == CaptainStopPurposeEnum.MaxRuntime ? "Failing the runaway mission is waiting" : "Failing the mission after exhausted stall recovery is waiting");
            string message = "Recovery is waiting: the previous job has not confirmed its exit"
                + (harborDown ? " (Harbor " + harborId + " is not linked; the captain may still be working)" : String.Empty)
                + ". No replacement was launched. " + step + ": "
                + (harborId != null ? "the stop is re-sent when the Harbor reconnects, or " : "the stop is ")
                + "retried at " + episode.NextStopRetryUtc.ToString("u") + " (backoff " + delay + " min, doubling up to " + _MaxStopRetryDelayMinutes + "). "
                + "Remediation: reconnect the owning Harbor and confirm the job has stopped before retrying.";
            await EmitEpisodeEventAsync("captain.stop_unconfirmed", message, captain, mission, payload, token).ConfigureAwait(false);
        }

        private async Task CloseEpisodeAsync(Captain captain, Mission? mission, CaptainLinkEpisode episode, CaptainReconcileOutcomeEnum outcome, DateTime? reconnectedUtc, CancellationToken token)
        {
            if (!episode.IsOpen) return;
            CaptainReconciledPayload payload = new CaptainReconciledPayload
            {
                CaptainId = captain.Id,
                MissionId = episode.MissionId ?? mission?.Id,
                HarborId = episode.HarborId,
                ProcessId = episode.ProcessId,
                Outcome = outcome,
                UnreachableSinceUtc = episode.UnreachableSinceUtc,
                StopRequestedUtc = episode.StopRequestedUtc,
                ReconnectedUtc = reconnectedUtc,
                LastActivityUtc = captain.LastHeartbeatUtc
            };
            string message = "Captain " + captain.Name + " reconciled (" + outcome + "): " + DescribeOutcome(outcome, episode, reconnectedUtc);
            if (outcome == CaptainReconcileOutcomeEnum.Resumed && episode.StopPending && episode.RecoveryAttemptsBefore.HasValue
                && captain.RecoveryAttempts > episode.RecoveryAttemptsBefore.Value)
            {
                // The attempts spent waiting on the stop never relaunched anything: the working captain gets them back.
                captain.RecoveryAttempts = episode.RecoveryAttemptsBefore.Value;
                captain.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
            }
            episode.UnreachableSinceUtc = null;
            episode.StopRequestedUtc = null;
            episode.RecoveryAttemptsBefore = null;
            episode.Escalated = false;
            episode.RetryDelayMinutes = 0;
            episode.NextStopRetryUtc = DateTime.MinValue;
            _Logging.Info(_Header + message);

            if (mission != null && String.Equals(mission.Id, payload.MissionId, StringComparison.Ordinal)
                && String.Equals(mission.FailureReason, _UnconfirmedStopReason, StringComparison.Ordinal))
            {
                // The unconfirmed stop no longer describes the mission.
                mission.FailureReason = null;
                mission.FailureKind = null;
                mission.LastUpdateUtc = DateTime.UtcNow;
                try
                {
                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    _Logging.Warn(_Header + "could not clear the unconfirmed stop of mission " + mission.Id + ": " + ex.Message);
                }
            }

            await EmitEpisodeEventAsync("captain.reconciled", message, captain, mission != null && String.Equals(mission.Id, payload.MissionId, StringComparison.Ordinal) ? mission : null, payload, token, payload.MissionId).ConfigureAwait(false);
        }

        private static string DescribeOutcome(CaptainReconcileOutcomeEnum outcome, CaptainLinkEpisode episode, DateTime? reconnectedUtc)
        {
            string harbor = episode.HarborId != null ? "Harbor " + episode.HarborId : "the owning host";
            string back = reconnectedUtc.HasValue ? " after " + harbor + " reconnected at " + reconnectedUtc.Value.ToString("u") : String.Empty;
            switch (outcome)
            {
                case CaptainReconcileOutcomeEnum.Resumed:
                    return "still working" + back + "; " + (episode.StopPending ? "the pending stop was cancelled and " : String.Empty) + "stall monitoring resumed from the reconnect.";
                case CaptainReconcileOutcomeEnum.Stopped:
                    return "the pending stop was delivered" + back + " and confirmed; recovery continues.";
                case CaptainReconcileOutcomeEnum.Exited:
                    return "its process exit was reported" + back + " and applied through the normal exit path.";
                default:
                    return "it moved on to another mission or process, so the episode no longer applies.";
            }
        }

        /// <summary>
        /// Close the episode of a captain whose process exit is being handled: the exit settles it.
        /// </summary>
        private async Task CloseEpisodeOnExitAsync(string captainId, string missionId, int processId, CancellationToken token)
        {
            try
            {
                CaptainLinkEpisode? episode;
                if (!_LinkEpisodes.TryGetValue(captainId, out episode))
                {
                    episode = await RehydrateEpisodeAsync(captainId, missionId, processId, token).ConfigureAwait(false);
                    _LinkEpisodes[captainId] = episode;
                }

                if (episode == null || !episode.IsOpen || !String.Equals(episode.MissionId, missionId, StringComparison.Ordinal)) return;
                Captain? captain = await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
                Mission? mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
                if (captain == null) return;
                string? harborId = episode.HarborId;
                DateTime? reconnected = harborId != null ? HarborLinks?.GetLinkedSinceUtc(harborId) : null;
                await CloseEpisodeAsync(captain, mission, episode, CaptainReconcileOutcomeEnum.Exited, reconnected, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not reconcile the exit of captain " + captainId + ": " + ex.Message);
            }
        }

        private async Task<CaptainLinkEpisode> LoadEpisodeAsync(Captain captain, Mission? mission, int? processId, CancellationToken token)
        {
            string? missionId = mission?.Id ?? captain.CurrentMissionId;
            if (_LinkEpisodes.TryGetValue(captain.Id, out CaptainLinkEpisode? cached))
            {
                if (String.Equals(cached.MissionId, missionId, StringComparison.Ordinal) && (cached.ProcessId == processId || !cached.IsOpen))
                {
                    cached.ProcessId = processId;
                    return cached;
                }

                // The captain moved on (another mission or a relaunched process): its old episode no longer applies.
                await CloseEpisodeAsync(captain, String.Equals(mission?.Id, cached.MissionId, StringComparison.Ordinal) ? mission : null, cached, CaptainReconcileOutcomeEnum.Superseded, null, token).ConfigureAwait(false);
                _LinkEpisodes.TryRemove(captain.Id, out CaptainLinkEpisode? _);
            }

            CaptainLinkEpisode episode = await RehydrateEpisodeAsync(captain.Id, missionId, processId, token).ConfigureAwait(false);
            if (episode.IsOpen && episode.ProcessId.HasValue && processId.HasValue && episode.ProcessId.Value != processId.Value)
            {
                await CloseEpisodeAsync(captain, mission, episode, CaptainReconcileOutcomeEnum.Superseded, null, token).ConfigureAwait(false);
            }

            episode.ProcessId = processId;
            _LinkEpisodes[captain.Id] = episode;
            return episode;
        }

        private async Task<CaptainLinkEpisode> RehydrateEpisodeAsync(string captainId, string? missionId, int? processId, CancellationToken token)
        {
            CaptainLinkEpisode episode = new CaptainLinkEpisode { CaptainId = captainId, MissionId = missionId, ProcessId = processId };
            if (String.IsNullOrEmpty(missionId)) return episode;

            ArmadaEvent? reconciled = await LatestCaptainEventAsync(captainId, missionId!, "captain.reconciled", token).ConfigureAwait(false);
            DateTime closedAt = reconciled?.CreatedUtc ?? DateTime.MinValue;

            ArmadaEvent? unreachable = await LatestCaptainEventAsync(captainId, missionId!, "captain.unreachable", token).ConfigureAwait(false);
            if (unreachable != null && unreachable.CreatedUtc > closedAt)
            {
                CaptainUnreachablePayload? payload = ReadPayload<CaptainUnreachablePayload>(unreachable);
                episode.UnreachableSinceUtc = payload?.DetectedUtc ?? unreachable.CreatedUtc;
                episode.HarborId = payload?.HarborId;
                episode.ProcessId = payload?.ProcessId ?? processId;
            }

            ArmadaEvent? stop = await LatestCaptainEventAsync(captainId, missionId!, "captain.stop_unconfirmed", token).ConfigureAwait(false);
            if (stop != null && stop.CreatedUtc > closedAt)
            {
                CaptainStopUnconfirmedPayload? payload = ReadPayload<CaptainStopUnconfirmedPayload>(stop);
                episode.StopRequestedUtc = payload?.StopRequestedUtc ?? stop.CreatedUtc;
                episode.Purpose = payload?.Purpose ?? CaptainStopPurposeEnum.Recovery;
                if (payload != null) episode.RecoveryAttemptsBefore = payload.Purpose == CaptainStopPurposeEnum.Recovery ? Math.Max(0, payload.Attempt - 1) : payload.Attempt;
                episode.RetryDelayMinutes = payload?.RetryDelayMinutes ?? 1;
                episode.NextStopRetryUtc = payload?.NextRetryUtc ?? stop.CreatedUtc.AddMinutes(1);
                episode.HarborId = payload?.HarborId ?? episode.HarborId;
                if (payload?.ProcessId != null) episode.ProcessId = payload.ProcessId;

                // A later attempt (a captain.stalled retry while the stop waited) carries the backoff now in effect.
                ArmadaEvent? stalled = await LatestCaptainEventAsync(captainId, missionId!, "captain.stalled", token).ConfigureAwait(false);
                CaptainStalledPayload? retry = stalled != null && stalled.CreatedUtc > stop.CreatedUtc ? ReadPayload<CaptainStalledPayload>(stalled) : null;
                if (retry != null && retry.WaitingOnStop)
                {
                    episode.RetryDelayMinutes = retry.RetryDelayMinutes;
                    episode.NextStopRetryUtc = retry.NextRetryUtc;
                }
            }

            return episode;
        }

        private async Task<ArmadaEvent?> LatestCaptainEventAsync(string captainId, string missionId, string eventType, CancellationToken token)
        {
            EnumerationResult<ArmadaEvent> events = await _Database.Events.EnumerateAsync(new EnumerationQuery
            {
                MissionId = missionId,
                EventType = eventType,
                PageSize = 5,
                Order = EnumerationOrderEnum.CreatedDescending
            }, token).ConfigureAwait(false);
            return events.Objects.FirstOrDefault(e => String.IsNullOrEmpty(e.CaptainId) || String.Equals(e.CaptainId, captainId, StringComparison.Ordinal));
        }

        private T? ReadPayload<T>(ArmadaEvent evt) where T : class
        {
            if (String.IsNullOrWhiteSpace(evt.Payload)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(evt.Payload!);
            }
            catch (JsonException ex)
            {
                _Logging.Debug(_Header + "could not read the payload of event " + evt.Id + ": " + ex.Message);
                return null;
            }
        }

        private async Task EmitEpisodeEventAsync(string eventType, string message, Captain captain, Mission? mission, object payload, CancellationToken token, string? missionId = null)
        {
            await EmitEventAsync(eventType, message, entityType: "captain", entityId: captain.Id, captainId: captain.Id,
                missionId: mission?.Id ?? missionId, vesselId: mission?.VesselId, voyageId: mission?.VoyageId,
                token: token, tenantId: mission?.TenantId ?? captain.TenantId, userId: mission?.UserId ?? captain.UserId,
                payload: JsonSerializer.Serialize(payload, payload.GetType())).ConfigureAwait(false);
        }

        #endregion
    }
}
