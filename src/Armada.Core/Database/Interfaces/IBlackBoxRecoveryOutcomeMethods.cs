namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for black box recovery outcome tracking (blackbox_recovery_outcomes).
    /// </summary>
    public interface IBlackBoxRecoveryOutcomeMethods
    {
        /// <summary>
        /// Insert an outcome row.
        /// </summary>
        /// <param name="outcome">Row.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The row.</returns>
        /// <exception cref="ArgumentNullException">Thrown when outcome is null.</exception>
        Task<BlackBoxRecoveryOutcome> CreateAsync(BlackBoxRecoveryOutcome outcome, CancellationToken token = default);

        /// <summary>
        /// Read an outcome row by id.
        /// </summary>
        /// <param name="id">Row id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The row, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<BlackBoxRecoveryOutcome?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// One page of an entry's outcome rows, newest first, paged in the database.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty or query is null.</exception>
        Task<EnumerationResult<BlackBoxRecoveryOutcome>> EnumerateByEntryAsync(string entryId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// One page of Pending outcome rows, oldest first, paged in the database.
        /// </summary>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<BlackBoxRecoveryOutcome>> EnumeratePendingAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Settle a Pending outcome. Rows that are no longer Pending are left alone.
        /// </summary>
        /// <param name="id">Row id.</param>
        /// <param name="outcome">Outcome.</param>
        /// <param name="outcomeStatus">Mission status that settled it, or null.</param>
        /// <param name="resolvedUtc">When.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the row was Pending and is now settled.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<bool> SettleAsync(string id, BlackBoxRecoveryOutcomeEnum outcome, string? outcomeStatus, DateTime resolvedUtc, CancellationToken token = default);

        /// <summary>
        /// One page of per-path statistics (grouped by rule id and path id in the database), most runs first, then path
        /// id and rule id.
        /// </summary>
        /// <param name="tenantId">Tenant scope, or null for any tenant.</param>
        /// <param name="userId">Owner scope (who ran it), or null for anyone.</param>
        /// <param name="ruleId">Rule filter, or null.</param>
        /// <param name="vesselId">Vessel filter, or null.</param>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<BlackBoxRecoveryStat>> EnumerateStatsAsync(string? tenantId, string? userId, string? ruleId, string? vesselId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Delete an entry's outcome rows.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty.</exception>
        Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default);

        /// <summary>
        /// Delete rows created before a time (statistics age out).
        /// </summary>
        /// <param name="beforeUtc">Cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteCreatedBeforeAsync(DateTime beforeUtc, CancellationToken token = default);
    }
}
