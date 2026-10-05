namespace Armada.Client.Socket
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Phase of a captain tool call in <c>ask.tool</c> and <c>planning-session.tool</c> events (wire values are
    /// lowercase: started, completed; names are read case-insensitively).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ToolCallPhaseEnum
    {
        /// <summary>
        /// The call started.
        /// </summary>
        Started,

        /// <summary>
        /// The call completed (successfully or not; see the event's ok flag).
        /// </summary>
        Completed
    }
}
