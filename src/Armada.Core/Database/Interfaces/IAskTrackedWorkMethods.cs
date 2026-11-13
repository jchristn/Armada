namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for work tracked by Ask Armada threads (ask_tracked_work). A thread tracks an entity at most
    /// once (unique on thread, entity type, entity id).
    /// </summary>
    public interface IAskTrackedWorkMethods
    {
        /// <summary>
        /// Start tracking an entity in a thread, or return the existing row when the thread already tracks it.
        /// </summary>
        /// <param name="work">Row to create; TenantId, ThreadId, and EntityId are required.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created or existing row.</returns>
        Task<AskTrackedWork> CreateOrGetAsync(AskTrackedWork work, CancellationToken token = default);

        /// <summary>
        /// Read a tracked work row.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Tracked work identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The row, or null.</returns>
        Task<AskTrackedWork?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update title, status, state, snapshot hash, and change/completion times.
        /// </summary>
        /// <param name="work">Row with updated fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated row.</returns>
        Task<AskTrackedWork> UpdateAsync(AskTrackedWork work, CancellationToken token = default);

        /// <summary>
        /// Read a thread's tracked work, newest first.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tracked work rows.</returns>
        Task<List<AskTrackedWork>> EnumerateByThreadAsync(string tenantId, string threadId, CancellationToken token = default);

        /// <summary>
        /// Read every Active row of every tenant (tracker sweep).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Active rows, oldest first.</returns>
        Task<List<AskTrackedWork>> EnumerateActiveAsync(CancellationToken token = default);

        /// <summary>
        /// Read the Active rows tracking one entity (change-driven updates).
        /// </summary>
        /// <param name="entityType">Entity type.</param>
        /// <param name="entityId">Entity identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Matching Active rows.</returns>
        Task<List<AskTrackedWork>> EnumerateActiveByEntityAsync(AskTrackedEntityTypeEnum entityType, string entityId, CancellationToken token = default);

        /// <summary>
        /// Read one page of a thread's tracked work. CreatedDescending (the default) is newest first; the id breaks
        /// ties. The page is cut in the database and TotalRecords counts every row of the thread.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="state">Only rows in this state, or null for every state.</param>
        /// <param name="query">Page number, page size (1 to 1000), and order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of tracked work rows.</returns>
        Task<EnumerationResult<AskTrackedWork>> EnumeratePageByThreadAsync(string tenantId, string threadId, AskTrackedWorkStateEnum? state, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Count a thread's tracked work rows.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="state">Only rows in this state, or null for every state.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of rows.</returns>
        Task<long> CountByThreadAsync(string tenantId, string threadId, AskTrackedWorkStateEnum? state, CancellationToken token = default);
    }
}
