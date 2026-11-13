namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for usage account memberships (usage_account_memberships): the effective-dated
    /// attribution periods that connect captains to the account group (tenant + host + runtime + account
    /// key) they captured through. Memberships are opened when an account is observed for a captain and
    /// closed on credential changes, host moves, provider identity changes or capture disable; closed rows
    /// remain as attribution history and are never rewritten to a captain's current account.
    /// </summary>
    public interface IUsageAccountMembershipMethods
    {
        /// <summary>
        /// Open (store) a membership. The stored effective-from time and all identity fields come from the
        /// caller; storage does not close or deduplicate other rows.
        /// </summary>
        /// <param name="membership">Membership to open.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored membership.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the membership or any required identity field is null or empty.</exception>
        Task<UsageAccountMembership> CreateAsync(UsageAccountMembership membership, CancellationToken token = default);

        /// <summary>
        /// Read one membership by id.
        /// </summary>
        /// <param name="id">Membership identifier.</param>
        /// <param name="tenantId">Required tenant scope.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The membership, or null.</returns>
        Task<UsageAccountMembership?> ReadAsync(string id, string tenantId, CancellationToken token = default);

        /// <summary>
        /// The captain's currently open memberships (effective-to is null), ordered by effective-from.
        /// </summary>
        /// <param name="tenantId">Required tenant scope.</param>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The open memberships, newest first.</returns>
        Task<List<UsageAccountMembership>> ReadActiveAsync(string tenantId, string captainId, CancellationToken token = default);

        /// <summary>
        /// Close every currently open membership of a captain by setting its effective-to time, and return
        /// the number closed. Idempotent: a captain with no open membership closes nothing.
        /// </summary>
        /// <param name="tenantId">Required tenant scope.</param>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="effectiveToUtc">Close time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of memberships closed.</returns>
        Task<int> CloseAsync(string tenantId, string captainId, DateTime effectiveToUtc, CancellationToken token = default);

        /// <summary>
        /// One page of memberships, newest effective-from first with a stable id tie-break.
        /// </summary>
        /// <param name="query">Filters and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        Task<EnumerationResult<UsageAccountMembership>> EnumerateAsync(UsageMembershipQuery query, CancellationToken token = default);

        /// <summary>
        /// Delete every membership of a tenant (tenant deletion cascade).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of memberships deleted.</returns>
        Task<int> DeleteByTenantAsync(string tenantId, CancellationToken token = default);
    }
}