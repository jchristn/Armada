namespace Armada.Tui.Screens.Build
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Kind of a row in the vessel history commit list.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VesselHistoryRowKindEnum
    {
        /// <summary>
        /// A commit.
        /// </summary>
        Commit = 0,

        /// <summary>
        /// The next (older) page is loading.
        /// </summary>
        Loading = 1,

        /// <summary>
        /// The oldest commit is shown; there is nothing more to load.
        /// </summary>
        End = 2,

        /// <summary>
        /// Loading the next page failed.
        /// </summary>
        Error = 3
    }
}
