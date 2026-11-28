namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// What a submit tool call returns to the model: accepted with the stored id, or the errors to fix and resubmit.
    /// </summary>
    public class StageSubmitResult
    {
        /// <summary>
        /// True when the submission was valid and stored.
        /// </summary>
        [JsonPropertyName("accepted")]
        public bool Accepted { get; set; } = false;

        /// <summary>
        /// What was submitted.
        /// </summary>
        [JsonPropertyName("kind")]
        public StageSubmissionKindEnum Kind { get; set; } = StageSubmissionKindEnum.Result;

        /// <summary>
        /// Stored submission id, or null when not accepted.
        /// </summary>
        [JsonPropertyName("submissionId")]
        public string? SubmissionId { get; set; } = null;

        /// <summary>
        /// Errors in the order found; empty when accepted.
        /// </summary>
        [JsonPropertyName("errors")]
        [CollectionBound(StageSubmissionCheck.MaxErrors + 1, Reason = "Copied from StageSubmissionCheck.Errors")]
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// What the model should do next.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }
}
