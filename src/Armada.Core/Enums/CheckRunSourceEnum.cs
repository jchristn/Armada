namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Origin of a structured check run.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CheckRunSourceEnum
    {
        /// <summary>
        /// Executed directly by Armada on the host.
        /// </summary>
        Armada = 0,

        /// <summary>
        /// Imported from an external provider or CI system.
        /// </summary>
        External = 1
    }
}
