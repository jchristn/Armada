namespace Armada.Tui.Ask
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// What Enter does with the composer text.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AskCommandParseKindEnum
    {
        /// <summary>
        /// Plain text for the captain.
        /// </summary>
        Text = 0,

        /// <summary>
        /// A known command (with its arguments).
        /// </summary>
        Command = 1,

        /// <summary>
        /// Text starting with <c>/</c> that is not a command.
        /// </summary>
        Unknown = 2
    }
}
