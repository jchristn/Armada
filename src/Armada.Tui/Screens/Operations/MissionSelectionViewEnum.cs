namespace Armada.Tui.Screens.Operations
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Which table a <see cref="MissionSelectionPanel"/> shows below the selection summary.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MissionSelectionViewEnum
    {
        /// <summary>
        /// Candidate comparison of the current, latest or opened decision.
        /// </summary>
        Comparison = 0,

        /// <summary>
        /// Paged decision history.
        /// </summary>
        History = 1,

        /// <summary>
        /// Read-only preview of current selection; writes nothing and claims nothing.
        /// </summary>
        Preview = 2
    }
}
