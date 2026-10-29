namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where a combined black box timeline item comes from.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxTimelineSourceEnum
    {
        /// <summary>
        /// The black box entry itself (captured, enriched, analyzed, resolved).
        /// </summary>
        Entry,

        /// <summary>
        /// A note on the entry.
        /// </summary>
        Note,

        /// <summary>
        /// The mission row or a mission event.
        /// </summary>
        Mission,

        /// <summary>
        /// A voyage event.
        /// </summary>
        Voyage,

        /// <summary>
        /// The dock row or a dock event.
        /// </summary>
        Dock,

        /// <summary>
        /// The merge queue entry row or a merge entry event.
        /// </summary>
        MergeEntry,

        /// <summary>
        /// A captain event in the mission's window.
        /// </summary>
        Captain,

        /// <summary>
        /// A Harbor link event (connected, disconnected) in the mission's window.
        /// </summary>
        Harbor,

        /// <summary>
        /// Unrecognized value.
        /// </summary>
        Unknown
    }
}
