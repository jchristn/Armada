namespace Armada.Tui.Screens.Configuration
{
    using System.Collections.Generic;
    using Armada.Tui.Screens.Kit;

    /// <summary>
    /// One row of the <see cref="HarborMetricsPanel"/>: styled text, the first row of a chart (which takes
    /// <see cref="ChartRows"/> rows), or a row a chart above covers (neither).
    /// </summary>
    public class HarborMetricsPanelLine
    {
        #region Public-Members

        /// <summary>
        /// Text runs, or null.
        /// </summary>
        public List<HarborMetricsPanelSpan>? Spans { get; set; } = null;

        /// <summary>
        /// Chart drawn from this row, or null.
        /// </summary>
        public MultiSeriesChart? Chart { get; set; } = null;

        /// <summary>
        /// Rows the chart takes.
        /// </summary>
        public int ChartRows { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborMetricsPanelLine()
        {
        }

        #endregion
    }
}
