namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Runbook create or update payload.
    /// </summary>
    public class RunbookUpsertRequest
    {
        /// <summary>
        /// Optional backing filename.
        /// </summary>
        public string? FileName { get; set; } = null;

        /// <summary>
        /// Optional title.
        /// </summary>
        public string? Title { get; set; } = null;

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
        /// Optional default check type.
        /// </summary>
        public CheckRunTypeEnum? DefaultCheckType { get; set; } = null;

        /// <summary>
        /// Parameter definitions.
        /// </summary>
        public List<RunbookParameter>? Parameters { get; set; } = null;

        /// <summary>
        /// Explicit runbook steps.
        /// </summary>
        public List<RunbookStep>? Steps { get; set; } = null;

        /// <summary>
        /// Runbook overview and instructions markdown.
        /// </summary>
        public string? OverviewMarkdown { get; set; } = null;

        /// <summary>
        /// Optional active flag.
        /// </summary>
        public bool? Active { get; set; } = null;

        /// <summary>
        /// Optional requested ownership scope (tenant-wide vs user-specific). Honored only for tenant/global
        /// admins; regular users always create user-specific runbooks. Null keeps the existing scope on update.
        /// </summary>
        public ScopeEnum? Scope { get; set; } = null;
    }
}
