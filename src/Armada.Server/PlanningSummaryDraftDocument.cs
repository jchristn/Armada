namespace Armada.Server
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The JSON object a captain returns when asked to summarize a planning session into a dispatch draft.
    /// </summary>
    public class PlanningSummaryDraftDocument
    {
        #region Public-Members

        /// <summary>
        /// Draft title, or null.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; } = null;

        /// <summary>
        /// Draft description, or null.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; } = null;

        /// <summary>
        /// Optional missions of a multi-mission draft: key, parentKey, title, description and ownedPaths each.
        /// </summary>
        [JsonPropertyName("missions")]
        [Armada.Core.Services.CollectionBound(Armada.Core.Services.CollectionLimits.MaxRequestItems)]
        public List<Armada.Core.Models.PlanningDraftMission>? Missions { get; set; } = null;

        #endregion
    }
}
