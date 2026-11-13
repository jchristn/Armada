namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for failure-cause rules (failure_cause_rules).
    /// </summary>
    public interface IFailureCauseRuleMethods
    {
        /// <summary>
        /// Create a rule.
        /// </summary>
        /// <param name="rule">Rule.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created rule.</returns>
        Task<FailureCauseRule> CreateAsync(FailureCauseRule rule, CancellationToken token = default);

        /// <summary>
        /// Read a rule by id (any tenant).
        /// </summary>
        /// <param name="id">Rule identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The rule, or null.</returns>
        Task<FailureCauseRule?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Update every field of a rule except its identifier, tenant, built-in flag and creation time; sets LastUpdateUtc.
        /// </summary>
        /// <param name="rule">Rule with updated fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated rule.</returns>
        Task<FailureCauseRule> UpdateAsync(FailureCauseRule rule, CancellationToken token = default);

        /// <summary>
        /// Delete a rule.
        /// </summary>
        /// <param name="id">Rule identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// List rules in evaluation order (Priority ascending, then Name). With a tenant, lists that tenant's rules plus
        /// the rules for every tenant (TenantId null); with null, lists every rule.
        /// </summary>
        /// <param name="tenantId">Tenant, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rules.</returns>
        Task<List<FailureCauseRule>> EnumerateAsync(string? tenantId, CancellationToken token = default);

        /// <summary>
        /// One page of the rules in evaluation order (Priority ascending, then Name, then id), paged in the database
        /// with a COUNT over the same scope. With a tenant, that tenant's rules plus the rules for every tenant; with
        /// null, every rule.
        /// </summary>
        /// <param name="tenantId">Tenant, or null.</param>
        /// <param name="query">Paging (PageNumber, PageSize 1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of rules.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<FailureCauseRule>> EnumeratePageAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default);
    }
}
