namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Services;

    /// <summary>
    /// The structured mission plan an Architect captain emits as a fenced <c>armada-plan</c> JSON block. Deserialized
    /// into this typed form; mission dependencies and sequencing come from its fields, not from description wording.
    /// </summary>
    public class ArchitectPlan
    {
        #region Public-Members

        /// <summary>Versions 2 to 4 validate contracts before Worker dispatch; version 3 also requires decision-to-check links and version 4 adds parent, ownedPaths, readsPaths and branchStrategy, rejecting owned-path overlaps between missions that can run at the same time. Null preserves legacy plans.</summary>
        [JsonPropertyName("contractVersion")]
        public int? ContractVersion { get; set; }

        /// <summary>Stable objective requirement IDs only (not ID plus description); acceptance criterion references match these strings exactly.</summary>
        [JsonPropertyName("requirements")]
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> Requirements { get; set; } = new List<string>();

        /// <summary>Resolved design choices, including compatibility changes and mandatory versus default settings.</summary>
        [JsonPropertyName("decisions")]
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> Decisions { get; set; } = new List<string>();

        /// <summary>
        /// The planned missions, in order. Indexes used by <see cref="ArchitectPlanMission.Parent"/> and
        /// <see cref="ArchitectPlanMission.DependsOn"/> are 1-based positions in this list.
        /// </summary>
        [JsonPropertyName("missions")]
        [CollectionBound(CollectionLimits.MaxRequestItems)]
        public List<ArchitectPlanMission> Missions { get; set; } = new List<ArchitectPlanMission>();

        /// <summary>
        /// Non-fatal findings from parsing, such as a <c>dependsOn</c> read as a parent or a v3 ownership overlap.
        /// Set by the parser; never read from the plan.
        /// </summary>
        [JsonIgnore]
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> Warnings { get; set; } = new List<string>();

        #endregion
    }
}
