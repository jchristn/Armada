namespace Armada.Server.Mcp
{
    using System.Text.Json.Serialization;
    using Armada.Core.Protocol;

    /// <summary>
    /// The permission prompt tool's answer in the shape Claude Code reads from the tool result text:
    /// <c>{"behavior":"allow","updatedInput":{...}}</c> or <c>{"behavior":"deny","message":"..."}</c>.
    /// </summary>
    public class CliPermissionPromptResult
    {
        /// <summary>
        /// Behavior value for an allowed call.
        /// </summary>
        public const string Allow = "allow";

        /// <summary>
        /// Behavior value for a denied call.
        /// </summary>
        public const string Deny = "deny";

        /// <summary>
        /// allow or deny.
        /// </summary>
        [JsonPropertyName("behavior")]
        public string Behavior { get; set; } = Deny;

        /// <summary>
        /// The tool input to run with (the original input, unchanged) when allowed; omitted when denied.
        /// </summary>
        [JsonPropertyName("updatedInput")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? UpdatedInput { get; set; } = null;

        /// <summary>
        /// Reason shown to the model when denied; omitted when allowed.
        /// </summary>
        [JsonPropertyName("message")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Message { get; set; } = null;
    }
}
