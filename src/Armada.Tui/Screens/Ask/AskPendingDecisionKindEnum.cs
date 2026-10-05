namespace Armada.Tui.Screens.Ask
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a pending decision reachable from the Ask transcript is.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AskPendingDecisionKindEnum
    {
        /// <summary>
        /// An action the captain (or a quick action) proposed: a approves, r rejects.
        /// </summary>
        Proposal = 0,

        /// <summary>
        /// A CLI tool permission request: a allows once, A allows and remembers, d (or r) denies.
        /// </summary>
        CliPermission = 1,

        /// <summary>
        /// A mission waiting in review (a work card row): a approves, r denies, through the Resolve Review dialog.
        /// </summary>
        MissionReview = 2
    }
}
