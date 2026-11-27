namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// The one way a mission's status changes (RELIABILITY_SIMPLICITY_PLAN.md RS-3.5). A transition checks the edge
    /// against <see cref="MissionStateMachine"/> (an allowed status with no legal edge to the target is dropped), writes the mission only while its stored status is still one of the
    /// allowed ones (a compare-and-set), emits <c>mission.status_changed</c>, and re-evaluates the voyage when the mission
    /// reaches a terminal status. A refused write returns false, leaves the stored row unchanged, and sets the mission's in-memory status back to the stored one.
    /// </summary>
    public sealed class MissionTransitions
    {
        #region Public-Members

        /// <summary>
        /// Records and broadcasts <c>mission.status_changed</c> (mission after the change, previous status, message). When
        /// null, the event row is written without a broadcast.
        /// </summary>
        public Func<Mission, MissionStatusEnum, string, Task>? StatusChanged { get; set; } = null;

        /// <summary>
        /// Re-evaluates a voyage after one of its missions reached a terminal status (voyage id). Null to leave voyages to
        /// the health check.
        /// </summary>
        public Func<string, CancellationToken, Task>? VoyageChanged { get; set; } = null;

        /// <summary>
        /// Wakes the Admiral dispatcher (RS-4.5) after a transition that can free capacity or unblock a dependent: every
        /// status except Assigned and InProgress, which only consume capacity. Must return at once. Null when not wired.
        /// </summary>
        public Action? DispatchWake { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly string _Header = "[MissionTransitions] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule? _Logging;
        private static readonly JsonSerializerOptions _PayloadOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public MissionTransitions(DatabaseDriver database, LoggingModule? logging = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Move a mission to <paramref name="to"/> while its stored status is one of <paramref name="allowedFrom"/>.
        /// </summary>
        /// <param name="mission">Mission; on success it holds the written values.</param>
        /// <param name="allowedFrom">Statuses the stored row may have.</param>
        /// <param name="to">Target status.</param>
        /// <param name="mutate">Changes to the other fields, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the mission was written; false when its stored status had moved on or it is missing.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission or allowedFrom is null.</exception>
        public Task<bool> TryTransitionAsync(Mission mission, IEnumerable<MissionStatusEnum> allowedFrom, MissionStatusEnum to, Action<Mission>? mutate = null, CancellationToken token = default)
        {
            if (allowedFrom == null) throw new ArgumentNullException(nameof(allowedFrom));
            MissionTransitionRequest request = new MissionTransitionRequest();
            request.AllowedFrom.AddRange(allowedFrom);
            request.To = to;
            request.Mutate = mutate;
            return TryTransitionAsync(mission, request, token);
        }

        /// <summary>
        /// Apply a transition request.
        /// </summary>
        /// <param name="mission">Mission; on success it holds the written values.</param>
        /// <param name="request">Transition.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the mission was written; false when its stored status had moved on or it is missing.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission or request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when no allowed status is given.</exception>
        public async Task<bool> TryTransitionAsync(Mission mission, MissionTransitionRequest request, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.AllowedFrom == null || request.AllowedFrom.Count == 0) throw new ArgumentException("At least one allowed status is required.", nameof(request));

            // Only legal edges may apply. A terminal status rewritten to itself is a no-op; any other illegal edge is a
            // caller defect, logged, and never written.
            List<MissionStatusEnum> legalFrom = new List<MissionStatusEnum>();
            foreach (MissionStatusEnum from in request.AllowedFrom)
            {
                bool legal = request.Operator ? MissionStateMachine.IsValidTransition(from, request.To) : MissionStateMachine.IsValidInternalTransition(from, request.To);
                if (legal)
                {
                    if (!legalFrom.Contains(from)) legalFrom.Add(from);
                }
                else if (from != request.To)
                {
                    _Logging?.Warn(_Header + "refused " + (request.Operator ? "operator" : "internal") + " transition " + from + " -> " + request.To + " for " + mission.Id + ": not in the state machine");
                }
            }
            if (legalFrom.Count == 0) return false;
            MissionStatusEnum previous = mission.Status;
            request.Mutate?.Invoke(mission);
            mission.Status = request.To;

            bool written;
            try
            {
                written = request.ExpectedLastUpdateUtc.HasValue
                    ? await _Database.Missions.TryUpdateIfUnchangedAsync(mission, request.ExpectedLastUpdateUtc.Value, legalFrom, token).ConfigureAwait(false)
                    : await _Database.Missions.TryUpdateIfStatusAsync(mission, legalFrom, token).ConfigureAwait(false);
            }
            catch
            {
                // A failed write (for example a transient deadlock the caller retries) leaves the in-memory status as stored.
                mission.Status = previous;
                throw;
            }
            if (!written)
            {
                // Match UpdateAsync: the caller sees the stored status, not the refused one.
                Mission? stored = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                mission.Status = stored?.Status ?? previous;
                if (stored != null) mission.LastUpdateUtc = stored.LastUpdateUtc;
                return false;
            }

            if (previous != request.To && request.To != MissionStatusEnum.Assigned && request.To != MissionStatusEnum.InProgress && DispatchWake != null)
            {
                try { DispatchWake(); }
                catch (Exception e) { _Logging?.Warn(_Header + "dispatch wake failed: " + e.Message); }
            }

            if (previous != request.To)
                await EmitStatusChangedAsync(mission, previous, request.Message ?? ("Mission " + request.To + ": " + mission.Title)).ConfigureAwait(false);

            if (request.ReevaluateVoyage && MissionStateMachine.IsTerminal(request.To) && !String.IsNullOrEmpty(mission.VoyageId) && VoyageChanged != null)
            {
                try
                {
                    await VoyageChanged(mission.VoyageId, token).ConfigureAwait(false);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    _Logging?.Warn(_Header + "voyage " + mission.VoyageId + " could not be re-evaluated after " + mission.Id + " reached " + request.To + ": " + e.Message);
                }
            }

            return true;
        }

        #endregion

        #region Private-Methods

        private async Task EmitStatusChangedAsync(Mission mission, MissionStatusEnum previous, string message)
        {
            try
            {
                if (StatusChanged != null)
                {
                    await StatusChanged(mission, previous, message).ConfigureAwait(false);
                    return;
                }

                ArmadaEvent evt = new ArmadaEvent("mission.status_changed", message);
                evt.TenantId = mission.TenantId;
                evt.UserId = mission.UserId;
                evt.EntityType = "mission";
                evt.EntityId = mission.Id;
                evt.CaptainId = mission.CaptainId;
                evt.MissionId = mission.Id;
                evt.VesselId = mission.VesselId;
                evt.VoyageId = mission.VoyageId;
                evt.Payload = JsonSerializer.Serialize(new MissionStatusChangedPayload { Status = mission.Status, PreviousStatus = previous }, _PayloadOptions);
                await _Database.Events.CreateAsync(evt, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logging?.Warn(_Header + "mission.status_changed for " + mission.Id + " could not be emitted: " + e.Message);
            }
        }

        #endregion
    }
}
