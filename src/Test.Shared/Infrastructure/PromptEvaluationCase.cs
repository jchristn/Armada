namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One case of scripts/common/prompt-evaluations/cases.json (the fields the tests read).
    /// </summary>
    public sealed class PromptEvaluationCase
    {
        /// <summary>Case id.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        /// <summary>Case kind (architect, report, blackbox).</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "";

        /// <summary>Prompt template the case evaluates.</summary>
        [JsonPropertyName("persona")]
        public string Persona { get; set; } = "";

        /// <summary>The improvement the case carries itself, or null when it uses the deployed persona additions.</summary>
        [JsonPropertyName("improvedAddition")]
        public string? ImprovedAddition { get; set; } = null;
    }
}
