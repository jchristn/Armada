namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>Tenant-scoped immutable selection history and compare-and-set budget lifecycle operations.</summary>
    public interface ICaptainSelectionMethods
    {
        /// <summary>Atomically claim a captain/mission and store Observe-mode history. Only a new Claimed receipt permits launch; replay returns AlreadyCommitted. Enforce mode is rejected until budget revalidation is integrated.</summary>
        Task<CaptainSelectionAdmissionReceipt> TryAdmitObservedAsync(CaptainSelectionRecordSet records, string? dockId = null, int ceiling = 256, CancellationToken token = default);
        /// <summary>Atomically persist a complete decision aggregate. Replaying its attempt returns the original decision.</summary>
        Task<CaptainSelectionDecision> CreateDecisionAsync(CaptainSelectionRecordSet records, CancellationToken token = default);
        /// <summary>Read a decision by tenant and optional mission-owner scope. Null owner is reserved for authorized administrators.</summary>
        Task<CaptainSelectionDecision?> ReadDecisionAsync(string tenantId, string decisionId, string? ownerUserId = null, CancellationToken token = default);
        /// <summary>Read the immutable effective policy referenced by an authorized decision.</summary>
        Task<CaptainSelectionPolicyRevision?> ReadPolicyAsync(string tenantId, string policyId, CancellationToken token = default);
        /// <summary>Page a visible mission's history in descending evaluation/ID order; page size 1..1000.</summary>
        Task<EnumerationResult<CaptainSelectionDecision>> EnumerateByMissionAsync(string tenantId, string missionId, string? ownerUserId = null, int pageNumber = 1, int pageSize = 25, CancellationToken token = default);
        /// <summary>Page candidate rows after mission and candidate-owner scope, before totals. Null owner scope is administrator-only.</summary>
        Task<EnumerationResult<CaptainSelectionCandidateRecord>> EnumerateCandidatesAsync(string tenantId, string decisionId, string? ownerUserId = null, string? candidateOwnerUserId = null, int pageNumber = 1, int pageSize = 25, CancellationToken token = default);
        /// <summary>Read one authorized candidate's immutable windows; never perform an unscoped lookup.</summary>
        Task<List<CaptainSelectionWindowRecord>> ReadWindowsAsync(string tenantId, string decisionId, string candidateId, string? ownerUserId = null, string? candidateOwnerUserId = null, CancellationToken token = default);
        /// <summary>Read immutable reasons of one authorized candidate.</summary>
        Task<List<CaptainSelectionReason>> ReadReasonsAsync(string tenantId, string decisionId, string candidateId, string? ownerUserId = null, string? candidateOwnerUserId = null, CancellationToken token = default);
        /// <summary>Read a bounded page of execution evidence for an authorized decision, ordered oldest first.</summary>
        Task<EnumerationResult<CaptainSelectionExecutionEvent>> EnumerateExecutionEventsAsync(string tenantId, string decisionId, string? ownerUserId = null, int pageNumber = 1, int pageSize = 25, CancellationToken token = default);
        /// <summary>Read active/unsettled/uncertain reservations of one verified tenant/account identity.</summary>
        Task<List<UsageBudgetReservation>> ReadReservationsAsync(string tenantId, string hostId, string runtime, string accountKey, CancellationToken token = default);
        /// <summary>Transition a reservation only when its tenant, state and revision match; invalid lifecycle edges are rejected.</summary>
        Task<bool> TryTransitionReservationAsync(string tenantId, string reservationId, int revision, UsageReservationStateEnum expected, UsageReservationStateEnum next, DateTime atUtc, string reason, string? snapshotId = null, CancellationToken token = default);
        /// <summary>Append actual-model/failure evidence without rewriting the selection decision.</summary>
        Task AppendExecutionEventAsync(CaptainSelectionExecutionEvent entry, CancellationToken token = default);
        /// <summary>Prune a bounded page of expired history, preserving decisions with live/unsettled reservations.</summary>
        Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize = 100, CancellationToken token = default);
        /// <summary>Delete one tenant's selection records in child-before-parent order.</summary>
        Task<int> DeleteByTenantAsync(string tenantId, CancellationToken token = default);
    }
}
