namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Production <see cref="IFleetActionMissionDispatcher"/>: validates and dispatches through
    /// <see cref="IAdmiralService"/>, maps voyage status to a target outcome, and cancels voyages with the same
    /// semantics as <c>DELETE /api/v1/voyages/{id}</c> and the <c>cancel_voyage</c> MCP tool.
    /// </summary>
    public class AdmiralFleetActionMissionDispatcher : IFleetActionMissionDispatcher
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly IAdmiralService _Admiral;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="admiral">Admiral service.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public AdmiralFleetActionMissionDispatcher(DatabaseDriver database, IAdmiralService admiral)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Admiral = admiral ?? throw new ArgumentNullException(nameof(admiral));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<DispatchValidationResult> ValidateAsync(Vessel vessel, string? pipelineId, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            return await _Admiral.ValidateDispatchAsync(null, pipelineId, null, vessel.Id, 1, false, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<string> DispatchAsync(Vessel vessel, string title, string prompt, string? pipelineId, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (String.IsNullOrEmpty(title)) throw new ArgumentNullException(nameof(title));

            List<MissionDescription> missions = new List<MissionDescription>
            {
                new MissionDescription(title, prompt ?? String.Empty)
            };

            Voyage voyage = await _Admiral.DispatchVoyageAsync(title, prompt ?? String.Empty, vessel.Id, missions, pipelineId, token).ConfigureAwait(false);
            return voyage.Id;
        }

        /// <inheritdoc />
        public async Task<FleetActionVoyageOutcomeEnum> GetOutcomeAsync(string voyageId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(voyageId)) throw new ArgumentNullException(nameof(voyageId));

            Voyage? voyage = await _Database.Voyages.ReadAsync(voyageId, token).ConfigureAwait(false);
            if (voyage == null) return FleetActionVoyageOutcomeEnum.Missing;

            switch (voyage.Status)
            {
                case VoyageStatusEnum.Complete:
                    List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(voyageId, token).ConfigureAwait(false);
                    if (missions.Count > 0 && missions.All(m => m.Status == MissionStatusEnum.Cancelled))
                        return FleetActionVoyageOutcomeEnum.Cancelled;
                    return FleetActionVoyageOutcomeEnum.Succeeded;
                case VoyageStatusEnum.Failed:
                    return FleetActionVoyageOutcomeEnum.Failed;
                case VoyageStatusEnum.Cancelled:
                    return FleetActionVoyageOutcomeEnum.Cancelled;
                default:
                    return FleetActionVoyageOutcomeEnum.Running;
            }
        }

        /// <inheritdoc />
        public async Task CancelVoyageAsync(string voyageId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(voyageId)) throw new ArgumentNullException(nameof(voyageId));

            Voyage? voyage = await _Database.Voyages.ReadAsync(voyageId, token).ConfigureAwait(false);
            if (voyage == null) return;
            if (voyage.Status == VoyageStatusEnum.Complete || voyage.Status == VoyageStatusEnum.Failed || voyage.Status == VoyageStatusEnum.Cancelled) return;

            List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(voyageId, token).ConfigureAwait(false);
            foreach (Mission mission in missions)
            {
                if (mission.Status != MissionStatusEnum.Pending && mission.Status != MissionStatusEnum.Assigned) continue;

                if (!String.IsNullOrEmpty(mission.CaptainId))
                {
                    Captain? captain = await _Database.Captains.ReadAsync(mission.CaptainId, token).ConfigureAwait(false);
                    if (captain != null && captain.CurrentMissionId == mission.Id)
                    {
                        List<Mission> otherMissions = (await _Database.Missions.EnumerateByCaptainAsync(captain.Id, token).ConfigureAwait(false))
                            .Where(om => om.Id != mission.Id && (om.Status == MissionStatusEnum.InProgress || om.Status == MissionStatusEnum.Assigned))
                            .ToList();
                        if (otherMissions.Count == 0)
                        {
                            captain.State = CaptainStateEnum.Idle;
                            captain.CurrentMissionId = null;
                            captain.CurrentDockId = null;
                            captain.ProcessId = null;
                            captain.RecoveryAttempts = 0;
                            captain.LastUpdateUtc = DateTime.UtcNow;
                            await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
                        }
                    }
                }

                mission.Status = MissionStatusEnum.Cancelled;
                mission.CompletedUtc = DateTime.UtcNow;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            }

            voyage.Status = VoyageStatusEnum.Cancelled;
            voyage.CompletedUtc = DateTime.UtcNow;
            voyage.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Voyages.UpdateAsync(voyage, token).ConfigureAwait(false);
        }

        #endregion
    }
}
