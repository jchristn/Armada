namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a stage checked before reporting: the commands it ran and the files it inspected.
    /// </summary>
    public class StageEvidence
    {
        /// <summary>
        /// Checks the stage ran, each the command and its result.
        /// </summary>
        [JsonPropertyName("checksRun")]
        [Description("Checks you ran, each the command and its result, for example \"dotnet test: 42 passed\".")]
        public List<string> ChecksRun { get; set; } = new List<string>();

        /// <summary>
        /// Repository-relative files the stage inspected.
        /// </summary>
        [JsonPropertyName("inspectedFiles")]
        [Description("Repository-relative files you read to reach this result.")]
        public List<string> InspectedFiles { get; set; } = new List<string>();
    }
}
