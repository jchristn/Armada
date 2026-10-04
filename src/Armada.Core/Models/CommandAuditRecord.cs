namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Payload of an <c>audit.command</c> event: one shell command or process Armada ran on a user's behalf.
    /// </summary>
    public class CommandAuditRecord
    {
        #region Public-Members

        /// <summary>
        /// What ran the command, for example WorkspaceExec, FleetAction, CheckRun, HarborProbe, MergeQueueTest.
        /// </summary>
        public string Source { get; set; } = "";

        /// <summary>
        /// Command text (secret-looking values redacted, truncated to 4,000 characters).
        /// </summary>
        public string Command { get; set; } = "";

        /// <summary>
        /// Working directory, when known.
        /// </summary>
        public string? WorkingDirectory { get; set; } = null;

        /// <summary>
        /// Where the command ran: Admiral, or a Harbor id.
        /// </summary>
        public string Host { get; set; } = "Admiral";

        /// <summary>
        /// Tenant of the caller or of the resource.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User who caused the command (null for scheduled or system-initiated runs).
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Vessel the command ran against, when any.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Entity type of the record that owns the command (FleetActionRun, CheckRun, MergeEntry, Harbor).
        /// </summary>
        public string? EntityType { get; set; } = null;

        /// <summary>
        /// Entity id of the record that owns the command.
        /// </summary>
        public string? EntityId { get; set; } = null;

        /// <summary>
        /// When the command was started.
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
