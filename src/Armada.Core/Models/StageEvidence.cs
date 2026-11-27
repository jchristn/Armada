namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Text.Json.Serialization;
    using Armada.Core.Services;

    /// <summary>
    /// What a stage checked before reporting: the commands it ran and the files it inspected.
    /// </summary>
    public class StageEvidence
    {
        /// <summary>
        /// Most checks a submission may list.
        /// </summary>
        public const int MaxChecksRun = 100;

        /// <summary>
        /// Most inspected files a submission may list.
        /// </summary>
        public const int MaxInspectedFiles = 500;

        /// <summary>
        /// Checks the stage ran, each the command and its result.
        /// </summary>
        [JsonPropertyName("checksRun")]
        [Description("Checks you ran, each the command and its result, for example \"dotnet test: 42 passed\".")]
        [CollectionBound(MaxChecksRun, Reason = "StageSubmissionValidator rejects more")]
        public List<string> ChecksRun { get; set; } = new List<string>();

        /// <summary>
        /// Repository-relative files the stage inspected.
        /// </summary>
        [JsonPropertyName("inspectedFiles")]
        [Description("Repository-relative files you read to reach this result.")]
        [CollectionBound(MaxInspectedFiles, Reason = "StageSubmissionValidator rejects more")]
        public List<string> InspectedFiles { get; set; } = new List<string>();
    }
}
