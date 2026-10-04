namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Work started from an Ask Armada thread (a voyage, mission, fleet action run, job, or import batch) that the thread
    /// monitors. Unique per (thread, entity type, entity id). The work itself stays visible tenant-wide on the normal pages.
    /// </summary>
    public class AskTrackedWork
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (atw_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Tenant that owns the record.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User that owns the record (the thread owner).
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Thread (ath_ prefix).
        /// </summary>
        public string ThreadId
        {
            get => _ThreadId;
            set => _ThreadId = value ?? String.Empty;
        }

        /// <summary>
        /// Type of the tracked entity.
        /// </summary>
        public AskTrackedEntityTypeEnum EntityType { get; set; } = AskTrackedEntityTypeEnum.Voyage;

        /// <summary>
        /// Identifier of the tracked entity.
        /// </summary>
        public string EntityId
        {
            get => _EntityId;
            set => _EntityId = value ?? String.Empty;
        }

        /// <summary>
        /// Display title of the tracked entity.
        /// </summary>
        public string Title
        {
            get => _Title;
            set => _Title = value ?? String.Empty;
        }

        /// <summary>
        /// Latest known entity status string (for example InProgress), or null.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Coarse state; only Active items are watched.
        /// </summary>
        public AskTrackedWorkStateEnum State { get; set; } = AskTrackedWorkStateEnum.Active;

        /// <summary>
        /// Hash of the latest work snapshot, used to detect changes, or null.
        /// </summary>
        public string? SnapshotHash { get; set; } = null;

        /// <summary>
        /// UTC time the snapshot last changed, or null.
        /// </summary>
        public DateTime? LastChangeUtc { get; set; } = null;

        /// <summary>
        /// UTC time the work reached a terminal state, or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// UTC creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC last update time.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Latest work snapshot (populated on thread detail reads; not a column).
        /// </summary>
        public AskWorkSnapshot? Snapshot { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.AskTrackedWorkIdPrefix, 24);
        private string _ThreadId = String.Empty;
        private string _EntityId = String.Empty;
        private string _Title = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskTrackedWork()
        {
        }

        #endregion
    }
}
