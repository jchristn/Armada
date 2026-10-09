namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// A request-history insert queued for the SQLite group-commit flusher, with the task completed once its row is
    /// committed (or faulted with the error that stopped it).
    /// </summary>
    internal sealed class RequestHistoryPendingInsert
    {
        #region Public-Members

        /// <summary>
        /// Entry to insert.
        /// </summary>
        internal RequestHistoryEntry Entry { get; }

        /// <summary>
        /// Detail to insert with the entry, if any.
        /// </summary>
        internal RequestHistoryDetail? Detail { get; }

        /// <summary>
        /// Completed when the row is committed.
        /// </summary>
        internal TaskCompletionSource<bool> Completion { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="detail">Detail, or null.</param>
        internal RequestHistoryPendingInsert(RequestHistoryEntry entry, RequestHistoryDetail? detail)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            Detail = detail;
            Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        #endregion
    }
}
