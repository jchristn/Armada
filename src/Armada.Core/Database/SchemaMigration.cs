namespace Armada.Core.Database
{
    /// <summary>
    /// Represents a versioned database schema migration.
    /// </summary>
    public class SchemaMigration
    {
        #region Public-Members

        /// <summary>
        /// Schema version number. Must be sequential starting from 1.
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// Human-readable description of what this migration does.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// SQL statements to execute for this migration.
        /// </summary>
        public IReadOnlyList<string> Statements { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a schema migration.
        /// </summary>
        /// <param name="version">Version number (must be >= 1).</param>
        /// <param name="description">Description of the migration.</param>
        /// <param name="statements">SQL statements to execute.</param>
        public SchemaMigration(int version, string description, params string[] statements)
        {
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version), "Version must be >= 1");
            if (string.IsNullOrEmpty(description)) throw new ArgumentNullException(nameof(description));
            if (statements == null || statements.Length == 0) throw new ArgumentException("At least one SQL statement is required", nameof(statements));

            Version = version;
            Description = description;
            Statements = statements;
        }

        #endregion
    }
}
