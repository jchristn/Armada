namespace Armada.Tui.Widgets
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a focus region marks itself (see <see cref="FocusFrame"/>). Chosen from the region's size only, never from
    /// its focus state, so moving focus never changes the layout.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FocusFrameKindEnum
    {
        /// <summary>
        /// Too small for any marker: the content gets the whole rectangle.
        /// </summary>
        None = 0,

        /// <summary>
        /// A one-cell box around the content (three or more columns and rows).
        /// </summary>
        Box = 1,

        /// <summary>
        /// Fallback for rectangles under three rows or columns: a one-column left gutter that shows a bar while the
        /// region has focus.
        /// </summary>
        Gutter = 2
    }
}
