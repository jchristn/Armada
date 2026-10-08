namespace Armada.Core.Metrics.Charts
{
    using System;

    /// <summary>
    /// A horizontal reference line across a line chart (for example a Harbor's slot capacity), drawn dashed.
    /// </summary>
    public class ChartReferenceLine
    {
        #region Public-Members

        /// <summary>
        /// Value the line marks.
        /// </summary>
        public double Value { get; set; } = 0;

        /// <summary>
        /// Display label (for example "Max slots (4)").
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Color role.
        /// </summary>
        public ChartColorEnum Color { get; set; } = ChartColorEnum.Danger;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ChartReferenceLine()
        {
        }

        /// <summary>
        /// Instantiate with values.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="label">Label.</param>
        /// <param name="color">Color role.</param>
        public ChartReferenceLine(double value, string label, ChartColorEnum color)
        {
            Value = value;
            Label = label ?? String.Empty;
            Color = color;
        }

        #endregion
    }
}
