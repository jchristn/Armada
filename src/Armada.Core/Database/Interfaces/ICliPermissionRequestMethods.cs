namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for CLI permission requests (cli_permission_requests).
    /// </summary>
    public interface ICliPermissionRequestMethods
    {
        /// <summary>
        /// Create a request.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created request.</returns>
        Task<CliPermissionRequest> CreateAsync(CliPermissionRequest request, CancellationToken token = default);

        /// <summary>
        /// Read a request by id (any tenant).
        /// </summary>
        /// <param name="id">Request identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The request, or null.</returns>
        Task<CliPermissionRequest?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Set the Ask message that renders the request card.
        /// </summary>
        /// <param name="id">Request identifier.</param>
        /// <param name="messageId">Message identifier, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpdateMessageAsync(string id, string? messageId, CancellationToken token = default);

        /// <summary>
        /// Atomically decide a pending request (compare-and-set on status Pending). Exactly one of several concurrent
        /// callers succeeds, which makes allow, deny, expiry, and cancellation mutually exclusive.
        /// </summary>
        /// <param name="id">Request identifier.</param>
        /// <param name="status">New status (not Pending).</param>
        /// <param name="source">What decided it.</param>
        /// <param name="ruleId">Deciding or remembered rule, or null.</param>
        /// <param name="decidedByUserId">Deciding user, or null.</param>
        /// <param name="message">Decision message, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call decided the request.</returns>
        Task<bool> TryDecideAsync(string id, CliPermissionRequestStatusEnum status, CliPermissionDecisionSourceEnum source, string? ruleId, string? decidedByUserId, string? message, CancellationToken token = default);

        /// <summary>
        /// List requests, newest first.
        /// </summary>
        /// <param name="query">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Requests.</returns>
        Task<List<CliPermissionRequest>> EnumerateAsync(CliPermissionRequestQuery query, CancellationToken token = default);

        /// <summary>
        /// Delete requests that are no longer pending (allowed, denied, expired, or cancelled) and were decided before a
        /// cutoff (a request with no decision time counts from its creation). Pending requests are never deleted.
        /// </summary>
        /// <param name="cutoffUtc">Requests decided before this time are deleted.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of requests deleted.</returns>
        Task<int> DeleteFinishedBeforeAsync(DateTime cutoffUtc, CancellationToken token = default);
    }
}
