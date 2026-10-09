namespace Armada.Core.Database.Sqlite
{
    using System;
    using System.Data;
    using System.Runtime.CompilerServices;
    using Microsoft.Data.Sqlite;
    using SQLitePCL;

    /// <summary>
    /// A SQLite connection opened by Armada's SQLite provider. On every open it makes sure the native connection has
    /// the provider's per-connection settings, and it reports each committed write transaction to the database's
    /// <see cref="SqliteWriteGate"/> so a write that bypasses the gate is counted (or, under
    /// <see cref="SqliteWriteGate.StrictAudit"/>, refused).
    /// <para>
    /// Per-connection settings, applied once per native connection (pooled connections keep them):
    /// busy_timeout, so a writer from another process is waited for inside SQLite instead of in
    /// Microsoft.Data.Sqlite's 150 ms sleep-and-retry loop; and synchronous=NORMAL, the recommended setting for WAL
    /// mode (commits no longer flush the WAL to disk; the database stays consistent after a power loss, which can
    /// only roll back the most recent commits). journal_mode=WAL is persistent in the database file and is set by
    /// <see cref="SqliteDatabaseDriver.InitializeAsync"/>.
    /// </para>
    /// </summary>
    public class SqliteProviderConnection : SqliteConnection
    {
        #region Public-Members

        /// <summary>
        /// busy_timeout applied to every provider connection, in milliseconds.
        /// </summary>
        public const int BusyTimeoutMs = 5000;

        /// <summary>
        /// Write gate of the database this connection points at.
        /// </summary>
        public SqliteWriteGate WriteGate
        {
            get { return _Gate; }
        }

        #endregion

        #region Private-Members

        private static readonly ConditionalWeakTable<sqlite3, object> _ConfiguredHandles = new ConditionalWeakTable<sqlite3, object>();
        private static readonly object _Configured = new object();
        private static readonly delegate_commit _CommitHook = OnCommit;

        private readonly SqliteWriteGate _Gate;
        private sqlite3? _HookedHandle = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">SQLite connection string.</param>
        public SqliteProviderConnection(string connectionString) : base(connectionString)
        {
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));
            _Gate = SqliteWriteGate.ForConnectionString(connectionString);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Open the connection, apply the per-connection settings, and install the write audit.
        /// </summary>
        public override void Open()
        {
            base.Open();

            sqlite3? handle = Handle;
            if (handle == null) return;

            if (!_ConfiguredHandles.TryGetValue(handle, out object? _))
            {
                raw.sqlite3_busy_timeout(handle, BusyTimeoutMs);
                raw.sqlite3_exec(handle, "PRAGMA synchronous=NORMAL;");
                _ConfiguredHandles.AddOrUpdate(handle, _Configured);
            }

            raw.sqlite3_commit_hook(handle, _CommitHook, _Gate);
            _HookedHandle = handle;
        }

        /// <summary>
        /// Remove the write audit (the native connection may go back to the pool and be reused by other code), then
        /// close the connection.
        /// </summary>
        public override void Close()
        {
            sqlite3? handle = _HookedHandle;
            _HookedHandle = null;
            if (handle != null && State == ConnectionState.Open)
            {
                raw.sqlite3_commit_hook(handle, null, null);
            }

            base.Close();
        }

        #endregion

        #region Private-Methods

        private static int OnCommit(object userData)
        {
            SqliteWriteGate? gate = userData as SqliteWriteGate;
            return gate == null ? 0 : gate.OnCommit();
        }

        #endregion
    }
}
