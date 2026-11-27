namespace Armada.Core.Services
{
    using System.Collections.Concurrent;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Black box capture of mission failures and provisioning failures.
    /// </summary>
    internal sealed class MissionFailureCapture
    {
        #region Private-Members

        private readonly MissionService _Service;
        private readonly IDockService _Docks;

        private readonly ConcurrentDictionary<string, string> _ProvisionFailureCaptures = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">The mission service facade.</param>
        /// <param name="context">State and services shared by the mission services.</param>
        /// <exception cref="ArgumentNullException">A parameter is null.</exception>
        internal MissionFailureCapture(MissionService service, MissionServiceContext context)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            if (context == null) throw new ArgumentNullException(nameof(context));
            _Docks = context.Docks;
        }

        #endregion

        #region Internal-Methods

        // A provisioned dock ends the failure streak, so the next provision failure is captured again.
        internal void ForgetProvisionFailureCapture(string missionId) => _ProvisionFailureCaptures.TryRemove(missionId, out string? _);

        internal Task<string?> RecordFailureAsync(
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
            return _Service.BlackBox.RecordMissionFailureAsync(mission, statusBefore, step, context =>
            {
                if (dock != null && context.Dock == null) context.Dock = dock;
                configure?.Invoke(context);
            }, token, callerMember, callerFile, callerLine);
        }

        internal async Task RecordProvisionFailureAsync(Mission mission, Captain captain, string? harborId, Exception? exception, CancellationToken token,
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

            await _Service.BlackBox.RecordMissionFailureAsync(mission, MissionStatusEnum.Assigned, step, context =>
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
