namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Kind of fleet action.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FleetActionKindEnum
    {
        /// <summary>
        /// Runs a shell command in each target vessel's working directory and captures exit code and output.
        /// </summary>
        Command,

        /// <summary>
        /// Dispatches one voyage per target vessel through the normal Admiral dispatch path.
        /// </summary>
        Mission
    }
}
