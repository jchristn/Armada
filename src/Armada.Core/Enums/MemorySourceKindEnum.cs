namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where a memory was extracted from. Recorded as provenance so a memory can be traced back to the work
    /// that produced it and later reconciled or superseded.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MemorySourceKindEnum
    {
        /// <summary>
        /// Distilled from a voyage (a batch of related missions).
        /// </summary>
        Voyage,

        /// <summary>
        /// Distilled from a single mission's execution.
        /// </summary>
        Mission,

        /// <summary>
        /// Learned about a specific vessel (repository).
        /// </summary>
        Vessel,

        /// <summary>
        /// Extracted from an interactive conversation (planning or chat).
        /// </summary>
        Conversation,

        /// <summary>
        /// Entered directly by an operator rather than distilled from work.
        /// </summary>
        Manual,

        /// <summary>
        /// Any other or unspecified origin.
        /// </summary>
        Other
    }
}
