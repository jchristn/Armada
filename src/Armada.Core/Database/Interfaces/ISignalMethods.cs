namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for signals.
    /// </summary>
    public interface ISignalMethods
    {
        /// <summary>
        /// Create a signal.
        /// </summary>
        Task<Signal> CreateAsync(Signal signal, CancellationToken token = default);

        /// <summary>
        /// Read a signal by identifier.
        /// </summary>
        Task<Signal?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate signals by recipient captain identifier.
        /// </summary>
        Task<List<Signal>> EnumerateByRecipientAsync(string captainId, bool unreadOnly = true, CancellationToken token = default);

        /// <summary>
        /// Enumerate recent signals.
        /// </summary>
        Task<List<Signal>> EnumerateRecentAsync(int count = 50, CancellationToken token = default);

        /// <summary>
        /// Mark a signal as read.
        /// </summary>
        Task MarkReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate signals with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<Signal>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);
    }
}
