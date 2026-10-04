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
    /// Playbooks API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listPlaybooks</c>: GET `/api/v1/playbooks${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Playbook>?> ListPlaybooksAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Playbook>>($"/api/v1/playbooks{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getPlaybook</c>: GET `/api/v1/playbooks/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Playbook?> GetPlaybookAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Playbook>($"/api/v1/playbooks/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createPlaybook</c>: POST '/api/v1/playbooks'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Playbook?> CreatePlaybookAsync(Playbook data, CancellationToken token = default)
        {
            return PostAsync<Playbook>("/api/v1/playbooks", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updatePlaybook</c>: PUT `/api/v1/playbooks/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Playbook?> UpdatePlaybookAsync(string id, Playbook data, CancellationToken token = default)
        {
            return PutAsync<Playbook>($"/api/v1/playbooks/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deletePlaybook</c>: DEL `/api/v1/playbooks/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeletePlaybookAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/playbooks/{E(id)}", null, null, token);
        }

        #endregion
    }
}
