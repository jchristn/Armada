namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for Harbor link events (connected, reconnecting, and disconnected transitions).
    /// </summary>
    public interface IHarborLinkEventMethods
    {
        /// <summary>
        /// Create an event.
        /// </summary>
        Task<HarborLinkEvent> CreateAsync(HarborLinkEvent linkEvent, CancellationToken token = default);

        /// <summary>
        /// Enumerate a Harbor's events that occurred in [fromUtc, toUtc). Oldest first.
        /// </summary>
        Task<List<HarborLinkEvent>> EnumerateAsync(string harborId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default);

        /// <summary>
        /// Read a Harbor's latest event that occurred before a time, or null. Gives the link state a window opens with.
        /// </summary>
        Task<HarborLinkEvent?> ReadLatestBeforeAsync(string harborId, DateTime beforeUtc, CancellationToken token = default);

        /// <summary>
        /// Delete a Harbor's events that occurred before a time. Returns the number deleted.
        /// </summary>
        Task<int> DeleteBeforeAsync(string harborId, DateTime beforeUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate the identifiers of every Harbor that has link events.
        /// </summary>
        Task<List<string>> EnumerateHarborIdsAsync(CancellationToken token = default);

        /// <summary>
        /// Delete every event of a Harbor. Returns the number deleted.
        /// </summary>
        Task<int> DeleteByHarborAsync(string harborId, CancellationToken token = default);
    }
}
