namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for CLI permission rules (cli_permission_rules).
    /// </summary>
    public interface ICliPermissionRuleMethods
    {
        /// <summary>
        /// Create a rule.
        /// </summary>
        /// <param name="rule">Rule.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created rule.</returns>
        Task<CliPermissionRule> CreateAsync(CliPermissionRule rule, CancellationToken token = default);

        /// <summary>
        /// Read a rule by id (any tenant).
        /// </summary>
        /// <param name="id">Rule identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The rule, or null.</returns>
        Task<CliPermissionRule?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Update the pattern, action, and description of a rule.
        /// </summary>
        /// <param name="rule">Rule with updated fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated rule.</returns>
        Task<CliPermissionRule> UpdateAsync(CliPermissionRule rule, CancellationToken token = default);

        /// <summary>
        /// Delete a rule.
        /// </summary>
        /// <param name="id">Rule identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// List rules, newest first.
        /// </summary>
        /// <param name="query">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rules.</returns>
        Task<List<CliPermissionRule>> EnumerateAsync(CliPermissionRuleQuery query, CancellationToken token = default);

        /// <summary>
        /// The rules that apply to a captain launch: rules for every tenant, Global rules of the tenant, Vessel rules of
        /// the vessel (when given), and Captain rules of the captain.
        /// </summary>
        /// <param name="tenantId">Tenant of the mission or thread.</param>
        /// <param name="captainId">Captain.</param>
        /// <param name="vesselId">Vessel, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Applicable rules.</returns>
        Task<List<CliPermissionRule>> EnumerateApplicableAsync(string? tenantId, string? captainId, string? vesselId, CancellationToken token = default);
    }
}
