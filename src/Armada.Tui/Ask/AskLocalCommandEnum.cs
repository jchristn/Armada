namespace Armada.Tui.Ask
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The built-in Ask commands the client handles itself (the dashboard's <c>AskLocalCommandName</c> in
    /// <c>lib/askCommands.ts</c>).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AskLocalCommandEnum
    {
        /// <summary>
        /// <c>/new</c> (alias <c>/clear</c>): start a new conversation with a fresh context.
        /// </summary>
        New = 0,

        /// <summary>
        /// <c>/help</c>: list the commands.
        /// </summary>
        Help = 1,

        /// <summary>
        /// <c>/summarize</c>: summarize the open conversation.
        /// </summary>
        Summarize = 2,

        /// <summary>
        /// <c>/rename &lt;title&gt;</c>: rename the open conversation.
        /// </summary>
        Rename = 3,

        /// <summary>
        /// <c>/archive</c>: archive the open conversation.
        /// </summary>
        Archive = 4,

        /// <summary>
        /// <c>/captain &lt;name&gt;</c>: switch the captain by name.
        /// </summary>
        Captain = 5,

        /// <summary>
        /// <c>/thinking on|off</c>: turn Show thinking on or off.
        /// </summary>
        Thinking = 6
    }
}
