namespace Armada.Core.Database
{
    using System;
    using System.Data.Common;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// Recognizes a database error that is worth one retry because the same statement may succeed a moment later, by
    /// the provider's error code or its own transient flag and never by message text: SQLite <c>SqliteErrorCode</c> 5
    /// (SQLITE_BUSY, "database is locked" after the busy timeout) or 6 (SQLITE_LOCKED); a deadlock or lock timeout by its
    /// provider code (SQL Server 1205 and 1222, MySQL 1213 and 1205, PostgreSQL 40P01 and 40001); any provider exception whose
    /// <see cref="DbException.IsTransient"/> is true (PostgreSQL, MySQL, and SQL Server set it for deadlocks, lock
    /// timeouts, and dropped connections); and a <see cref="TimeoutException"/>.
    /// </summary>
    public static class TransientDatabaseError
    {
        #region Public-Members

        /// <summary>
        /// SQLite primary result code SQLITE_BUSY.
        /// </summary>
        public const int SqliteBusy = 5;

        /// <summary>
        /// SQLite primary result code SQLITE_LOCKED.
        /// </summary>
        public const int SqliteLocked = 6;

        /// <summary>
        /// SQL Server error number for a transaction chosen as a deadlock victim.
        /// </summary>
        public const int SqlServerDeadlockVictim = 1205;

        /// <summary>
        /// SQL Server error number for an exceeded lock request timeout.
        /// </summary>
        public const int SqlServerLockTimeout = 1222;

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when <paramref name="ex"/>, or an exception it wraps, is a transient database error.
        /// </summary>
        /// <param name="ex">Exception.</param>
        /// <returns>True for a transient error.</returns>
        public static bool IsTransient(Exception? ex)
        {
            Exception? current = ex;
            int depth = 0;
            while (current != null && depth < 16)
            {
                if (current is SqliteException sqlite)
                {
                    if (sqlite.SqliteErrorCode == SqliteBusy || sqlite.SqliteErrorCode == SqliteLocked) return true;
                }
                else if (current is Microsoft.Data.SqlClient.SqlException sqlServer)
                {
                    // Deadlock victim (1205) and lock request timeout (1222) are retryable whatever IsTransient reports.
                    if (sqlServer.Number == SqlServerDeadlockVictim || sqlServer.Number == SqlServerLockTimeout || sqlServer.IsTransient) return true;
                }
                else if (current is MySqlConnector.MySqlException mysql)
                {
                    if (mysql.ErrorCode == MySqlConnector.MySqlErrorCode.LockDeadlock || mysql.ErrorCode == MySqlConnector.MySqlErrorCode.LockWaitTimeout || mysql.IsTransient) return true;
                }
                else if (current is Npgsql.PostgresException postgres)
                {
                    if (postgres.SqlState == Npgsql.PostgresErrorCodes.DeadlockDetected || postgres.SqlState == Npgsql.PostgresErrorCodes.SerializationFailure || postgres.IsTransient) return true;
                }
                else if (current is DbException db)
                {
                    if (db.IsTransient) return true;
                }
                else if (current is TimeoutException)
                {
                    return true;
                }

                if (current is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
                {
                    current = aggregate.InnerExceptions[0];
                }
                else
                {
                    current = current.InnerException;
                }

                depth++;
            }

            return false;
        }

        #endregion
    }
}
