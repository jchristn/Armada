namespace Armada.Runtimes
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// What the API turn loop does with a response that requested no tool calls.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ApiTerminalActionEnum
    {
        /// <summary>
        /// The response finishes the run.
        /// </summary>
        Complete,

        /// <summary>
        /// Ask the captain to continue with a follow-up user turn.
        /// </summary>
        Continue,

        /// <summary>
        /// Offer only the submit tool on the next turn and require a tool call.
        /// </summary>
        ForceSubmit,

        /// <summary>
        /// End the run as failed with the decision's error.
        /// </summary>
        Fail
    }
}
