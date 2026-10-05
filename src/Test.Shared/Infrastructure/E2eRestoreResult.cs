namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// Body of a successful POST /api/v1/restore.
    /// </summary>
    public class E2eRestoreResult
    {
        #region Public-Members

        /// <summary>
        /// Outcome ("restored").
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Safety backup of the replaced state.
        /// </summary>
        public string? BackupPath { get; set; } = null;

        /// <summary>
        /// Schema version after the restore.
        /// </summary>
        public int SchemaVersion { get; set; } = 0;

        #endregion
    }
}
