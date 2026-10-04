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
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listPromptTemplates</c>: GET `/api/v1/prompt-templates${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<PromptTemplate>?> ListPromptTemplatesAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<PromptTemplate>>($"/api/v1/prompt-templates{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getPromptTemplate</c>: GET `/api/v1/prompt-templates/${encodeURIComponent(name)}`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PromptTemplate?> GetPromptTemplateAsync(string name, CancellationToken token = default)
        {
            return GetAsync<PromptTemplate>($"/api/v1/prompt-templates/{E(name)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createPromptTemplate</c>: POST /api/v1/prompt-templates.
        /// </summary>
        /// <param name="data">Template to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PromptTemplate?> CreatePromptTemplateAsync(PromptTemplateCreateRequest data, CancellationToken token = default)
        {
            return PostAsync<PromptTemplate>("/api/v1/prompt-templates", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updatePromptTemplate</c>: PUT /api/v1/prompt-templates/{name}.
        /// </summary>
        /// <param name="name">Template name.</param>
        /// <param name="data">New content.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PromptTemplate?> UpdatePromptTemplateAsync(string name, PromptTemplateUpdateRequest data, CancellationToken token = default)
        {
            return PutAsync<PromptTemplate>($"/api/v1/prompt-templates/{E(name)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>resetPromptTemplate</c>: POST `/api/v1/prompt-templates/${encodeURIComponent(name)}/reset`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PromptTemplate?> ResetPromptTemplateAsync(string name, CancellationToken token = default)
        {
            return PostAsync<PromptTemplate>($"/api/v1/prompt-templates/{E(name)}/reset", null, null, token);
        }

        #endregion
    }
}
