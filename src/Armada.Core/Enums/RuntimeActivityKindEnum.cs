namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a captain is doing at the moment, as read from its runtime's structured output.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RuntimeActivityKindEnum
    {
        /// <summary>
        /// The captain started a tool call (a shell command, a file edit, an MCP tool).
        /// </summary>
        [EnumMember(Value = "ToolCall")]
        ToolCall,

        /// <summary>
        /// The captain wrote text (a message to the user or a progress note).
        /// </summary>
        [EnumMember(Value = "Text")]
        Text,

        /// <summary>
        /// The captain is reasoning.
        /// </summary>
        [EnumMember(Value = "Thinking")]
        Thinking
    }
}
