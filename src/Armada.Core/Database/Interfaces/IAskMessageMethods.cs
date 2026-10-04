namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for Ask Armada thread messages (ask_messages).
    /// </summary>
    public interface IAskMessageMethods
    {
        /// <summary>
        /// Append a message to a thread. In one transaction the thread row is updated first (message count, last
        /// message time, and optionally unread count), which serializes concurrent appends to the same thread, and the
        /// message receives the next per-thread sequence number. Sequence numbers are strictly increasing and unique
        /// per thread even under concurrent appends (a unique index on (thread_id, sequence) backs this up).
        /// </summary>
        /// <param name="message">Message to append; TenantId and ThreadId are required. Sequence is assigned.</param>
        /// <param name="countsAsUnread">Whether the message increments the thread's unread counter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored message with its sequence.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the message is null.</exception>
        /// <exception cref="ArgumentException">Thrown when TenantId or ThreadId is missing.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the thread does not exist in the tenant.</exception>
        Task<AskMessage> CreateAsync(AskMessage message, bool countsAsUnread, CancellationToken token = default);

        /// <summary>
        /// Read a message.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Message identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The message (without nested tool calls), or null.</returns>
        Task<AskMessage?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update a message's content, thinking, links, captain, and duration.
        /// </summary>
        /// <param name="message">Message with updated fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated message.</returns>
        Task<AskMessage> UpdateAsync(AskMessage message, CancellationToken token = default);

        /// <summary>
        /// Read a page of a thread's messages ordered by ascending sequence: the newest <paramref name="pageSize"/>
        /// messages with a sequence below <paramref name="beforeSequence"/> (or the newest overall when null).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="beforeSequence">Exclusive upper bound on sequence, or null.</param>
        /// <param name="pageSize">Maximum messages to return (1-500).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page; nested tool calls, proposal, and tracked work are not populated.</returns>
        Task<AskMessagePage> EnumerateAsync(string tenantId, string threadId, int? beforeSequence, int pageSize, CancellationToken token = default);
    }
}
