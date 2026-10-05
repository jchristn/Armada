namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for Ask Armada conversation threads (ask_threads). Owner-scoped reads take the tenant and the
    /// owning user; a thread of another user is never returned.
    /// </summary>
    public interface IAskThreadMethods
    {
        /// <summary>
        /// Create a thread.
        /// </summary>
        /// <param name="thread">Thread to create; TenantId and UserId are required.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created thread.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the thread is null.</exception>
        /// <exception cref="ArgumentException">Thrown when TenantId or UserId is missing.</exception>
        Task<AskThread> CreateAsync(AskThread thread, CancellationToken token = default);

        /// <summary>
        /// Read a thread owned by a user.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">Owning user identifier.</param>
        /// <param name="id">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The thread, or null when it does not exist or belongs to someone else.</returns>
        Task<AskThread?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a thread by id regardless of owner (server-internal use: MCP gate, work tracker).
        /// </summary>
        /// <param name="id">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The thread, or null.</returns>
        Task<AskThread?> ReadByIdAsync(string id, CancellationToken token = default);

        /// <summary>
        /// List unpinned threads, across all tenants and users, whose last activity (the last message, or creation
        /// when the thread has no messages) is before a cutoff, oldest first. Used by retention pruning.
        /// </summary>
        /// <param name="inactiveBeforeUtc">Activity cutoff (UTC).</param>
        /// <param name="includeArchived">Whether archived threads are included.</param>
        /// <param name="maxResults">Maximum rows to return; clamped to 1..1000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Matching threads, oldest activity first.</returns>
        Task<List<AskThread>> EnumerateInactiveAsync(DateTime inactiveBeforeUtc, bool includeArchived, int maxResults, CancellationToken token = default);

        /// <summary>
        /// Update the user-editable fields of a thread (title, captain, auto-approve, summary, pinned, archived). Message
        /// and unread counters are maintained by message creation and <see cref="MarkReadAsync"/>, never by this method.
        /// </summary>
        /// <param name="thread">Thread with updated fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated thread as stored.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the thread is null.</exception>
        Task<AskThread> UpdateAsync(AskThread thread, CancellationToken token = default);

        /// <summary>
        /// Set or clear a thread's CLI tool permission policy override. <see cref="UpdateAsync"/> never writes this
        /// column.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Thread identifier.</param>
        /// <param name="policy">Policy, or null to inherit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row was updated.</returns>
        Task<bool> UpdateCliPermissionPolicyAsync(string tenantId, string id, CliPermissionPolicyEnum? policy, CancellationToken token = default);

        /// <summary>
        /// Enumerate a user's threads: pinned first, then most recent activity (last message, else creation) first.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">Owning user identifier.</param>
        /// <param name="request">Paging, search, and archive filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of threads.</returns>
        Task<EnumerationResult<AskThread>> EnumerateAsync(string tenantId, string userId, AskThreadEnumerateRequest request, CancellationToken token = default);

        /// <summary>
        /// Delete a thread and, in one transaction, its messages, tool calls, proposals, and tracked-work rows. The work
        /// itself (voyages, missions, runs) is untouched.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">Owning user identifier.</param>
        /// <param name="id">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a thread was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Reset a thread's unread counter to zero.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">Owning user identifier.</param>
        /// <param name="id">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the thread exists and was updated.</returns>
        Task<bool> MarkReadAsync(string tenantId, string userId, string id, CancellationToken token = default);
    }
}
