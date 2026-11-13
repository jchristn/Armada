namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Real battery dispatch: one voyage on the fixture vessel under the built-in ModelEvaluation pipeline with the
    /// task's persona, pinned to the captain for every stage (strict) and landing mode None, so the work stays in its
    /// dock for grading. It then polls the mission until it finishes (WorkProduced, Review, Complete, Failed,
    /// LandingFailed, Cancelled or Blocked) or the timeout passes, which cancels it.
    /// </summary>
    public class AdmiralBatteryTaskDispatcher : IBatteryTaskDispatcher
    {
        #region Public-Members

        /// <summary>
        /// Mission statuses after which a battery task is graded.
        /// </summary>
        public static readonly IReadOnlyList<MissionStatusEnum> FinishedStatuses = new List<MissionStatusEnum>
        {
            MissionStatusEnum.WorkProduced, MissionStatusEnum.Review, MissionStatusEnum.Complete, MissionStatusEnum.Failed,
            MissionStatusEnum.LandingFailed, MissionStatusEnum.Cancelled, MissionStatusEnum.Blocked
        };

        /// <summary>
        /// Resolves the host of a mission's dock (set by the server; null means the Admiral's machine).
        /// </summary>
        public DockHostResolver? DockHosts { get; set; } = null;

        /// <summary>
        /// Returns a dispatcher to use instead of real dispatch, or null (test hosts set a canned dispatcher).
        /// </summary>
        public Func<IBatteryTaskDispatcher?>? OverrideSource { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly string _Header = "[AdmiralBatteryTaskDispatcher] ";
        private readonly IAdmiralService _Admiral;
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private readonly TimeSpan _PollInterval;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="admiral">Admiral service (dispatch and cancel).</param>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="pollInterval">How often to read the mission, or null for 5 seconds.</param>
        /// <exception cref="ArgumentNullException">Thrown when admiral, database or logging is null.</exception>
        public AdmiralBatteryTaskDispatcher(IAdmiralService admiral, DatabaseDriver database, LoggingModule logging, TimeSpan? pollInterval = null)
        {
            _Admiral = admiral ?? throw new ArgumentNullException(nameof(admiral));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _PollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<BatteryDispatchResult> RunAsync(BatteryDispatchRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            IBatteryTaskDispatcher? replacement = OverrideSource?.Invoke();
            if (replacement != null) return await replacement.RunAsync(request, token).ConfigureAwait(false);
            BatteryDispatchResult result = new BatteryDispatchResult();

            List<CaptainAssignmentOverride> overrides = new List<CaptainAssignmentOverride>
            {
                new CaptainAssignmentOverride("*", request.Captain.Id, null) { StrictCaptain = true, PromptVariant = request.PromptVariant }
            };
            MissionDescription mission = new MissionDescription(request.Title, request.Description)
            {
                RequestedCaptainId = request.Captain.Id,
                Persona = request.Persona
            };

            Voyage voyage;
            try
            {
                voyage = await _Admiral.DispatchVoyageAsync(
                    "Battery: " + request.Title,
                    request.Description,
                    request.Vessel.Id,
                    new List<MissionDescription> { mission },
                    BatteryFixtureService.PipelineName,
                    null,
                    JsonSerializer.Serialize(overrides),
                    LandingModeEnum.None,
                    token,
                    null,
                    true).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                result.FailureReason = "Dispatch failed: " + ex.Message;
                return result;
            }

            result.VoyageId = voyage.Id;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(1000, request.TimeoutMs));
            Mission? current = null;
            while (true)
            {
                List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(voyage.Id, CancellationToken.None).ConfigureAwait(false);
                current = missions.OrderBy(m => m.CreatedUtc).LastOrDefault();
                if (current != null && missions.All(m => FinishedStatuses.Contains(m.Status))) break;
                bool cancelled = token.IsCancellationRequested;
                if (cancelled || DateTime.UtcNow >= deadline)
                {
                    // Timed out or cancelled (a Full Evaluate stopping): cancel what is still open so its dock and captain
                    // are released, then report what is there.
                    if (cancelled) result.Cancelled = true;
                    else result.TimedOut = true;
                    foreach (Mission open in missions.Where(m => !FinishedStatuses.Contains(m.Status)))
                    {
                        try
                        {
                            await _Admiral.CancelMissionAsync(open, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _Logging.Warn(_Header + "could not cancel " + (cancelled ? "stopped" : "timed-out") + " mission " + open.Id + ": " + ex.Message);
                        }
                    }

                    if (cancelled)
                    {
                        List<Mission> after = await _Database.Missions.EnumerateByVoyageAsync(voyage.Id, CancellationToken.None).ConfigureAwait(false);
                        current = after.OrderBy(m => m.CreatedUtc).LastOrDefault() ?? current;
                    }

                    break;
                }

                try
                {
                    await Task.Delay(_PollInterval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Checked at the top of the next pass, which cancels the open missions.
                }
            }

            result.Mission = current;
            if (current != null && !String.IsNullOrEmpty(current.DockId))
            {
                Dock? dock = await _Database.Docks.ReadAsync(current.DockId!, CancellationToken.None).ConfigureAwait(false);
                if (dock != null && !String.IsNullOrWhiteSpace(dock.WorktreePath))
                {
                    DockHost host = DockHosts != null ? DockHosts.ForDock(dock) : new DockHostResolver(new GitService(_Logging), _Logging, null).Local;
                    result.Workspace = new DockBatteryWorkspace(host, dock.WorktreePath!);
                }
            }

            return result;
        }

        #endregion
    }
}
