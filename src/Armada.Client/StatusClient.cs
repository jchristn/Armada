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
    public sealed class StatusClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal StatusClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>getStatus</c>: GET '/api/v1/status'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getStatus")]
        public Task<ArmadaStatus?> GetAsync(CancellationToken token = default)
        {
            return _Transport.GetAsync<ArmadaStatus>("/api/v1/status", null, token);
        }

        /// <summary>
        /// Dashboard <c>getHealth</c>: GET '/api/v1/status/health'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getHealth")]
        public Task<HealthResult?> GetHealthAsync(CancellationToken token = default)
        {
            return _Transport.GetAsync<HealthResult>("/api/v1/status/health", null, token);
        }

        /// <summary>
        /// Dashboard <c>getDoctor</c>: GET '/api/v1/doctor'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getDoctor")]
        public Task<List<DoctorCheck>?> GetDoctorAsync(CancellationToken token = default)
        {
            return _Transport.GetAsync<List<DoctorCheck>>("/api/v1/doctor", null, token);
        }

        #endregion
    }
}
