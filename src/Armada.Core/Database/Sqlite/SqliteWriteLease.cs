namespace Armada.Core.Database.Sqlite
{
    using System;
    using System.Threading;

    /// <summary>
    /// Holds a <see cref="SqliteWriteGate"/> until disposed. Disposing releases the gate to the next queued writer;
    /// disposing again does nothing.
    /// </summary>
    public sealed class SqliteWriteLease : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Gate this lease is for.
        /// </summary>
        public SqliteWriteGate Gate
        {
            get { return _Gate; }
        }

        /// <summary>
        /// Whether the lease currently holds the gate (granted and not yet disposed).
        /// </summary>
        public bool IsActive
        {
            get { return Volatile.Read(ref _State) == _Granted; }
        }

        #endregion

        #region Internal-Members

        /// <summary>
        /// Lease the flow carried before this one was taken (possibly for another database), restored on dispose.
        /// </summary>
        internal SqliteWriteLease? Previous
        {
            get { return _Previous; }
        }

        #endregion

        #region Private-Members

        private const int _Pending = 0;
        private const int _Granted = 1;
        private const int _Released = 2;

        private readonly SqliteWriteGate _Gate;
        private readonly SqliteWriteLease? _Previous;
        private int _State = _Pending;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="gate">Gate.</param>
        /// <param name="previous">Lease the flow carried before this one.</param>
        internal SqliteWriteLease(SqliteWriteGate gate, SqliteWriteLease? previous)
        {
            _Gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _Previous = previous;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Release the gate.
        /// </summary>
        public void Dispose()
        {
            int prior = Interlocked.Exchange(ref _State, _Released);
            SqliteWriteGate.RestoreFlow(this);
            if (prior == _Granted) _Gate.Release(this);
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Mark the lease as holding the gate. Called by the gate, under its lock.
        /// </summary>
        internal void MarkGranted()
        {
            Interlocked.CompareExchange(ref _State, _Granted, _Pending);
        }

        #endregion
    }
}
