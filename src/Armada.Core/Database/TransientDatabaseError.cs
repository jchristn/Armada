namespace Armada.Core.Database
{
    using System;
    using System.Data.Common;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// Recognizes a database error that is worth one retry because the same statement may succeed a moment later, by
    /// the provider's error code or its own transient flag and never by message text: SQLite <c>SqliteErrorCode</c> 5
    /// (SQLITE_BUSY, "database is locked" after the busy timeout) or 6 (SQLITE_LOCKED); any provider exception whose
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
