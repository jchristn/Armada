namespace Armada.Server
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Black box capture for the operator actions the REST, MCP and WebSocket surfaces perform on missions: a manual
    /// status transition to a failed, cancelled or blocked state (step OperatorTransition) and the cancellation of a
    /// vessel's active missions when the vessel is deleted (step VesselDeleted). Both steps are in the External phase, so
    /// the capture triages them External. The failure site recorded is the surface's caller, not this helper. Capture
    /// never throws and is a no-op when the black box is not wired.
    /// </summary>
    public static class BlackBoxOperatorCapture
    {
        #region Public-Methods

        /// <summary>
        /// Whether a manual transition to <paramref name="status"/> is captured: Failed, LandingFailed, Cancelled and
        /// Blocked.
        /// </summary>
        /// <param name="status">Target status.</param>
        /// <returns>True when captured.</returns>
        public static bool CapturesTransitionTo(MissionStatusEnum status)
        {
            return status == MissionStatusEnum.Failed
                || status == MissionStatusEnum.LandingFailed
                || status == MissionStatusEnum.Cancelled
                || status == MissionStatusEnum.Blocked;
        }

        /// <summary>
        /// Whether <see cref="RecordTransitionAsync"/> emits <c>mission.status_changed</c> for a transition to
        /// <paramref name="status"/>: the capture emits it (with PreviousStatus) for every transition it captures, so a
        /// surface that also emits the event must skip it when this is true, or one transition produces two events.
        /// </summary>
        /// <param name="capture">Capture service, or null.</param>
        /// <param name="status">Target status.</param>
        /// <returns>True when the capture emits the event.</returns>
        public static bool EmitsStatusChanged(IBlackBoxCapture? capture, MissionStatusEnum status)
        {
            return capture != null && CapturesTransitionTo(status);
        }

        /// <summary>
        /// Capture a manual status transition (call after the mission row is written). Does nothing for a target status
        /// <see cref="CapturesTransitionTo"/> does not capture. The capture emits <c>mission.status_changed</c>; see
        /// <see cref="EmitsStatusChanged"/>.
        /// </summary>
        /// <param name="capture">Capture service, or null (no-op).</param>
        /// <param name="mission">Mission after the transition.</param>
        /// <param name="statusBefore">Status before the transition.</param>
        /// <param name="surface">Surface that made the transition (REST, MCP or WebSocket).</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="callerMember">Filled by the compiler.</param>
        /// <param name="callerFile">Filled by the compiler.</param>
        /// <param name="callerLine">Filled by the compiler.</param>
        /// <returns>The entry id, or null.</returns>
        public static Task<string?> RecordTransitionAsync(
            IBlackBoxCapture? capture,
            Mission mission,
            MissionStatusEnum statusBefore,
            string surface,
            CancellationToken token = default,
            [CallerMemberName] string callerMember = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
        {
            if (capture == null || mission == null || !CapturesTransitionTo(mission.Status)) return Task.FromResult<string?>(null);
            string note = "Operator transitioned the mission from " + statusBefore + " to " + mission.Status + " through " + surface + ".";
            return capture.RecordMissionFailureAsync(mission, statusBefore, BlackBoxStepEnum.OperatorTransition, context =>
            {
                context.Note = note;
                SetSite(context, callerMember, callerFile, callerLine);
            }, token);
        }

        /// <summary>
        /// Capture the cancellation of an active mission because its vessel is being deleted (call after the mission row
        /// is written and before it is removed). The entry outlives the mission row; it is removed with the vessel only
        /// when blackBox.retention.deleteWithVessel cascades it.
        /// </summary>
        /// <param name="capture">Capture service, or null (no-op).</param>
        /// <param name="mission">Mission after the cancellation.</param>
        /// <param name="statusBefore">Status before the cancellation.</param>
        /// <param name="vessel">The vessel being deleted, or null.</param>
        /// <param name="surface">Surface that deleted the vessel (REST, MCP or WebSocket).</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="callerMember">Filled by the compiler.</param>
        /// <param name="callerFile">Filled by the compiler.</param>
        /// <param name="callerLine">Filled by the compiler.</param>
        /// <returns>The entry id, or null.</returns>
        public static Task<string?> RecordVesselDeletedAsync(
            IBlackBoxCapture? capture,
            Mission mission,
            MissionStatusEnum statusBefore,
            Vessel? vessel,
            string surface,
            CancellationToken token = default,
            [CallerMemberName] string callerMember = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
        {
            if (capture == null || mission == null) return Task.FromResult<string?>(null);
            string note = "Vessel " + (vessel?.Name ?? mission.VesselId ?? "(unknown)") + " was deleted through " + surface + " while the mission was " + statusBefore + ".";
            return capture.RecordMissionFailureAsync(mission, statusBefore, BlackBoxStepEnum.VesselDeleted, context =>
            {
                context.Vessel = vessel;
                context.Note = note;
                SetSite(context, callerMember, callerFile, callerLine);
            }, token);
        }

        #endregion

        #region Private-Methods

        private static void SetSite(BlackBoxCaptureContext context, string callerMember, string callerFile, int callerLine)
        {
            context.CallerMember = callerMember;
            context.CallerFile = callerFile;
            context.CallerLine = callerLine;
        }

        #endregion
    }
}
