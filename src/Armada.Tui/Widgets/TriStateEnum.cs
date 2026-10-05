namespace Armada.Tui.Widgets
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Value of a <see cref="TriStateField"/> (filters such as Active, Dirty, Unread only).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TriStateEnum
    {
        /// <summary>
        /// No filter.
        /// </summary>
        Any = 0,

        /// <summary>
        /// True.
        /// </summary>
        Yes = 1,

        /// <summary>
        /// False.
        /// </summary>
        No = 2
    }
}
