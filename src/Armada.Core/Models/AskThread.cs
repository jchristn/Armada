namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// A private Ask Armada conversation owned by one user. Threads keep their own message history, captain,
    /// auto-approve setting, and the work started from them.
    /// </summary>
    public class AskThread
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (ath_ prefix).
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
        /// Thread title. Never null; defaults to "New conversation" and is truncated to 200 characters.
        /// </summary>
        public string Title
        {
            get => _Title;
            set => _Title = String.IsNullOrWhiteSpace(value) ? "New conversation" : (value.Trim().Length > 200 ? value.Trim().Substring(0, 200) : value.Trim());
        }

        /// <summary>
        /// Captain (cpt_ prefix) that answers in this thread, or null when no captain is selected.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// When true, state-changing tool calls run without a confirm card (still recorded as Executed proposals). Default false.
        /// </summary>
        public bool AutoApprove { get; set; } = false;

        /// <summary>
        /// Latest conversation summary, used as context when older history is trimmed, or null.
        /// </summary>
        public string? SummaryText { get; set; } = null;

        /// <summary>
        /// UTC time of the latest summary, or null.
        /// </summary>
        public DateTime? SummaryUtc { get; set; } = null;

        /// <summary>
        /// Whether the thread is pinned to the top of the list. Default false.
        /// </summary>
        public bool Pinned { get; set; } = false;

        /// <summary>
        /// Whether the thread is archived (hidden from the list unless requested). Default false.
        /// </summary>
        public bool Archived { get; set; } = false;

        /// <summary>
        /// UTC time of the latest message, or null when the thread has none.
        /// </summary>
        public DateTime? LastMessageUtc { get; set; } = null;

        /// <summary>
        /// Number of messages in the thread. Minimum 0.
        /// </summary>
        public int MessageCount
        {
            get => _MessageCount;
            set => _MessageCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Messages posted by the captain or Armada since the owner last marked the thread read. Minimum 0.
        /// </summary>
        public int UnreadCount
        {
            get => _UnreadCount;
            set => _UnreadCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of tracked work items still Active (computed on read; not persisted).
        /// </summary>
        public int ActiveWorkCount
        {
            get => _ActiveWorkCount;
            set => _ActiveWorkCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Whether a captain turn is currently running in this thread (computed on read; not persisted).
        /// </summary>
        public bool TurnRunning { get; set; } = false;

        /// <summary>
        /// UTC creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC last update time.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.AskThreadIdPrefix, 24);
        private string _Title = "New conversation";
        private int _MessageCount = 0;
        private int _UnreadCount = 0;
        private int _ActiveWorkCount = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskThread()
        {
        }

        #endregion
    }
}
