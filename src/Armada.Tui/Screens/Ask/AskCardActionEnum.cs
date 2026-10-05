namespace Armada.Tui.Screens.Ask
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a clickable button on an Ask confirm card does.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AskCardActionEnum
    {
        /// <summary>
        /// Approve the proposal (the same call as <c>a</c>).
        /// </summary>
        Approve = 0,

        /// <summary>
        /// Reject the proposal (the same call as <c>r</c>).
        /// </summary>
        Reject = 1,

        /// <summary>
        /// Show or hide the exact arguments (the same as <c>x</c>).
        /// </summary>
        Arguments = 2
    }
}
