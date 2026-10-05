namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// One readiness warning or error for a vessel.
    /// </summary>
    public class VesselReadinessIssue
    {
        /// <summary>
        /// Stable issue code.
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// Severity of the issue.
        /// </summary>
        public ReadinessSeverityEnum Severity { get; set; } = ReadinessSeverityEnum.Warning;

        /// <summary>
        /// Human-readable title.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Detailed explanation.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Optional related value such as a command, path, or input key.
        /// </summary>
        public string? RelatedValue { get; set; } = null;

        /// <summary>
        /// Input provider when the issue concerns a workflow input reference (for example a missing environment
        /// variable or file), or null. Use this instead of parsing the provider prefix of <see cref="RelatedValue"/>.
        /// </summary>
        public WorkflowInputReferenceProviderEnum? InputProvider { get; set; } = null;
    }
}
