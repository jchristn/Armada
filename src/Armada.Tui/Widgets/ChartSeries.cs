namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One named data series for <see cref="ChartView"/>.
    /// </summary>
    public class ChartSeries
    {
        #region Public-Members

        /// <summary>
        /// English series name (legend).
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Values in order. Never null.
        /// </summary>
        public List<double> Values { get; set; } = new List<double>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="values">Values.</param>
        public ChartSeries(string name, IEnumerable<double>? values = null)
        {
            Name = name ?? "";
            if (values != null) Values = new List<double>(values);
        }

        #endregion
    }
}
