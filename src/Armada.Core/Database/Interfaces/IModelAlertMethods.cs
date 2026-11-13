namespace Armada.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for model alerts (model_alerts): one row per alert condition (tenant, profile, revision group,
    /// persona, vessel scope, rule), identified by a unique alert key. Suggested actions are computed on read and are not
    /// stored; applied actions are kept on the row as the audit record.
    /// </summary>
    public interface IModelAlertMethods
    {
        /// <summary>
        /// Create an alert. The alert key must be unique; a second alert with the same key is a duplicate.
        /// </summary>
        /// <param name="alert">Alert.</param>
        /// <param name="alertKey">Dedupe key (see ModelAlertRules.Key).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created alert.</returns>
        Task<ModelAlert> CreateAsync(ModelAlert alert, string alertKey, CancellationToken token = default);

        /// <summary>
        /// Update an alert's mutable values (everything but the id, tenant, key, profile, revision group, persona, vessel
        /// and rule).
        /// </summary>
        /// <param name="alert">Alert.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated alert.</returns>
        Task<ModelAlert> UpdateAsync(ModelAlert alert, CancellationToken token = default);

        /// <summary>
        /// Read an alert by id (any tenant).
        /// </summary>
        /// <param name="id">Alert identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The alert, or null.</returns>
        Task<ModelAlert?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read an alert by its dedupe key.
        /// </summary>
        /// <param name="alertKey">Alert key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The alert, or null.</returns>
        Task<ModelAlert?> ReadByKeyAsync(string alertKey, CancellationToken token = default);

        /// <summary>
        /// One page of alerts, most recently raised first (then id descending).
        /// </summary>
        /// <param name="query">Filters and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        Task<EnumerationResult<ModelAlert>> EnumerateAsync(ModelAlertQuery query, CancellationToken token = default);

        /// <summary>
        /// Every open alert of a tenant (null for every tenant), for the evaluation pass that resolves them.
        /// </summary>
        /// <param name="tenantId">Tenant, or null for every tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The open alerts.</returns>
        Task<List<ModelAlert>> EnumerateOpenAsync(string? tenantId, CancellationToken token = default);
    }
}
