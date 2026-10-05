namespace Armada.Client.Socket
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// State of an Ask turn in an <c>ask.turn</c> event (wire values are lowercase: started, completed, cancelled,
    /// failed; names are read case-insensitively).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AskTurnStateEnum
    {
        /// <summary>
        /// The turn started.
        /// </summary>
        Started,

        /// <summary>
        /// The turn finished with a reply.
        /// </summary>
        Completed,

        /// <summary>
        /// The turn was cancelled.
        /// </summary>
        Cancelled,

        /// <summary>
        /// The turn failed.
        /// </summary>
        Failed
    }
}
