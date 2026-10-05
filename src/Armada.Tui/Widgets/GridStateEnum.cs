namespace Armada.Tui.Widgets
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Load state of an <see cref="ArmadaGrid{T}"/>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GridStateEnum
    {
        /// <summary>
        /// Rows are shown.
        /// </summary>
        Ready = 0,

        /// <summary>
        /// A page is loading.
        /// </summary>
        Loading = 1,

        /// <summary>
        /// The last load failed.
        /// </summary>
        Error = 2
    }
}
