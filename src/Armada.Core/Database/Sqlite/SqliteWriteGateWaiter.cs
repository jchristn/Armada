namespace Armada.Core.Database.Sqlite
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A writer queued on a <see cref="SqliteWriteGate"/>: its lease, the task completed when the gate is handed to
    /// it, its place in the queue (null once granted or cancelled), and its cancellation registration.
    /// </summary>
    internal sealed class SqliteWriteGateWaiter
    {
        #region Public-Members

        /// <summary>
        /// Lease granted to this writer.
        /// </summary>
        internal SqliteWriteLease Lease { get; }

        /// <summary>
        /// Completed with the lease when the gate is handed to this writer.
        /// </summary>
        internal TaskCompletionSource<SqliteWriteLease> Completion { get; }

        /// <summary>
        /// Node in the gate's queue, or null once the writer was granted the gate or cancelled.
        /// </summary>
        internal LinkedListNode<SqliteWriteGateWaiter>? Node { get; set; }

        /// <summary>
        /// Cancellation registration that removes the writer from the queue.
        /// </summary>
        internal CancellationTokenRegistration Registration { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="lease">Lease to grant.</param>
        internal SqliteWriteGateWaiter(SqliteWriteLease lease)
        {
            Lease = lease;
            Completion = new TaskCompletionSource<SqliteWriteLease>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        #endregion
    }
}
