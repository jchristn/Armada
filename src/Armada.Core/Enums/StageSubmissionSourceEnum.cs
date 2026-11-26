namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a stage submission reached the Admiral.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum StageSubmissionSourceEnum
    {
        /// <summary>
        /// The captain called the MCP submit tool with its mission-scoped token.
        /// </summary>
        Mcp,

        /// <summary>
        /// An API endpoint captain called the runtime's native submit tool, which validated the submission and wrote it
        /// into the stage output; the Admiral stored it at completion.
        /// </summary>
        Native
    }
}
