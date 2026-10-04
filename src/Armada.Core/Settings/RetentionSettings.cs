namespace Armada.Core.Settings
{
    /// <summary>
    /// Retention for data that otherwise grows without bound: Ask Armada threads, finished background jobs, and
    /// finished vessel import batches. Pruning runs in the Admiral's health-check loop on its slow cadence (every
    /// 100 health-check cycles). All values apply live. For every setting, 0 means never; values are clamped to
    /// 0..3650 days. Fleet action runs are governed by <see cref="FleetActionSettings.RunRetentionDays"/> and
    /// request history by <see cref="ArmadaSettings.RequestHistoryRetentionDays"/>.
    /// </summary>
    public class RetentionSettings
    {
        #region Public-Members

        /// <summary>
        /// Archive Ask threads with no activity (last message, or creation when empty) for this many days. Pinned
        /// threads are never archived. Archived threads stay readable and searchable. Default 90; 0 never archives;
        /// maximum 3650.
        /// </summary>
        public int AskThreadArchiveAfterDays
        {
            get => _AskThreadArchiveAfterDays;
            set => _AskThreadArchiveAfterDays = Clamp(value);
        }

        /// <summary>
        /// Permanently delete Ask threads (with their messages, tool calls, proposals, and tracked work) that have had
        /// no activity for this many days, archived or not. Pinned threads are never deleted. Default 0 (never);
        /// maximum 3650.
        /// </summary>
        public int AskThreadDeleteAfterDays
        {
            get => _AskThreadDeleteAfterDays;
            set => _AskThreadDeleteAfterDays = Clamp(value);
        }

        /// <summary>
        /// Delete finished background jobs (Succeeded, Failed, Cancelled) that completed more than this many days
        /// ago. The newest finished job of each kind and name per tenant is always kept, because schedules (for
        /// example the vessel health interval) measure elapsed time from it. Default 30; 0 never deletes; maximum 3650.
        /// </summary>
        public int JobRetentionDays
        {
            get => _JobRetentionDays;
            set => _JobRetentionDays = Clamp(value);
        }

        /// <summary>
        /// Delete finished vessel import batches (Completed, CompletedWithFailures, Failed), with their candidate
        /// items and fleet recommendations, last updated more than this many days ago. Imported vessels are not
        /// affected. Default 90; 0 never deletes; maximum 3650.
        /// </summary>
        public int ImportBatchRetentionDays
        {
            get => _ImportBatchRetentionDays;
            set => _ImportBatchRetentionDays = Clamp(value);
        }

        #endregion

        #region Private-Members

        private int _AskThreadArchiveAfterDays = 90;
        private int _AskThreadDeleteAfterDays = 0;
        private int _JobRetentionDays = 30;
        private int _ImportBatchRetentionDays = 90;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public RetentionSettings()
        {
        }

        #endregion

        #region Private-Methods

        private static int Clamp(int value)
        {
            return value < 0 ? 0 : (value > 3650 ? 3650 : value);
        }

        #endregion
    }
}
