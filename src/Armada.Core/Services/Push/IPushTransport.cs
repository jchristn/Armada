namespace Armada.Core.Services.Push
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Sends push messages and fetches delivery receipts. The production implementation is
    /// <see cref="ExpoPushTransport"/> (the Expo Push Service HTTP API); tests substitute a double so the real service is
    /// never called.
    /// </summary>
    public interface IPushTransport
    {
        /// <summary>
        /// Send up to <see cref="ExpoPushTransport.MaxMessagesPerRequest"/> messages in one request.
        /// </summary>
        /// <param name="messages">Messages (one per device).</param>
        /// <param name="accessToken">Optional Expo access token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One ticket per message, in message order.</returns>
        /// <exception cref="PushTransportException">Thrown when the request fails; <see cref="PushTransportException.Transient"/>
        /// tells whether a retry may succeed.</exception>
        Task<List<PushTicket>> SendAsync(List<PushMessage> messages, string? accessToken, CancellationToken token = default);

        /// <summary>
        /// Fetch the receipts of up to <see cref="ExpoPushTransport.MaxReceiptIdsPerRequest"/> tickets. Tickets whose
        /// receipt is not ready yet (or expired) are absent from the result.
        /// </summary>
        /// <param name="ticketIds">Ticket identifiers.</param>
        /// <param name="accessToken">Optional Expo access token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Receipts that are available.</returns>
        /// <exception cref="PushTransportException">Thrown when the request fails.</exception>
        Task<List<PushReceipt>> GetReceiptsAsync(List<string> ticketIds, string? accessToken, CancellationToken token = default);
    }
}
