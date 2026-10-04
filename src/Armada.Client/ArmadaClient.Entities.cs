namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Entities API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>getEntity</c>: GET /api/v1/{type}/{id} for the generic entity lookup (fleets, vessels, captains, missions, voyages, signals, events, docks, merge-queue, playbooks, objectives, releases, environments, deployments).
        /// </summary>
        /// <param name="type">Entity type, for example missions.</param>
        /// <param name="id">Entity id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentException">Thrown for an unknown entity type.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ArmadaRawJson?> GetEntityAsync(string type, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(type) || !_EntityEndpoints.Contains(type)) throw new ArgumentException("Unknown entity type: " + type, nameof(type));
            return GetAsync<ArmadaRawJson>($"/api/v1/{type}/{E(id)}", null, token);
        }

        #endregion
    }
}
