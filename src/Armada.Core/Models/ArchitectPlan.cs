namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The structured mission plan an Architect captain emits as a fenced <c>armada-plan</c> JSON block. Deserialized
    /// into this typed form; mission dependencies and sequencing come from its fields, not from description wording.
    /// </summary>
    public class ArchitectPlan
    {
        #region Public-Members

        /// <summary>
        /// The planned missions, in order. Indexes used by <see cref="ArchitectPlanMission.DependsOn"/> are 1-based
        /// positions in this list.
        /// </summary>
        [JsonPropertyName("missions")]
        public List<ArchitectPlanMission> Missions { get; set; } = new List<ArchitectPlanMission>();

        #endregion
    }
}
