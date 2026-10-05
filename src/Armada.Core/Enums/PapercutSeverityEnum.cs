namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// How much a papercut cost the captain that reported it.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PapercutSeverityEnum
    {
        /// <summary>
        /// Noticed and worked around at little cost.
        /// </summary>
        Low,

        /// <summary>
        /// Cost real time, or forced a workaround that a later mission will repeat.
        /// </summary>
        Medium,

        /// <summary>
        /// Blocked or degraded the mission.
        /// </summary>
        High
    }
}
