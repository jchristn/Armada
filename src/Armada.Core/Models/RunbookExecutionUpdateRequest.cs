namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Update a runbook execution snapshot.
    /// </summary>
    public class RunbookExecutionUpdateRequest
    {
        /// <summary>
        /// Optional status override.
        /// </summary>
        public RunbookExecutionStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Optional completed-step identifiers.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string>? CompletedStepIds { get; set; } = null;

        /// <summary>
        /// Optional step notes.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public Dictionary<string, string>? StepNotes { get; set; } = null;

        /// <summary>
        /// Optional execution notes.
        /// </summary>
        public string? Notes { get; set; } = null;
    }
}
