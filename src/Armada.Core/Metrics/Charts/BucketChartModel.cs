namespace Armada.Core.Metrics.Charts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// What a bucketed time chart shows, independent of how it is drawn: the buckets, the series (stacked bars or lines),
    /// an optional reference line, and how values are written. Gives the axis scale, the text of a bucket's tooltip, and an
    /// accessible summary, so every surface that draws it says the same thing.
    /// </summary>
    public class BucketChartModel
    {
        #region Public-Members

        /// <summary>
        /// Title (for example "Jobs over time").
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Plain-language range (for example "last 24 hours").
        /// </summary>
        public string RangeName { get; set; } = String.Empty;

        /// <summary>
        /// Stacked bars or lines.
        /// </summary>
        public BucketChartKindEnum Kind { get; set; } = BucketChartKindEnum.StackedBar;

        /// <summary>
        /// How values are written.
        /// </summary>
        public ChartValueFormatEnum Format { get; set; } = ChartValueFormatEnum.Count;

        /// <summary>
        /// Bucket width in minutes.
        /// </summary>
        public int BucketMinutes { get; set; } = 30;

        /// <summary>
        /// Bucket starts (UTC), oldest first. Never null.
        /// </summary>
        public List<DateTime> BucketStartsUtc
        {
            get { return _BucketStartsUtc; }
            set { _BucketStartsUtc = value ?? new List<DateTime>(); }
        }

        /// <summary>
        /// Series, bottom of the stack (or first in the legend) first. Never null.
        /// </summary>
        public List<ChartSeriesData> Series
        {
            get { return _Series; }
            set { _Series = value ?? new List<ChartSeriesData>(); }
        }

        /// <summary>
        /// Reference line (line charts), or null.
        /// </summary>
        public ChartReferenceLine? Reference { get; set; } = null;

        /// <summary>
        /// Number of buckets.
        /// </summary>
        public int BucketCount
        {
            get { return _BucketStartsUtc.Count; }
        }

        /// <summary>
        /// True when any series has a value above zero.
        /// </summary>
        public bool HasData
        {
            get { return _Series.Any(s => s.Values.Any(v => v.HasValue && v.Value > 0)); }
        }

        #endregion

        #region Private-Members

        private List<DateTime> _BucketStartsUtc = new List<DateTime>();
        private List<ChartSeriesData> _Series = new List<ChartSeriesData>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BucketChartModel()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The largest value the plot must show: the tallest stack (stacked bars) or the largest point (lines), and the
        /// reference line.
        /// </summary>
        /// <returns>Largest value; zero when there is no data.</returns>
        public double DataMax()
        {
            double max = 0;
            if (Kind == BucketChartKindEnum.StackedBar)
            {
                for (int i = 0; i < BucketCount; i++) max = Math.Max(max, StackTotal(i));
            }
            else
            {
                foreach (ChartSeriesData series in _Series)
                {
                    double? seriesMax = series.Max();
                    if (seriesMax.HasValue) max = Math.Max(max, seriesMax.Value);
                }
            }

            if (Reference != null) max = Math.Max(max, Reference.Value);
            return max;
        }

        /// <summary>
        /// The top of the value axis: <see cref="DataMax"/> rounded up to 1, 2, or 5 times a power of ten (at least 1).
        /// </summary>
        /// <returns>Axis maximum.</returns>
        public double AxisMax()
        {
            return ChartGeometry.NiceCeiling(DataMax());
        }

        /// <summary>
        /// Sum of every series at a bucket (nulls count as zero).
        /// </summary>
        /// <param name="index">Bucket index.</param>
        /// <returns>Total.</returns>
        public double StackTotal(int index)
        {
            double total = 0;
            foreach (ChartSeriesData series in _Series)
            {
                double? value = series.At(index);
                if (value.HasValue && value.Value > 0) total += value.Value;
            }

            return total;
        }

        /// <summary>
        /// Short axis label of a bucket in a time zone.
        /// </summary>
        /// <param name="index">Bucket index.</param>
        /// <param name="zone">Viewer's time zone.</param>
        /// <returns>Label; empty when out of range.</returns>
        public string BucketLabel(int index, TimeZoneInfo zone)
        {
            if (index < 0 || index >= BucketCount) return String.Empty;
            return ChartFormat.BucketLabel(_BucketStartsUtc[index], BucketMinutes, zone);
        }

        /// <summary>
        /// Tooltip text of a bucket: the time it covers, then each series' value (and the total for stacked bars, and the
        /// reference line).
        /// </summary>
        /// <param name="index">Bucket index.</param>
        /// <param name="zone">Viewer's time zone.</param>
        /// <returns>Text; empty when out of range.</returns>
        public string Tooltip(int index, TimeZoneInfo zone)
        {
            if (index < 0 || index >= BucketCount) return String.Empty;
            StringBuilder sb = new StringBuilder();
            sb.Append(ChartFormat.BucketSpan(_BucketStartsUtc[index], BucketMinutes, zone));
            foreach (ChartSeriesData series in _Series)
            {
                sb.Append('\n').Append(series.Name).Append(": ").Append(ChartFormat.Value(series.At(index), Format));
            }

            if (Kind == BucketChartKindEnum.StackedBar && _Series.Count > 1)
            {
                sb.Append('\n').Append("Total: ").Append(ChartFormat.Value(StackTotal(index), Format));
            }

            if (Reference != null) sb.Append('\n').Append(Reference.Label);
            return sb.ToString();
        }

        /// <summary>
        /// One-sentence description for assistive technology: the title and range, then each series' total (stacked
        /// bars) or highest value (lines), and the reference line.
        /// </summary>
        /// <returns>Text.</returns>
        public string Summary()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Title);
            if (!String.IsNullOrEmpty(RangeName)) sb.Append(", ").Append(RangeName);
            sb.Append(": ");
            if (!HasData)
            {
                sb.Append("no data");
            }
            else
            {
                List<string> parts = new List<string>();
                foreach (ChartSeriesData series in _Series)
                {
                    if (Kind == BucketChartKindEnum.StackedBar) parts.Add(series.Name + " " + ChartFormat.Value(series.Total(), Format));
                    else parts.Add(series.Name + " highest " + ChartFormat.Value(series.Max(), Format));
                }

                sb.Append(String.Join(", ", parts));
            }

            if (Reference != null) sb.Append("; ").Append(Reference.Label);
            return sb.ToString();
        }

        #endregion
    }
}
