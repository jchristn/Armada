namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// A tool call made by the captain during an Ask Armada turn, persisted with the assistant message. ArgumentsText and
    /// ResultText are unmanaged text (tool payloads).
    /// </summary>
    public class AskMessageToolCall
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (atc_ prefix).
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
        /// Assistant message (amg_ prefix) the call belongs to.
        /// </summary>
        public string MessageId
        {
            get => _MessageId;
            set => _MessageId = value ?? String.Empty;
        }

        /// <summary>
        /// Thread (ath_ prefix).
        /// </summary>
        public string ThreadId
        {
            get => _ThreadId;
            set => _ThreadId = value ?? String.Empty;
        }

        /// <summary>
        /// Runtime-assigned call identifier, or null.
        /// </summary>
        public string? CallId { get; set; } = null;

        /// <summary>
        /// Tool name as the runtime reported it.
        /// </summary>
        public string ToolName
        {
            get => _ToolName;
            set => _ToolName = value ?? String.Empty;
        }

        /// <summary>
        /// Tool arguments (JSON text), or null.
        /// </summary>
        public string? ArgumentsText { get; set; } = null;

        /// <summary>
        /// Tool result text, or null.
        /// </summary>
        public string? ResultText { get; set; } = null;

        /// <summary>
        /// Whether the call succeeded, or null when unknown.
        /// </summary>
        public bool? Ok { get; set; } = null;

        /// <summary>
        /// Call duration in milliseconds, or null.
        /// </summary>
        public long? ElapsedMs { get; set; } = null;

        /// <summary>
        /// True when the CLI refused the call because it needed permission that the turn's CLI tool permission policy
        /// did not grant (from the runtime's typed permission denial report), or null when unknown.
        /// </summary>
        public bool? PermissionDenied { get; set; } = null;

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

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.AskToolCallIdPrefix, 24);
        private string _MessageId = String.Empty;
        private string _ThreadId = String.Empty;
        private string _ToolName = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskMessageToolCall()
        {
        }

        #endregion
    }
}
