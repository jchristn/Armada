namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Cancels a voyage the same way on every surface (REST, MCP, WebSocket): the voyage is marked Cancelled, then each
    /// Pending, Assigned, or Blocked mission is cancelled through <see cref="IAdmiralService.CancelMissionAsync"/>.
    /// In-progress work is left running. The result carries counts only. Thread safe: stateless.
    /// </summary>
    public static class VoyageCancellation
    {
        #region Public-Methods

        /// <summary>
        /// Cancel a voyage already read under the caller's scope.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="admiral">Admiral service.</param>
        /// <param name="voyage">Voyage to cancel.</param>
        /// <param name="tenantId">Tenant to scope the mission read to, or null for every tenant (global admin).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The cancelled voyage with counts.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static async Task<VoyageCancelResult> CancelAsync(DatabaseDriver database, IAdmiralService admiral, Voyage voyage, string? tenantId, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (admiral == null) throw new ArgumentNullException(nameof(admiral));
            if (voyage == null) throw new ArgumentNullException(nameof(voyage));

            VoyageStatusEnum statusBefore = voyage.Status;
            voyage.Status = VoyageStatusEnum.Cancelled;
            voyage.CompletedUtc = DateTime.UtcNow;
            voyage.LastUpdateUtc = DateTime.UtcNow;
            voyage = await database.Voyages.UpdateAsync(voyage, token).ConfigureAwait(false);

            VoyageCancelResult result = new VoyageCancelResult { Voyage = voyage };
            List<Mission> missions = String.IsNullOrEmpty(tenantId)
                ? await database.Missions.EnumerateByVoyageAsync(voyage.Id, token).ConfigureAwait(false)
                : await database.Missions.EnumerateByVoyageAsync(tenantId, voyage.Id, token).ConfigureAwait(false);
            foreach (Mission mission in missions)
            {
                if (mission.Status != MissionStatusEnum.Pending && mission.Status != MissionStatusEnum.Assigned && mission.Status != MissionStatusEnum.Blocked) continue;
                try
                {
                    Mission cancelled = await admiral.CancelMissionAsync(mission, token).ConfigureAwait(false);
                    result.CancelledMissions++;
                    result.Cancelled.Add(cancelled);
                }
                catch (InvalidOperationException ex)
                {
                    result.NotCancelledMissions++;
                    if (result.FirstNotCancelledReason == null) result.FirstNotCancelledReason = mission.Id + ": " + ex.Message;
                }
            }

            // An operator cancellation: recorded after its missions (each recorded by CancelMissionAsync), triaged External.
            if (statusBefore != VoyageStatusEnum.Cancelled)
            {
                await admiral.BlackBox.RecordVoyageFailureAsync(result.Voyage, statusBefore, BlackBoxStepEnum.Cancelled,
                    "Cancelled by an operator: " + result.CancelledMissions + " mission(s) cancelled" + (result.NotCancelledMissions > 0 ? ", " + result.NotCancelledMissions + " could not be" : "") + ".",
                    context => context.Note = result.FirstNotCancelledReason, token).ConfigureAwait(false);
            }

            return result;
        }

        #endregion
    }
}
