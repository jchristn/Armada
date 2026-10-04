namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for tool calls recorded on Ask Armada messages (ask_message_tool_calls).
    /// </summary>
    public interface IAskMessageToolCallMethods
    {
        /// <summary>
        /// Store several tool calls in one transaction.
        /// </summary>
        /// <param name="calls">Tool calls; each needs TenantId, ThreadId, and MessageId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored tool calls.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        Task<List<AskMessageToolCall>> CreateManyAsync(List<AskMessageToolCall> calls, CancellationToken token = default);

        /// <summary>
        /// Read the tool calls of several messages, ordered by creation.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="messageIds">Message identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tool calls of those messages.</returns>
        Task<List<AskMessageToolCall>> EnumerateByMessagesAsync(string tenantId, string threadId, List<string> messageIds, CancellationToken token = default);
    }
}
