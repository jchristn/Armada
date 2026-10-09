namespace Armada.Core.Database.Sqlite
{
    using System.Threading.Tasks;

    /// <summary>
    /// A pending <see cref="SqliteWriteGate.WhenQueuedAsync"/>: completed once the gate's queue holds at least
    /// <see cref="Count"/> writers.
    /// </summary>
    internal sealed class SqliteWriteGateQueueWatch
    {
        #region Public-Members

        /// <summary>
        /// Queue length to wait for.
        /// </summary>
        internal int Count { get; }

        /// <summary>
        /// Completed when the queue reaches <see cref="Count"/>.
        /// </summary>
        internal TaskCompletionSource<bool> Completion { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="count">Queue length to wait for.</param>
        internal SqliteWriteGateQueueWatch(int count)
        {
            Count = count;
            Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        #endregion
    }
}
