namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Validation and preview output for a workflow profile.
    /// </summary>
    public class WorkflowProfileValidationResult
    {
        /// <summary>
        /// Whether the profile is valid.
        /// </summary>
        public bool IsValid { get; set; } = true;

        /// <summary>
        /// Validation errors.
        /// </summary>
        [CollectionBound(WorkflowProfileService.MaxValidationMessages, Reason = "At most one message per validation rule")]
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// Non-fatal warnings.
        /// </summary>
        [CollectionBound(WorkflowProfileService.MaxValidationMessages, Reason = "At most one message per validation rule")]
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// Check types that can currently be resolved from the profile.
        /// </summary>
        [CollectionBound(WorkflowProfileService.MaxAvailableCheckTypes, Reason = "Distinct check types the profile commands can resolve")]
        public List<string> AvailableCheckTypes { get; set; } = new List<string>();

        /// <summary>
        /// Fully resolved command previews for the profile itself.
        /// </summary>
        [CollectionBound(WorkflowProfileService.MaxCommandPreviews, Reason = "15 profile commands plus 6 per environment; Environments is limited to MaxConfigItems")]
        public List<WorkflowProfileCommandPreview> CommandPreviews { get; set; } = new List<WorkflowProfileCommandPreview>();
    }
}
