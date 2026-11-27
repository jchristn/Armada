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
    /// Mux runtime endpoint API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class MuxEndpointsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal MuxEndpointsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/runtimes/mux/endpoints: one page of the saved Mux endpoints, ordered by name.
        /// </summary>
        /// <param name="configDirectory">Mux config directory, or null.</param>
        /// <param name="pageNumber">One-based page number.</param>
        /// <param name="pageSize">Page size, 1 to 1000 (the server normalizes other values).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateMuxEndpoints")]
        public Task<MuxEndpointPage?> EnumerateAsync(string? configDirectory = null, int pageNumber = 1, int pageSize = 10, CancellationToken token = default)
        {
            string query = "?pageNumber=" + pageNumber + "&pageSize=" + pageSize + (String.IsNullOrEmpty(configDirectory) ? "" : "&configDirectory=" + ArmadaQueryString.Escape(configDirectory));
            return _Transport.GetAsync<MuxEndpointPage>("/api/v1/runtimes/mux/endpoints" + query, null, token);
        }

        /// <summary>
        /// Dashboard <c>listMuxEndpoints</c>: every saved Mux endpoint for a picker, read explicitly page by page (1000 per
        /// page) from GET /api/v1/runtimes/mux/endpoints until TotalPages is reached. The returned page holds every endpoint
        /// and the server's TotalRecords; Success is false (with the server's error) when any page fails.
        /// </summary>
        /// <param name="configDirectory">Mux config directory, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Every endpoint, or the failing page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listMuxEndpoints")]
        public async Task<MuxEndpointPage?> ListAsync(string? configDirectory = null, CancellationToken token = default)
        {
            MuxEndpointPage? first = await EnumerateAsync(configDirectory, 1, 1000, token).ConfigureAwait(false);
            if (first == null || !first.Success) return first;
            for (int page = 2; page <= first.TotalPages; page++)
            {
                MuxEndpointPage? next = await EnumerateAsync(configDirectory, page, 1000, token).ConfigureAwait(false);
                if (next == null || !next.Success) return next;
                first.Objects.AddRange(next.Objects);
            }

            first.PageSize = first.Objects.Count < 1 ? first.PageSize : first.Objects.Count;
            first.TotalPages = first.Objects.Count < 1 ? 0 : 1;
            return first;
        }

        /// <summary>
        /// Dashboard <c>listAllMuxEndpoints</c>: every saved Mux endpoint for a picker, read page by page (see
        /// <see cref="ListAsync"/>).
        /// </summary>
        /// <param name="configDirectory">Mux config directory, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Every endpoint, or the failing page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listAllMuxEndpoints")]
        public Task<MuxEndpointPage?> ListAllAsync(string? configDirectory = null, CancellationToken token = default)
        {
            return ListAsync(configDirectory, token);
        }

        /// <summary>
        /// Dashboard <c>getMuxEndpoint</c>: GET /api/v1/runtimes/mux/endpoints/{name}.
        /// </summary>
        /// <param name="name">Endpoint name.</param>
        /// <param name="configDirectory">Mux config directory, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getMuxEndpoint")]
        public Task<MuxEndpointShowResult?> GetAsync(string name, string? configDirectory = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<MuxEndpointShowResult>($"/api/v1/runtimes/mux/endpoints/{ArmadaQueryString.Escape(name)}" + (String.IsNullOrEmpty(configDirectory) ? "" : "?configDirectory=" + ArmadaQueryString.Escape(configDirectory)), null, token);
        }

        #endregion
    }
}
