namespace Armada.Core.Metrics.Charts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// One series of a bucketed time chart: a name, a color role, and one value per bucket (null where the series has no
    /// value, which a line chart leaves as a gap).
    /// </summary>
    public class ChartSeriesData
    {
        #region Public-Members

        /// <summary>
        /// Stable key (for example missionsFinished, or runtime|model).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Display name (for example "Missions finished").
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Color role.
        /// </summary>
        public ChartColorEnum Color { get; set; } = ChartColorEnum.Accent;

        /// <summary>
        /// True to draw the line dashed (line charts).
        /// </summary>
        public bool Dashed { get; set; } = false;

        /// <summary>
        /// One value per bucket, oldest first. Never null.
        /// </summary>
        public List<double?> Values
        {
            get { return _Values; }
            set { _Values = value ?? new List<double?>(); }
        }

        #endregion

        #region Private-Members

        private List<double?> _Values = new List<double?>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ChartSeriesData()
        {
        }

        /// <summary>
        /// Instantiate with values.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="name">Display name.</param>
        /// <param name="color">Color role.</param>
        /// <param name="values">Values, oldest first.</param>
        /// <param name="dashed">True to draw the line dashed.</param>
        public ChartSeriesData(string key, string name, ChartColorEnum color, IEnumerable<double?> values, bool dashed = false)
        {
            Key = key ?? String.Empty;
            Name = name ?? String.Empty;
            Color = color;
            Dashed = dashed;
            Values = values == null ? new List<double?>() : values.ToList();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Sum of the values (nulls count as zero).
        /// </summary>
        /// <returns>Total.</returns>
        public double Total()
        {
            double total = 0;
            foreach (double? value in _Values) if (value.HasValue) total += value.Value;
            return total;
        }

        /// <summary>
        /// Largest value, or null when every value is null.
        /// </summary>
        /// <returns>Largest value or null.</returns>
        public double? Max()
        {
            double? max = null;
            foreach (double? value in _Values)
            {
                if (value.HasValue && (max == null || value.Value > max.Value)) max = value.Value;
            }

            return max;
        }

        /// <summary>
        /// Value at a bucket, or null when out of range or missing.
        /// </summary>
        /// <param name="index">Bucket index.</param>
        /// <returns>Value or null.</returns>
        public double? At(int index)
        {
            if (index < 0 || index >= _Values.Count) return null;
            return _Values[index];
        }

        #endregion
    }
}
