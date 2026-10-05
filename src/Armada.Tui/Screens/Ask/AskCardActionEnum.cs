namespace Armada.Tui.Screens.Ask
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a clickable button on an Ask confirm card or CLI permission card does.
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
        Arguments = 2,

        /// <summary>
        /// Allow a CLI permission request once (the same as <c>a</c> on its card).
        /// </summary>
        AllowOnce = 3,

        /// <summary>
        /// Allow a CLI permission request and remember it as a rule (the same as <c>A</c> on its card).
        /// </summary>
        AllowAndRemember = 4,

        /// <summary>
        /// Deny a CLI permission request (the same as <c>d</c> on its card).
        /// </summary>
        Deny = 5
    }
}
