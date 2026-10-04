namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a dependency tool invocation (dotnet or npm) ended, before its output is interpreted.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DependencyToolOutcomeEnum
    {
        /// <summary>
        /// The process ran to completion (any exit code).
        /// </summary>
        Completed,

        /// <summary>
        /// The executable could not be started (not installed or not on PATH).
        /// </summary>
        ToolMissing,

        /// <summary>
        /// The process exceeded the configured timeout and was killed.
        /// </summary>
        TimedOut
    }
}
