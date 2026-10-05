namespace Armada.Tui.Screens.Ask
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Kind of a block in the Ask transcript.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AskBlockKindEnum
    {
        /// <summary>
        /// "Load earlier messages" at the top.
        /// </summary>
        Older = 0,

        /// <summary>
        /// The empty state (greeting and quick actions).
        /// </summary>
        Empty = 1,

        /// <summary>
        /// A persisted (or optimistic) message.
        /// </summary>
        Message = 2,

        /// <summary>
        /// The live streaming reply.
        /// </summary>
        Stream = 3,

        /// <summary>
        /// The rotating waiting phrase.
        /// </summary>
        Waiting = 4,

        /// <summary>
        /// The last turn's failure.
        /// </summary>
        TurnError = 5
    }
}
