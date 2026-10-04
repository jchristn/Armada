namespace Armada.Core.Services
{
    /// <summary>
    /// Counts from one <see cref="RetentionService.PruneAsync"/> pass.
    /// </summary>
    public class RetentionResult
    {
        /// <summary>
        /// Ask threads archived for inactivity.
        /// </summary>
        public int AskThreadsArchived { get; set; } = 0;

        /// <summary>
        /// Ask threads deleted for inactivity (with their messages and related rows).
        /// </summary>
        public int AskThreadsDeleted { get; set; } = 0;

        /// <summary>
        /// Finished background jobs deleted.
        /// </summary>
        public int JobsDeleted { get; set; } = 0;

        /// <summary>
        /// Finished vessel import batches deleted (with their items and recommendations).
        /// </summary>
        public int ImportBatchesDeleted { get; set; } = 0;

        /// <summary>
        /// Total rows (threads, jobs, batches) affected.
        /// </summary>
        public int Total
        {
            get { return AskThreadsArchived + AskThreadsDeleted + JobsDeleted + ImportBatchesDeleted; }
        }
    }
}
