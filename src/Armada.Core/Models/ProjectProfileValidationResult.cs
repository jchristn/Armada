namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Validation outcome for a project profile.
    /// </summary>
    public class ProjectProfileValidationResult
    {
        /// <summary>
        /// Whether the profile is valid (no errors).
        /// </summary>
        public bool IsValid { get; set; } = false;

        /// <summary>
        /// Blocking validation errors.
        /// </summary>
        [CollectionBound(ProjectProfileService.MaxValidationMessages, Reason = "At most one message per validation rule")]
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// Non-blocking warnings.
        /// </summary>
        [CollectionBound(ProjectProfileService.MaxValidationMessages, Reason = "At most one message per validation rule")]
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
