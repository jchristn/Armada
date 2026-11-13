namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Fully resolved workflow-profile preview for a target vessel.
    /// </summary>
    public class WorkflowProfileResolutionPreviewResult
    {
        /// <summary>
        /// Profile that Armada resolved for the target vessel.
        /// </summary>
        public WorkflowProfile? ResolvedProfile { get; set; } = null;

        /// <summary>
        /// How the resolved profile was selected.
        /// </summary>
        public WorkflowProfileResolutionModeEnum ResolutionMode { get; set; } = WorkflowProfileResolutionModeEnum.Global;

        /// <summary>
        /// Available check types exposed by the resolved profile.
        /// </summary>
        [CollectionBound(WorkflowProfileService.MaxAvailableCheckTypes, Reason = "Distinct check types the profile commands can resolve")]
        public List<string> AvailableCheckTypes { get; set; } = new List<string>();

        /// <summary>
        /// Fully resolved command previews across base and environment-specific commands.
        /// </summary>
        [CollectionBound(WorkflowProfileService.MaxCommandPreviews, Reason = "15 profile commands plus 6 per environment; Environments is limited to MaxConfigItems")]
        public List<WorkflowProfileCommandPreview> CommandPreviews { get; set; } = new List<WorkflowProfileCommandPreview>();
    }
}
