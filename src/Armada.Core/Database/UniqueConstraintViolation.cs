namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Microsoft.Data.SqlClient;
    using Microsoft.Data.Sqlite;
    using MySqlConnector;
    using Npgsql;
    using Armada.Core.Services;

    /// <summary>
    /// Recognizes a unique-constraint (or primary-key) violation reported by any of the four database providers, by
    /// the provider's error code and never by its message text, and translates it into a
    /// <see cref="DuplicateEntityException"/> whose message is Armada's own, so raw provider text never reaches API
    /// clients. Codes: SQLite <c>SqliteErrorCode</c> 19 (SQLITE_CONSTRAINT) with extended code 2067
    /// (SQLITE_CONSTRAINT_UNIQUE) or 1555 (SQLITE_CONSTRAINT_PRIMARYKEY); PostgreSQL <c>SqlState</c> 23505
    /// (unique_violation); MySQL <c>Number</c> 1062 (ER_DUP_ENTRY); SQL Server <c>Number</c> 2627 (unique or
    /// primary key constraint) or 2601 (unique index).
    /// </summary>
    public static class UniqueConstraintViolation
    {
        #region Public-Members

        /// <summary>
        /// SQLite primary result code for a constraint violation.
        /// </summary>
        public const int SqliteConstraint = 19;

        /// <summary>
        /// SQLite extended result code for a UNIQUE constraint violation.
        /// </summary>
        public const int SqliteConstraintUnique = 2067;

        /// <summary>
        /// SQLite extended result code for a PRIMARY KEY constraint violation.
        /// </summary>
        public const int SqliteConstraintPrimaryKey = 1555;

        /// <summary>
        /// PostgreSQL SQLSTATE for unique_violation.
        /// </summary>
        public const string PostgresUniqueViolation = "23505";

        /// <summary>
        /// MySQL error number ER_DUP_ENTRY.
        /// </summary>
        public const int MysqlDuplicateEntry = 1062;

        /// <summary>
        /// SQL Server error number for a violated UNIQUE or PRIMARY KEY constraint.
        /// </summary>
        public const int SqlServerUniqueConstraint = 2627;

        /// <summary>
        /// SQL Server error number for a duplicate key row in a unique index.
        /// </summary>
        public const int SqlServerUniqueIndex = 2601;

        #endregion

        #region Private-Members

        private static readonly Dictionary<string, string> _KeyHints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Fleet", "name" },
            { "Vessel", "name" },
            { "Captain", "name" },
            { "Persona", "name" },
            { "Pipeline", "name" },
            { "PromptTemplate", "name" },
            { "User", "email" },
            { "Playbook", "file name" },
            { "PushDevice", "push token" },
            { "Credential", "token" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when <paramref name="ex"/> itself is a provider unique-constraint or primary-key violation.
        /// </summary>
        /// <param name="ex">Exception.</param>
        /// <returns>True for a unique violation.</returns>
        public static bool IsUniqueViolation(Exception? ex)
        {
            if (ex == null) return false;
            if (ex is SqliteException sqlite)
            {
                return sqlite.SqliteErrorCode == SqliteConstraint
                    && (sqlite.SqliteExtendedErrorCode == SqliteConstraintUnique || sqlite.SqliteExtendedErrorCode == SqliteConstraintPrimaryKey);
            }

            if (ex is PostgresException postgres)
            {
                return String.Equals(postgres.SqlState, PostgresUniqueViolation, StringComparison.Ordinal);
            }

            if (ex is MySqlException mysql)
            {
                return mysql.Number == MysqlDuplicateEntry;
            }

            if (ex is SqlException sqlServer)
            {
                return sqlServer.Number == SqlServerUniqueConstraint || sqlServer.Number == SqlServerUniqueIndex;
            }

            return false;
        }

        /// <summary>
        /// Translate <paramref name="ex"/> (or any exception it wraps) into a <see cref="DuplicateEntityException"/>
        /// when it is, or wraps, a duplicate-entity error: an existing <see cref="DuplicateEntityException"/> is
        /// returned as is, and a provider unique-constraint violation becomes a new one for
        /// <paramref name="entityType"/>. Returns null for anything else.
        /// </summary>
        /// <param name="ex">Exception.</param>
        /// <param name="entityType">Entity type to name in the message when translating a provider error, for example
        /// Captain; null names a generic record.</param>
        /// <returns>The duplicate-entity exception, or null.</returns>
        public static DuplicateEntityException? Translate(Exception? ex, string? entityType = null)
        {
            Exception? current = ex;
            int depth = 0;
            while (current != null && depth < 16)
            {
                if (current is DuplicateEntityException existing) return existing;
                if (IsUniqueViolation(current))
                {
                    string type = String.IsNullOrEmpty(entityType) ? "Record" : entityType!;
                    return new DuplicateEntityException(type, null, null, MessageFor(type), current);
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

            return null;
        }

        /// <summary>
        /// The message used when the database reports a duplicate without saying which field: it names the entity and,
        /// for entities with a user-facing unique value, that value's kind (for example "A captain with the same name
        /// or ID already exists.").
        /// </summary>
        /// <param name="entityType">Entity type, for example Captain or VesselHealth.</param>
        /// <returns>Message.</returns>
        public static string MessageFor(string entityType)
        {
            string type = String.IsNullOrEmpty(entityType) ? "Record" : entityType;
            string label = ToLabel(type);
            string article = StartsWithVowel(label) ? "An " : "A ";
            if (_KeyHints.TryGetValue(type, out string? hint))
                return article + label + " with the same " + hint + " or ID already exists.";
            return article + label + " with the same unique key already exists.";
        }

        /// <summary>
        /// Entity type for a database method interface, for example Captain for ICaptainMethods.
        /// </summary>
        /// <param name="methodsInterface">Interface type.</param>
        /// <returns>Entity type.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="methodsInterface"/> is null.</exception>
        public static string EntityTypeFor(Type methodsInterface)
        {
            if (methodsInterface == null) throw new ArgumentNullException(nameof(methodsInterface));
            string name = methodsInterface.Name;
            if (name.Length > 1 && name[0] == 'I' && Char.IsUpper(name[1])) name = name.Substring(1);
            if (name.EndsWith("Methods", StringComparison.Ordinal) && name.Length > "Methods".Length)
                name = name.Substring(0, name.Length - "Methods".Length);
            return name;
        }

        #endregion

        #region Private-Methods

        private static string ToLabel(string pascal)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (i > 0 && Char.IsUpper(c)) sb.Append(' ');
                sb.Append(Char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }

        private static bool StartsWithVowel(string label)
        {
            if (String.IsNullOrEmpty(label)) return false;
            return "aeiou".IndexOf(label[0]) >= 0;
        }

        #endregion
    }
}
