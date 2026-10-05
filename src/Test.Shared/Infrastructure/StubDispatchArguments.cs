namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Models;

    /// <summary>
    /// Arguments of the <c>dispatch</c> MCP tool as a stub captain sends them.
    /// </summary>
    public sealed class StubDispatchArguments
    {
        #region Public-Members

        /// <summary>
        /// Voyage title.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        /// <summary>
        /// Target vessel.
        /// </summary>
        [JsonPropertyName("vesselId")]
        public string VesselId { get; set; } = "";

        /// <summary>
        /// Missions.
        /// </summary>
        [JsonPropertyName("missions")]
        public List<MissionDescription> Missions { get; set; } = new List<MissionDescription>();

        #endregion
    }
}
