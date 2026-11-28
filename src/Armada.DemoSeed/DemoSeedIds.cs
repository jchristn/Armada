namespace Armada.DemoSeed
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Ids the store screenshot script needs (scripts/mobile/capture-store-screenshots.sh), written to demo-ids.json.
    /// </summary>
    public class DemoSeedIds
    {
        /// <summary>
        /// A mission with several attempts: an output repair and an escalation to a stronger model.
        /// </summary>
        [JsonPropertyName("MISSION_ID")]
        public string MissionId { get; set; } = "";

        /// <summary>
        /// A voyage running the mixed open and frontier pipeline.
        /// </summary>
        [JsonPropertyName("VOYAGE_ID")]
        public string VoyageId { get; set; } = "";

        /// <summary>
        /// The open model's profile, with a measured scorecard.
        /// </summary>
        [JsonPropertyName("PROFILE_ID")]
        public string ProfileId { get; set; } = "";
    }
}
