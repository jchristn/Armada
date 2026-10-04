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
    /// Skills API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listSkills</c>: GET `/api/v1/skills${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Skill>?> ListSkillsAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Skill>>($"/api/v1/skills{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getSkill</c>: GET `/api/v1/skills/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Skill?> GetSkillAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Skill>($"/api/v1/skills/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createSkill</c>: POST '/api/v1/skills'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Skill?> CreateSkillAsync(Skill data, CancellationToken token = default)
        {
            return PostAsync<Skill>("/api/v1/skills", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateSkill</c>: PUT `/api/v1/skills/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Skill?> UpdateSkillAsync(string id, Skill data, CancellationToken token = default)
        {
            return PutAsync<Skill>($"/api/v1/skills/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteSkill</c>: DEL `/api/v1/skills/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteSkillAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/skills/{E(id)}", null, null, token);
        }

        #endregion
    }
}
