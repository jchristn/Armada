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
    public sealed class PlaybooksClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal PlaybooksClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listPlaybooks</c>: GET `/api/v1/playbooks${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listPlaybooks")]
        public Task<EnumerationResult<Playbook>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<Playbook>>($"/api/v1/playbooks{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getPlaybook</c>: GET `/api/v1/playbooks/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getPlaybook")]
        public Task<Playbook?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<Playbook>($"/api/v1/playbooks/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createPlaybook</c>: POST '/api/v1/playbooks'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createPlaybook")]
        public Task<Playbook?> CreateAsync(Playbook data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Playbook>("/api/v1/playbooks", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updatePlaybook</c>: PUT `/api/v1/playbooks/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updatePlaybook")]
        public Task<Playbook?> UpdateAsync(string id, Playbook data, CancellationToken token = default)
        {
            return _Transport.PutAsync<Playbook>($"/api/v1/playbooks/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deletePlaybook</c>: DEL `/api/v1/playbooks/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deletePlaybook")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/playbooks/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        #endregion
    }
}
