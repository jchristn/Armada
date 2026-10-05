namespace Armada.Core.Models
{
    using System;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// One permission prompt a CLI captain raised for one of its own tools (for example a Bash command) while running a
    /// mission or an Ask turn under <see cref="CliPermissionPolicyEnum.ApproveInArmada"/>. The captain waits until an
    /// approver allows or denies it, a rule decides it, or it expires. The stored input is redacted for display; the
    /// original input is held only in memory by the waiting call and is never persisted.
    /// </summary>
    public class CliPermissionRequest
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (cpr_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Tenant of the mission or thread.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owner of the mission or thread (the user the captain works for).
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Captain (cpt_ prefix) that asked.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Mission (msn_ prefix) the captain was running, or null for an Ask turn.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Voyage (vyg_ prefix) of the mission, or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Vessel (vsl_ prefix) of the mission, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Ask thread (ath_ prefix) of the turn, or null for a mission.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// Ask message (amg_ prefix) that renders the request card in the thread, or null.
        /// </summary>
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// Runtime of the captain.
        /// </summary>
        public AgentRuntimeEnum Runtime { get; set; } = AgentRuntimeEnum.ClaudeCode;

        /// <summary>
        /// Tool name as the CLI reported it (for example Bash, WebFetch, Edit, or mcp__server__tool).
        /// </summary>
        public string ToolName
        {
            get => _ToolName;
            set => _ToolName = value ?? String.Empty;
        }

        /// <summary>
        /// Tool input as JSON object text with secrets redacted (display only).
        /// </summary>
        public string InputText
        {
            get => _InputText;
            set => _InputText = String.IsNullOrWhiteSpace(value) ? "{}" : value;
        }

        /// <summary>
        /// One-line description of what the tool would do (the command, URL, or path), redacted.
        /// </summary>
        public string SummaryText
        {
            get => _SummaryText;
            set => _SummaryText = value ?? String.Empty;
        }

        /// <summary>
        /// Suggested allow rule in Claude Code permission rule syntax (for example <c>Bash(git status:*)</c>), used as
        /// the starting pattern of "allow and remember".
        /// </summary>
        public string? SuggestedRule { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public CliPermissionRequestStatusEnum Status { get; set; } = CliPermissionRequestStatusEnum.Pending;

        /// <summary>
        /// What decided the request, or null while pending.
        /// </summary>
        public CliPermissionDecisionSourceEnum? DecisionSource { get; set; } = null;

        /// <summary>
        /// Rule (cpl_ prefix) that decided the request, or that "allow and remember" created, or null.
        /// </summary>
        public string? RuleId { get; set; } = null;

        /// <summary>
        /// User who decided, or null.
        /// </summary>
        public string? DecidedByUserId { get; set; } = null;

        /// <summary>
        /// Message returned to the captain with a denial (or the approver's note), or null.
        /// </summary>
        public string? DecisionMessage { get; set; } = null;

        /// <summary>
        /// UTC time the request expires while pending.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow.AddMinutes(10);

        /// <summary>
        /// UTC time of the decision, or null.
        /// </summary>
        public DateTime? DecidedUtc { get; set; } = null;

        /// <summary>
        /// UTC creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC last update time.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Captain name (filled on read; not persisted).
        /// </summary>
        public string? CaptainName { get; set; } = null;

        /// <summary>
        /// Vessel name (filled on read; not persisted).
        /// </summary>
        public string? VesselName { get; set; } = null;

        /// <summary>
        /// Mission title (filled on read; not persisted).
        /// </summary>
        public string? MissionTitle { get; set; } = null;

        /// <summary>
        /// Ask thread title (filled on read for callers who own the thread; not persisted).
        /// </summary>
        public string? ThreadTitle { get; set; } = null;

        /// <summary>
        /// Whether the caller who read the request may decide it (filled on read; not persisted).
        /// </summary>
        public bool CanDecide { get; set; } = false;

        /// <summary>
        /// Whether the caller may also store an allow rule with the decision (admins; filled on read; not persisted).
        /// </summary>
        public bool CanRemember { get; set; } = false;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.CliPermissionRequestIdPrefix, 24);
        private string _ToolName = String.Empty;
        private string _InputText = "{}";
        private string _SummaryText = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliPermissionRequest()
        {
        }

        #endregion
    }
}
