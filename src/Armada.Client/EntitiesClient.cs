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
    public sealed class EntitiesClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        private static readonly HashSet<string> _EntityEndpoints = new HashSet<string>(StringComparer.Ordinal)
        {
            "fleets", "vessels", "captains", "missions", "voyages", "signals", "events", "docks", "merge-queue",
            "playbooks", "objectives", "releases", "environments", "deployments"
        };

        #endregion

        #region Constructors-and-Factories

        internal EntitiesClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

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
        [DashboardExport("getEntity")]
        public Task<ArmadaRawJson?> GetAsync(string type, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(type) || !_EntityEndpoints.Contains(type)) throw new ArgumentException("Unknown entity type: " + type, nameof(type));
            return _Transport.GetAsync<ArmadaRawJson>($"/api/v1/{type}/{ArmadaQueryString.Escape(id)}", null, token);
        }

        #endregion
    }
}
