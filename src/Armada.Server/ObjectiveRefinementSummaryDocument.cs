namespace Armada.Server
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The JSON object a captain returns when asked to summarize an objective refinement session.
    /// </summary>
    public class ObjectiveRefinementSummaryDocument
    {
        #region Public-Members

        /// <summary>
        /// Refined objective summary, or null.
        /// </summary>
        [JsonPropertyName("summary")]
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Acceptance criteria, or null.
        /// </summary>
        [JsonPropertyName("acceptanceCriteria")]
        public List<string?>? AcceptanceCriteria { get; set; } = null;

        /// <summary>
        /// Non-goals, or null.
        /// </summary>
        [JsonPropertyName("nonGoals")]
        public List<string?>? NonGoals { get; set; } = null;

        /// <summary>
        /// Rollout constraints, or null.
        /// </summary>
        [JsonPropertyName("rolloutConstraints")]
        public List<string?>? RolloutConstraints { get; set; } = null;

        /// <summary>
        /// Suggested pipeline identifier, or null.
        /// </summary>
        [JsonPropertyName("suggestedPipelineId")]
        public string? SuggestedPipelineId { get; set; } = null;

        #endregion
    }
}
