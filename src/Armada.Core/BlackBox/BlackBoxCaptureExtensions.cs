namespace Armada.Core.BlackBox
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// One-line capture helpers for failure sites. Call after the failing mission (or voyage) update succeeded:
    /// <code>await _BlackBox.RecordMissionFailureAsync(mission, statusBefore, BlackBoxStepEnum.ScopeViolation, token: token).ConfigureAwait(false);</code>
    /// A null capture (black box not wired) is a no-op. The caller's member, file and line are recorded as the failure
    /// site. Never throws.
    /// </summary>
    public static class BlackBoxCaptureExtensions
    {
        /// <summary>
        /// Capture a mission failure.
        /// </summary>
        /// <param name="capture">Capture service, or null (no-op).</param>
        /// <param name="mission">Mission after the failure write.</param>
        /// <param name="statusBefore">Status before the failure write.</param>
        /// <param name="step">Failure step.</param>
        /// <param name="configure">Fills more of the context (git facts, exception, command), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="callerMember">Filled by the compiler.</param>
        /// <param name="callerFile">Filled by the compiler.</param>
        /// <param name="callerLine">Filled by the compiler.</param>
        /// <returns>The entry id, or null.</returns>
        public static async Task<string?> RecordMissionFailureAsync(
            this IBlackBoxCapture? capture,
            Mission mission,
            MissionStatusEnum? statusBefore,
            BlackBoxStepEnum step,
            Action<BlackBoxCaptureContext>? configure = null,
            CancellationToken token = default,
            [CallerMemberName] string callerMember = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
        {
            if (capture == null || mission == null) return null;
            try
            {
                BlackBoxCaptureContext context = new BlackBoxCaptureContext
                {
                    Mission = mission,
                    StatusBefore = statusBefore,
                    Step = step,
                    CallerMember = callerMember,
                    CallerFile = callerFile,
                    CallerLine = callerLine
                };
                configure?.Invoke(context);
                return await capture.CaptureMissionFailureAsync(context, token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Capture a voyage failure.
        /// </summary>
        /// <param name="capture">Capture service, or null (no-op).</param>
        /// <param name="voyage">Voyage after the failure write.</param>
        /// <param name="statusBefore">Status before the failure write.</param>
        /// <param name="step">Failure step (a Voyage phase step).</param>
        /// <param name="reason">Why, or null.</param>
        /// <param name="configure">Fills more of the context, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="callerMember">Filled by the compiler.</param>
        /// <param name="callerFile">Filled by the compiler.</param>
        /// <param name="callerLine">Filled by the compiler.</param>
        /// <returns>The entry id, or null.</returns>
        public static async Task<string?> RecordVoyageFailureAsync(
            this IBlackBoxCapture? capture,
            Voyage voyage,
            VoyageStatusEnum? statusBefore,
            BlackBoxStepEnum step,
            string? reason = null,
            Action<BlackBoxVoyageCaptureContext>? configure = null,
            CancellationToken token = default,
            [CallerMemberName] string callerMember = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
        {
            if (capture == null || voyage == null) return null;
            try
            {
                BlackBoxVoyageCaptureContext context = new BlackBoxVoyageCaptureContext
                {
                    Voyage = voyage,
                    StatusBefore = statusBefore,
                    Step = step,
                    Reason = reason,
                    CallerMember = callerMember,
                    CallerFile = callerFile,
                    CallerLine = callerLine
                };
                configure?.Invoke(context);
                return await capture.CaptureVoyageFailureAsync(context, token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
