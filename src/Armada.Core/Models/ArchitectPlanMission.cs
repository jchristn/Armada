namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One mission in an <see cref="ArchitectPlan"/>.
    /// </summary>
    public class ArchitectPlanMission
    {
        #region Public-Members

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
        /// 1-based index of an earlier mission in the plan whose full Worker chain must finish before this mission
        /// starts, or null when the mission can start immediately.
        /// </summary>
        [JsonPropertyName("dependsOn")]
        public int? DependsOn { get; set; } = null;

        /// <summary>
        /// When true, this mission waits until every other Worker mission in the voyage has settled (for example a
        /// documentation mission that describes the final behavior of the implementation missions).
        /// </summary>
        [JsonPropertyName("waitForOtherMissions")]
        public bool WaitForOtherMissions { get; set; } = false;

        #endregion
    }
}
