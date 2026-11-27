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
    /// Inbox API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class InboxClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal InboxClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>getInbox</c>: GET '/api/v1/inbox'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getInbox")]
        public Task<List<InboxItem>?> GetAsync(CancellationToken token = default)
        {
            return _Transport.EnumerateAllAsync<InboxItem>("/api/v1/inbox/enumerate", null, token);
        }

        #endregion
    }
}
