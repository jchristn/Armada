namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised at startup when a server-provider database has pending migrations,
    /// <c>Database.RequireBackupConfirmationForMigrations</c> is true, and no backup confirmation covers the target
    /// schema version. The message carries the provider's dump command and how to confirm. See docs/UPGRADING.md.
    /// </summary>
    public class MigrationBackupRequiredException : InvalidOperationException
    {
        /// <summary>
        /// Schema version the database is at.
        /// </summary>
        public int CurrentVersion { get; }

        /// <summary>
        /// Schema version the pending migrations would move the database to.
        /// </summary>
        public int TargetVersion { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Instructions: the dump command and how to confirm the backup.</param>
        /// <param name="currentVersion">Current schema version.</param>
        /// <param name="targetVersion">Target schema version.</param>
        public MigrationBackupRequiredException(string message, int currentVersion, int targetVersion)
            : base(message)
        {
            CurrentVersion = currentVersion;
            TargetVersion = targetVersion;
        }
    }
}
