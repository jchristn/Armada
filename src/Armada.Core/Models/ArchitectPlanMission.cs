namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Services;

    /// <summary>
    /// One mission in an <see cref="ArchitectPlan"/>.
    /// </summary>
    public class ArchitectPlanMission
    {
        #region Public-Members

        /// <summary>Implementation, investigation, or integration; version 2 requires a final integration barrier.</summary>
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        /// <summary>Executable acceptance scenarios, preserved in every downstream slice contract.</summary>
        [JsonPropertyName("acceptanceCriteria")]
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<ArchitectAcceptanceCriterion> AcceptanceCriteria { get; set; } = new List<ArchitectAcceptanceCriterion>();

        /// <summary>1-based indexes of relevant plan decisions. Null preserves legacy all-decision context; empty means none.</summary>
        [JsonPropertyName("decisionIndexes")]
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<int>? DecisionIndexes { get; set; }

        /// <summary>Exact commands whose original exit status must be checked.</summary>
        [JsonPropertyName("validationCommands")]
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> ValidationCommands { get; set; } = new List<string>();

        /// <summary>
        /// Mission title.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; } = null;

        /// <summary>
        /// Mission description: what to do, which files to touch, and why.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; } = null;

        /// <summary>
        /// Contracts up to v3: 1-based index of an earlier mission in the plan whose full Worker chain must finish before
        /// this mission starts, or null when the mission can start immediately. Read as <see cref="Parent"/>.
        /// </summary>
        [JsonPropertyName("dependsOn")]
        public int? DependsOn { get; set; } = null;

        /// <summary>
        /// Contract v4: 1-based index of an earlier mission in the plan that this mission builds on. It becomes the
        /// mission's parent: it starts after the parent's last stage finishes and branches from the parent's result.
        /// A v3 or v4 <see cref="DependsOn"/> is read as a parent with a warning.
        /// </summary>
        [JsonPropertyName("parent")]
        public int? Parent { get; set; } = null;

        /// <summary>
        /// Contract v4: files or directories this mission may change. A trailing <c>/</c> is a directory prefix and
        /// <c>*</c> matches within one path segment (<c>**</c> across segments). Missions that can run at the same time
        /// must not own the same path.
        /// </summary>
        [JsonPropertyName("ownedPaths")]
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> OwnedPaths { get; set; } = new List<string>();

        /// <summary>Contract v4: context the mission needs but must not change.</summary>
        [JsonPropertyName("readsPaths")]
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> ReadsPaths { get; set; } = new List<string>();

        /// <summary>
        /// Contract v4: <c>continue</c> or <c>fork</c> to override the default branch rule (the first child continues
        /// its parent's branch, later children fork from its head). Null keeps the default.
        /// </summary>
        [JsonPropertyName("branchStrategy")]
        public string? BranchStrategy { get; set; } = null;

        /// <summary>
        /// When true, this mission waits until other implementation pipelines in the voyage, including reviews
        /// and checks, settle. Its own downstream stages and the Architect are excluded; reworks inherit the
        /// barrier. For example, final documentation waits for implementation review to finish.
        /// </summary>
        [JsonPropertyName("waitForOtherMissions")]
        public bool WaitForOtherMissions { get; set; } = false;

        /// <summary>
        /// Worker eligibility envelope tightened for this mission, or null to use the vessel default. It may only tighten
        /// the vessel default; a looser field is clamped to the default and the clamp is recorded.
        /// </summary>
        [JsonPropertyName("workerEnvelope")]
        public WorkerEnvelope? WorkerEnvelope { get; set; } = null;

        #endregion
    }
}
