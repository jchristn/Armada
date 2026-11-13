namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Services;

    /// <summary>A falsifiable acceptance criterion and its objective traceability.</summary>
    public class ArchitectAcceptanceCriterion
    {
        /// <summary>Stable identifier within the plan.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>Objective requirement IDs this scenario verifies.</summary>
        [JsonPropertyName("requirementIds")]
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> RequirementIds { get; set; } = new List<string>();

        /// <summary>One-based plan decisions verified by this check. Required in version 3; empty means a general check.</summary>
        [JsonPropertyName("decisionIndexes")]
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<int>? DecisionIndexes { get; set; }

        /// <summary>Concrete inputs and action.</summary>
        [JsonPropertyName("scenario")]
        public string? Scenario { get; set; }

        /// <summary>Observable expected behavior, not a test count.</summary>
        [JsonPropertyName("expected")]
        public string? Expected { get; set; }

        /// <summary>Named test or verification method, including the observation point.</summary>
        [JsonPropertyName("verification")]
        public string? Verification { get; set; }

        /// <summary>Applicable failure and boundary cases with expected outcomes.</summary>
        [JsonPropertyName("negativeCases")]
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> NegativeCases { get; set; } = new List<string>();

        /// <summary>Specific reason no negative cases apply, rather than invented cases.</summary>
        [JsonPropertyName("negativeCasesNotApplicableReason")]
        public string? NegativeCasesNotApplicableReason { get; set; }
    }
}
