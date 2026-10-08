namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for Harbor job records (one row per captain launch delegated to a Harbor).
    /// </summary>
    public interface IHarborJobMethods
    {
        /// <summary>
        /// Create a job record.
        /// </summary>
        Task<HarborJobRecord> CreateAsync(HarborJobRecord record, CancellationToken token = default);

        /// <summary>
        /// Update a job record (selected by its Id).
        /// </summary>
        Task<HarborJobRecord> UpdateAsync(HarborJobRecord record, CancellationToken token = default);

        /// <summary>
        /// Read the record of a launch by its job identifier, or null.
        /// </summary>
        Task<HarborJobRecord?> ReadByJobIdAsync(string jobId, CancellationToken token = default);

        /// <summary>
        /// Enumerate a Harbor's jobs that were active or ended in a window: launched before <paramref name="toUtc"/> and
        /// not ended before <paramref name="fromUtc"/>. Oldest launch first.
        /// </summary>
        Task<List<HarborJobRecord>> EnumerateAsync(string harborId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate a Harbor's jobs that have not ended. Oldest launch first.
        /// </summary>
        Task<List<HarborJobRecord>> EnumerateOpenAsync(string harborId, CancellationToken token = default);

        /// <summary>
        /// Delete records of jobs that ended before a cutoff. Returns the number deleted.
        /// </summary>
        Task<int> DeleteEndedBeforeAsync(DateTime cutoffUtc, CancellationToken token = default);

        /// <summary>
        /// Delete every record of a Harbor. Returns the number deleted.
        /// </summary>
        Task<int> DeleteByHarborAsync(string harborId, CancellationToken token = default);
    }
}
