namespace Armada.Tui.Widgets
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Chart rendering style.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChartKindEnum
    {
        /// <summary>
        /// Braille line chart.
        /// </summary>
        Line = 0,

        /// <summary>
        /// Horizontal bar chart (one bar per label).
        /// </summary>
        Bar = 1,

        /// <summary>
        /// One-row sparkline.
        /// </summary>
        Sparkline = 2
    }
}
