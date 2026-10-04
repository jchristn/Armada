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
    /// Status API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>getStatus</c>: GET '/api/v1/status'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ArmadaStatus?> GetStatusAsync(CancellationToken token = default)
        {
            return GetAsync<ArmadaStatus>("/api/v1/status", null, token);
        }

        /// <summary>
        /// Dashboard <c>getHealth</c>: GET '/api/v1/status/health'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<HealthResult?> GetHealthAsync(CancellationToken token = default)
        {
            return GetAsync<HealthResult>("/api/v1/status/health", null, token);
        }

        /// <summary>
        /// Dashboard <c>getDoctor</c>: GET '/api/v1/doctor'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<List<DoctorCheck>?> GetDoctorAsync(CancellationToken token = default)
        {
            return GetAsync<List<DoctorCheck>>("/api/v1/doctor", null, token);
        }

        #endregion
    }
}
