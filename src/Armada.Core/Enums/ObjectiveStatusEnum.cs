namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Objective lifecycle state.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ObjectiveStatusEnum
    {
        /// <summary>
        /// Newly captured objective that has not been fully scoped yet.
        /// </summary>
        Draft = 0,
        /// <summary>
        /// Scope, acceptance criteria, and constraints are defined.
        /// </summary>
        Scoped = 1,
        /// <summary>
        /// Planning exists but implementation is not yet underway.
        /// </summary>
        Planned = 2,
        /// <summary>
        /// Implementation or coordination work is in progress.
        /// </summary>
        InProgress = 3,
        /// <summary>
        /// The objective has at least one associated shipped release.
        /// </summary>
        Released = 4,
        /// <summary>
        /// The objective has reached deployment execution.
        /// </summary>
        Deployed = 5,
        /// <summary>
        /// The objective is fully complete.
        /// </summary>
        Completed = 6,
        /// <summary>
        /// Progress is blocked and requires attention.
        /// </summary>
        Blocked = 7,
        /// <summary>
        /// The objective was intentionally abandoned.
        /// </summary>
        Cancelled = 8
    }
}
