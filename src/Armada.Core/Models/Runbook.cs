namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Playbook-backed executable operational runbook.
    /// </summary>
    public class Runbook
    {
        /// <summary>
        /// Runbook identifier. This matches the backing playbook ID.
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value.Trim();
            }
        }

        /// <summary>
        /// Backing playbook identifier.
        /// </summary>
        public string PlaybookId { get; set; } = String.Empty;

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owning user identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Ownership scope, mirrored from the backing playbook: tenant-wide runbooks are visible to everyone in
        /// the tenant but editable only by tenant/global admins; user-specific runbooks are owned by
        /// <see cref="UserId"/>. Defaults tenant-wide.
        /// </summary>
        public ScopeEnum Scope { get; set; } = ScopeEnum.TenantWide;

        /// <summary>
        /// Backing playbook filename.
        /// </summary>
        public string FileName { get; set; } = "RUNBOOK.md";

        /// <summary>
        /// Human-facing title.
        /// </summary>
        public string Title
        {
            get => _Title;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Title));
                _Title = value.Trim();
            }
        }

        /// <summary>
        /// Optional description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Optional bound workflow profile.
        /// </summary>
        public string? WorkflowProfileId { get; set; } = null;

        /// <summary>
        /// Optional bound environment identifier.
        /// </summary>
        public string? EnvironmentId { get; set; } = null;

        /// <summary>
        /// Optional bound environment name.
        /// </summary>
        public string? EnvironmentName { get; set; } = null;

        /// <summary>
        /// Optional suggested default check type.
        /// </summary>
        public CheckRunTypeEnum? DefaultCheckType { get; set; } = null;

        /// <summary>
        /// Runbook parameters.
        /// </summary>
        public List<RunbookParameter> Parameters { get; set; } = new List<RunbookParameter>();

        /// <summary>
        /// Explicit or derived runbook steps.
        /// </summary>
        public List<RunbookStep> Steps { get; set; } = new List<RunbookStep>();

        /// <summary>
        /// Operator-facing markdown overview and instructions.
        /// </summary>
        public string OverviewMarkdown { get; set; } = String.Empty;

        /// <summary>
        /// Whether the runbook is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Creation timestamp.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        private string _Id = String.Empty;
        private string _Title = "Runbook";
    }
}
