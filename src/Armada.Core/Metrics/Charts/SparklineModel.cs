namespace Armada.Core.Metrics.Charts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A sparkline: a small trend of one series with no axes, and an accessible summary of it.
    /// </summary>
    public class SparklineModel
    {
        #region Public-Members

        /// <summary>
        /// What the trend is (for example "First output trend for ClaudeCode").
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Values, oldest first (null where there is none). Never null.
        /// </summary>
        public List<double?> Values
        {
            get { return _Values; }
            set { _Values = value ?? new List<double?>(); }
        }

        /// <summary>
        /// How values are written.
        /// </summary>
        public ChartValueFormatEnum Format { get; set; } = ChartValueFormatEnum.DurationMs;

        /// <summary>
        /// Color role.
        /// </summary>
        public ChartColorEnum Color { get; set; } = ChartColorEnum.Accent;

        /// <summary>
        /// True when any value is present.
        /// </summary>
        public bool HasData
        {
            get { return _Values.Any(v => v.HasValue); }
        }

        #endregion

        #region Private-Members

        private List<double?> _Values = new List<double?>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SparklineModel()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// One-sentence description: the latest value and the lowest and highest.
        /// </summary>
        /// <returns>Text.</returns>
        public string Summary()
        {
            List<double> present = _Values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            if (present.Count == 0) return Name + ": no data";
            double latest = present[present.Count - 1];
            return Name + ": latest " + ChartFormat.Value(latest, Format) + ", lowest " + ChartFormat.Value(present.Min(), Format)
                + ", highest " + ChartFormat.Value(present.Max(), Format);
        }

        #endregion
    }
}
