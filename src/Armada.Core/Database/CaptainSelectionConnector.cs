namespace Armada.Core.Database
{
    using System;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database.Sqlite;

    /// <summary>
    /// Opens a provider connection for one captain selection operation, holding the SQLite write gate for writes.
    /// </summary>
    internal sealed class CaptainSelectionConnector
    {
        private readonly Func<DbConnection> _Factory;
        private readonly SqliteWriteGate? _Gate;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="factory">Creates a closed provider connection.</param>
        /// <param name="gate">SQLite write gate, or null for server providers.</param>
        /// <exception cref="ArgumentNullException">Thrown when factory is null.</exception>
        internal CaptainSelectionConnector(Func<DbConnection> factory, SqliteWriteGate? gate)
        {
            _Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _Gate = gate;
        }

        /// <summary>
        /// Run an action on an open connection.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="action">Action over the open connection.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="write">True to hold the SQLite write gate while the action runs.</param>
        /// <returns>The action's result.</returns>
        internal async Task<T> RunAsync<T>(Func<DbConnection, Task<T>> action, CancellationToken token, bool write = false)
        {
            using IDisposable? lease = write && _Gate != null ? await _Gate.EnterAsync(token).ConfigureAwait(false) : null;
            using DbConnection connection = _Factory();
            await connection.OpenAsync(token).ConfigureAwait(false);
            return await action(connection).ConfigureAwait(false);
        }
    }
}
