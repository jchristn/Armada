namespace Armada.Core.Services
{
    using System.Collections.Concurrent;
    using System.Runtime.CompilerServices;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Black box capture for the failure writes MissionService owns (dispatch, provision, completion, review, handoff).
    /// Each site updates the mission row first and then records the failure here; the capture never throws.
    /// </summary>
    public partial class MissionService
    {
        #region Private-Members

        private readonly ConcurrentDictionary<string, string> _ProvisionFailureCaptures = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Private-Methods

        private Task<string?> RecordFailureAsync(
            Mission mission,
            MissionStatusEnum? statusBefore,
            BlackBoxStepEnum step,
            Dock? dock = null,
            Action<BlackBoxCaptureContext>? configure = null,
            CancellationToken token = default,
            [CallerMemberName] string callerMember = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
        {
            return BlackBox.RecordMissionFailureAsync(mission, statusBefore, step, context =>
            {
                if (dock != null && context.Dock == null) context.Dock = dock;
                configure?.Invoke(context);
            }, token, callerMember, callerFile, callerLine);
        }

        private async Task RecordProvisionFailureAsync(Mission mission, Captain captain, string? harborId, Exception? exception, CancellationToken token,
            [CallerMemberName] string callerMember = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
        {
            // Provisioning failures send the mission back to Pending and are retried every dispatch cycle: record one entry
            // per mission and distinct cause, not one per retry.
            DockProvisionFailure? failure = _Docks.TakeProvisionFailure(mission.Id);
            Exception? cause = exception ?? failure?.Exception;
            BlackBoxStepEnum step = failure?.Step ?? BlackBoxStepEnum.DockProvision;
            string signature = step + "|" + (harborId ?? "") + "|" + (cause?.GetType().Name ?? "") + "|" + (cause?.Message ?? "");
            if (_ProvisionFailureCaptures.TryGetValue(mission.Id, out string? previous) && String.Equals(previous, signature, StringComparison.Ordinal)) return;
            _ProvisionFailureCaptures[mission.Id] = signature;
            if (_ProvisionFailureCaptures.Count > 10000) _ProvisionFailureCaptures.Clear();

            await BlackBox.RecordMissionFailureAsync(mission, MissionStatusEnum.Assigned, step, context =>
            {
                context.Captain = captain;
                BlackBoxSiteFacts.ApplyException(context, cause);
                if (failure != null)
                {
                    context.GitFacts ??= new BlackBoxGitFacts();
                    context.GitFacts.CheckoutPath ??= failure.WorktreePath;
                    context.GitFacts.WorktreeExisted ??= failure.WorktreeExisted;
                }
                context.Reason = "Dock provisioning failed" + (harborId != null ? " on Harbor " + harborId : "") + " at " + step
                    + (cause != null ? ": " + cause.Message : ".");
                context.Note = "Non-terminal: the mission went back to Pending and is retried on the next dispatch cycle.";
            }, token, callerMember, callerFile, callerLine).ConfigureAwait(false);
        }

        #endregion
    }
}
