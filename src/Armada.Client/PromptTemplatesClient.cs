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
    /// PromptTemplates API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class PromptTemplatesClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal PromptTemplatesClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listPromptTemplates</c>: GET `/api/v1/prompt-templates${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listPromptTemplates")]
        public Task<EnumerationResult<PromptTemplate>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<PromptTemplate>>($"/api/v1/prompt-templates{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getPromptTemplate</c>: GET `/api/v1/prompt-templates/${encodeURIComponent(name)}`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getPromptTemplate")]
        public Task<PromptTemplate?> GetAsync(string name, CancellationToken token = default)
        {
            return _Transport.GetAsync<PromptTemplate>($"/api/v1/prompt-templates/{ArmadaQueryString.Escape(name)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createPromptTemplate</c>: POST /api/v1/prompt-templates.
        /// </summary>
        /// <param name="data">Template to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createPromptTemplate")]
        public Task<PromptTemplate?> CreateAsync(PromptTemplateCreateRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<PromptTemplate>("/api/v1/prompt-templates", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updatePromptTemplate</c>: PUT /api/v1/prompt-templates/{name}.
        /// </summary>
        /// <param name="name">Template name.</param>
        /// <param name="data">New content.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updatePromptTemplate")]
        public Task<PromptTemplate?> UpdateAsync(string name, PromptTemplateUpdateRequest data, CancellationToken token = default)
        {
            return _Transport.PutAsync<PromptTemplate>($"/api/v1/prompt-templates/{ArmadaQueryString.Escape(name)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>resetPromptTemplate</c>: POST `/api/v1/prompt-templates/${encodeURIComponent(name)}/reset`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("resetPromptTemplate")]
        public Task<PromptTemplate?> ResetAsync(string name, CancellationToken token = default)
        {
            return _Transport.PostAsync<PromptTemplate>($"/api/v1/prompt-templates/{ArmadaQueryString.Escape(name)}/reset", null, null, token);
        }

        #endregion
    }
}
