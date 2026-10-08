namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for Harbor link-health samples (one row per Harbor per minute of heartbeats).
    /// </summary>
    public interface IHarborLinkSampleMethods
    {
        /// <summary>
        /// Create a sample.
        /// </summary>
        Task<HarborLinkSample> CreateAsync(HarborLinkSample sample, CancellationToken token = default);

        /// <summary>
        /// Enumerate a Harbor's samples whose minute starts in [fromUtc, toUtc). Oldest first.
        /// </summary>
        Task<List<HarborLinkSample>> EnumerateAsync(string harborId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default);

        /// <summary>
        /// Read a Harbor's most recent sample, or null.
        /// </summary>
        Task<HarborLinkSample?> ReadLatestAsync(string harborId, CancellationToken token = default);

        /// <summary>
        /// Delete samples whose minute started before a cutoff. Returns the number deleted.
        /// </summary>
        Task<int> DeleteBeforeAsync(DateTime cutoffUtc, CancellationToken token = default);

        /// <summary>
        /// Delete every sample of a Harbor. Returns the number deleted.
        /// </summary>
        Task<int> DeleteByHarborAsync(string harborId, CancellationToken token = default);
    }
}
