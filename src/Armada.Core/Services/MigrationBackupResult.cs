namespace Armada.Core.Services
{
    using Armada.Core.Enums;

    /// <summary>
    /// Outcome of the startup pre-migration check performed by <see cref="MigrationBackupService"/>.
    /// </summary>
    public class MigrationBackupResult
    {
        /// <summary>
        /// Database provider.
        /// </summary>
        public DatabaseTypeEnum Provider { get; set; } = DatabaseTypeEnum.Sqlite;

        /// <summary>
        /// Schema version found in the database (0 for a new, empty database).
        /// </summary>
        public int CurrentVersion { get; set; } = 0;

        /// <summary>
        /// Latest schema version this build migrates to.
        /// </summary>
        public int TargetVersion { get; set; } = 0;

        /// <summary>
        /// Whether startup is about to apply migrations to an existing database.
        /// </summary>
        public bool MigrationsPending { get; set; } = false;

        /// <summary>
        /// SQLite only: directory holding the copy taken before migrating, or null when no copy was taken.
        /// </summary>
        public string? BackupDirectory { get; set; } = null;

        /// <summary>
        /// Server providers only: the command the operator should run to dump the database before migrating, or
        /// null when nothing is pending.
        /// </summary>
        public string? DumpCommand { get; set; } = null;

        /// <summary>
        /// Number of older pre-migration backups deleted by retention.
        /// </summary>
        public int BackupsPruned { get; set; } = 0;
    }
}
