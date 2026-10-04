namespace Armada.Tui.Widgets
{
    /// <summary>
    /// Chart rendering style.
    /// </summary>
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
