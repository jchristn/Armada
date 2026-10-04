namespace Armada.Tui.Screens.Ask
{
    /// <summary>
    /// Kind of a block in the Ask transcript.
    /// </summary>
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
