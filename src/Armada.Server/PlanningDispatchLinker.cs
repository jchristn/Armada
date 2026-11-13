namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Links a voyage dispatched from a planning session to every objective linked to the session (REST and MCP
    /// dispatch share it). The linked objectives are read in bounded pages. When a link would push an objective's id
    /// lists over <see cref="CollectionLimits.MaxLinkedIds"/>, the error names the voyage that was already dispatched
    /// and the objective that could not take it, so the caller is never left guessing.
    /// </summary>
    public static class PlanningDispatchLinker
    {
        /// <summary>
        /// Link the voyage to each objective linked to the planning session.
        /// </summary>
        /// <param name="objectives">Objective service.</param>
        /// <param name="auth">Caller.</param>
        /// <param name="planningSessionId">Planning session identifier.</param>
        /// <param name="voyageId">Dispatched voyage identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of objectives linked.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when an objective cannot hold the voyage's links.</exception>
        public static async Task<int> LinkAsync(ObjectiveService objectives, AuthContext auth, string planningSessionId, string voyageId, CancellationToken token = default)
        {
            if (objectives == null) throw new ArgumentNullException(nameof(objectives));
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(planningSessionId)) throw new ArgumentNullException(nameof(planningSessionId));
            if (String.IsNullOrWhiteSpace(voyageId)) throw new ArgumentNullException(nameof(voyageId));

            List<Objective> linked = await objectives.EnumerateByPlanningSessionAsync(auth, planningSessionId, token).ConfigureAwait(false);
            int count = 0;
            foreach (Objective objective in linked)
            {
                try
                {
                    await objectives.LinkVoyageAsync(auth, objective.Id, voyageId, token).ConfigureAwait(false);
                }
                catch (CollectionLimitExceededException ex)
                {
                    throw new CollectionLimitExceededException(ex.Field, ex.Limit, ex.Actual,
                        "Voyage " + voyageId + " was dispatched, but objective " + objective.Id + " could not be linked to it: " + ex.Message);
                }

                count++;
            }

            return count;
        }
    }
}
