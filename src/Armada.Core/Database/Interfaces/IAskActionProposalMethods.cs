namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for Ask Armada action proposals (ask_action_proposals).
    /// </summary>
    public interface IAskActionProposalMethods
    {
        /// <summary>
        /// Create a proposal.
        /// </summary>
        /// <param name="proposal">Proposal; TenantId and ThreadId are required.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created proposal.</returns>
        Task<AskActionProposal> CreateAsync(AskActionProposal proposal, CancellationToken token = default);

        /// <summary>
        /// Read a proposal.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Proposal identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The proposal, or null.</returns>
        Task<AskActionProposal?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update every mutable field of a proposal (message link, status, result, error, decision, execution time).
        /// </summary>
        /// <param name="proposal">Proposal with updated fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated proposal.</returns>
        Task<AskActionProposal> UpdateAsync(AskActionProposal proposal, CancellationToken token = default);

        /// <summary>
        /// Atomically move a proposal from one status to another (compare-and-set), recording the decision. Exactly one
        /// of several concurrent callers succeeds, which makes approve, reject, and expire mutually exclusive.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Proposal identifier.</param>
        /// <param name="from">Required current status.</param>
        /// <param name="to">New status.</param>
        /// <param name="decidedByUserId">Deciding user, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call performed the transition.</returns>
        Task<bool> TryTransitionAsync(string tenantId, string id, AskProposalStatusEnum from, AskProposalStatusEnum to, string? decidedByUserId, CancellationToken token = default);

        /// <summary>
        /// Read a thread's proposals, oldest first, optionally filtered by status.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="status">Status filter, or null for all.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Proposals.</returns>
        Task<List<AskActionProposal>> EnumerateByThreadAsync(string tenantId, string threadId, AskProposalStatusEnum? status, CancellationToken token = default);

        /// <summary>
        /// Read pending proposals of every tenant created before a cutoff (expiry sweep).
        /// </summary>
        /// <param name="cutoffUtc">Creation cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Pending proposals older than the cutoff.</returns>
        Task<List<AskActionProposal>> EnumeratePendingBeforeAsync(DateTime cutoffUtc, CancellationToken token = default);
    }
}
