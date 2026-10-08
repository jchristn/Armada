namespace Armada.Core.Metrics.Charts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Pure layout math for the bucketed charts, in plot coordinates (origin at the top left, y growing down): the value
    /// axis scale, stacked bar rectangles, line points with gaps, sparkline points, bucket hit testing, and level
    /// quantizing for text sparklines. No UI types, so it is tested on its own and shared by every surface.
    /// </summary>
    public static class ChartGeometry
    {
        #region Public-Methods

        /// <summary>
        /// Round a positive value up to 1, 2, or 5 times a power of ten; at least 1.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Rounded value.</returns>
        public static double NiceCeiling(double value)
        {
            if (Double.IsNaN(value) || value <= 1) return 1;
            double exponent = Math.Floor(Math.Log10(value));
            double magnitude = Math.Pow(10, exponent);
            double fraction = value / magnitude;
            double nice;
            if (fraction <= 1.000001) nice = 1;
            else if (fraction <= 2) nice = 2;
            else if (fraction <= 5) nice = 5;
            else nice = 10;
            return nice * magnitude;
        }

        /// <summary>
        /// Whole-number ticks from zero to a maximum: the smallest step that keeps the gaps within
        /// <paramref name="maxIntervals"/> (a step up to twice as large that divides the maximum evenly is preferred), every
        /// multiple of the step below the maximum, then the maximum itself. A multiple closer to the maximum than half a
        /// step is dropped so labels do not crowd. The maximum is rounded up to a whole number, at least 1.
        /// </summary>
        /// <param name="max">Axis maximum.</param>
        /// <param name="maxIntervals">Most gaps between ticks (values below 1 count as 1).</param>
        /// <returns>Ticks, ascending, from zero to the maximum.</returns>
        public static List<double> WholeTicks(double max, int maxIntervals)
        {
            long top = Double.IsNaN(max) || max <= 1 ? 1 : (long)Math.Ceiling(max);
            int intervals = Math.Max(1, maxIntervals);
            long step = Math.Max(1, (long)Math.Ceiling(top / (double)intervals));
            for (long candidate = step; candidate <= step * 2 && candidate < top; candidate++)
            {
                if (top % candidate == 0)
                {
                    step = candidate;
                    break;
                }
            }

            List<double> ticks = new List<double>();
            for (long value = 0; value < top; value += step)
            {
                if (value > 0 && (top - value) * 2 < step) break;
                ticks.Add(value);
            }

            ticks.Add(top);
            return ticks;
        }

        /// <summary>
        /// Vertical position of a value on a plot of a height (zero at the bottom, the axis maximum at the top), clamped.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="height">Plot height.</param>
        /// <param name="axisMax">Axis maximum.</param>
        /// <returns>Y.</returns>
        public static double ValueToY(double value, double height, double axisMax)
        {
            if (axisMax <= 0 || height <= 0) return Math.Max(0, height);
            double ratio = Math.Clamp(value / axisMax, 0, 1);
            return height - ratio * height;
        }

        /// <summary>
        /// Width of one bucket's slot across a plot.
        /// </summary>
        /// <param name="width">Plot width.</param>
        /// <param name="count">Buckets.</param>
        /// <returns>Slot width; zero when there are no buckets.</returns>
        public static double SlotWidth(double width, int count)
        {
            if (count <= 0 || width <= 0) return 0;
            return width / count;
        }

        /// <summary>
        /// The bucket under a horizontal position, or -1 outside the plot.
        /// </summary>
        /// <param name="x">Horizontal position.</param>
        /// <param name="width">Plot width.</param>
        /// <param name="count">Buckets.</param>
        /// <returns>Bucket index or -1.</returns>
        public static int BucketAt(double x, double width, int count)
        {
            if (count <= 0 || width <= 0 || Double.IsNaN(x) || x < 0 || x >= width) return -1;
            int index = (int)Math.Floor(x / SlotWidth(width, count));
            return Math.Clamp(index, 0, count - 1);
        }

        /// <summary>
        /// Rectangles of a stacked bar chart: for each bucket, each series with a value above zero stacked from the
        /// bottom in series order. Bars leave a gap between buckets when there is room.
        /// </summary>
        /// <param name="model">Chart.</param>
        /// <param name="width">Plot width.</param>
        /// <param name="height">Plot height.</param>
        /// <param name="axisMax">Axis maximum (see <see cref="BucketChartModel.AxisMax"/>).</param>
        /// <returns>Segments, bucket by bucket.</returns>
        public static List<ChartBarSegment> StackedBars(BucketChartModel model, double width, double height, double axisMax)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            List<ChartBarSegment> segments = new List<ChartBarSegment>();
            int count = model.BucketCount;
            double slot = SlotWidth(width, count);
            if (slot <= 0 || height <= 0 || axisMax <= 0) return segments;
            double gap = slot >= 4 ? Math.Max(1, slot * 0.2) : 0;
            double barWidth = Math.Max(0.5, slot - gap);
            for (int i = 0; i < count; i++)
            {
                double cumulative = 0;
                for (int s = 0; s < model.Series.Count; s++)
                {
                    double? value = model.Series[s].At(i);
                    if (!value.HasValue || value.Value <= 0) continue;
                    double bottom = ValueToY(cumulative, height, axisMax);
                    cumulative += value.Value;
                    double top = ValueToY(cumulative, height, axisMax);
                    ChartBarSegment segment = new ChartBarSegment();
                    segment.BucketIndex = i;
                    segment.SeriesIndex = s;
                    segment.X = i * slot + gap / 2;
                    segment.Y = top;
                    segment.Width = barWidth;
                    segment.Height = Math.Max(0, bottom - top);
                    segments.Add(segment);
                }
            }

            return segments;
        }

        /// <summary>
        /// Points of a line through the bucket centers. A null value breaks the line, so the result is a list of runs of
        /// consecutive values; a run of one point is a lone dot.
        /// </summary>
        /// <param name="values">Values, one per bucket.</param>
        /// <param name="width">Plot width.</param>
        /// <param name="height">Plot height.</param>
        /// <param name="axisMax">Axis maximum.</param>
        /// <returns>Runs of points.</returns>
        public static List<List<ChartPoint>> LineRuns(IList<double?> values, double width, double height, double axisMax)
        {
            List<List<ChartPoint>> runs = new List<List<ChartPoint>>();
            if (values == null || values.Count == 0) return runs;
            double slot = SlotWidth(width, values.Count);
            List<ChartPoint> current = new List<ChartPoint>();
            for (int i = 0; i < values.Count; i++)
            {
                double? value = values[i];
                if (!value.HasValue)
                {
                    if (current.Count > 0) runs.Add(current);
                    current = new List<ChartPoint>();
                    continue;
                }

                current.Add(new ChartPoint(i, (i + 0.5) * slot, ValueToY(value.Value, height, axisMax)));
            }

            if (current.Count > 0) runs.Add(current);
            return runs;
        }

        /// <summary>
        /// Points of a sparkline scaled between the smallest and largest value (a flat series sits mid-height), first
        /// point at the left edge and last at the right. Nulls break the line as in <see cref="LineRuns"/>.
        /// </summary>
        /// <param name="values">Values.</param>
        /// <param name="width">Width.</param>
        /// <param name="height">Height.</param>
        /// <returns>Runs of points.</returns>
        public static List<List<ChartPoint>> SparklineRuns(IList<double?> values, double width, double height)
        {
            List<List<ChartPoint>> runs = new List<List<ChartPoint>>();
            if (values == null || values.Count == 0) return runs;
            List<double> present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            if (present.Count == 0) return runs;
            double min = present.Min();
            double max = present.Max();
            double range = max - min;
            double step = values.Count > 1 ? width / (values.Count - 1) : 0;
            List<ChartPoint> current = new List<ChartPoint>();
            for (int i = 0; i < values.Count; i++)
            {
                double? value = values[i];
                if (!value.HasValue)
                {
                    if (current.Count > 0) runs.Add(current);
                    current = new List<ChartPoint>();
                    continue;
                }

                double x = values.Count > 1 ? i * step : width / 2;
                double y = range > 0 ? height - (value.Value - min) / range * height : height / 2;
                current.Add(new ChartPoint(i, x, y));
            }

            if (current.Count > 0) runs.Add(current);
            return runs;
        }

        /// <summary>
        /// Quantize values to levels 0 to <paramref name="levels"/> - 1 against zero and a maximum, for text sparklines: 0
        /// is exactly zero (or below), any value above zero is at least level 1, and the maximum is the top level. Null
        /// stays -1 (no value).
        /// </summary>
        /// <param name="values">Values.</param>
        /// <param name="max">Value of the top level (at least the largest value to avoid clipping).</param>
        /// <param name="levels">Number of levels (at least 2).</param>
        /// <returns>One level per value.</returns>
        public static List<int> Levels(IList<double?> values, double max, int levels)
        {
            if (levels < 2) throw new ArgumentOutOfRangeException(nameof(levels));
            List<int> result = new List<int>();
            if (values == null) return result;
            foreach (double? value in values)
            {
                if (!value.HasValue) { result.Add(-1); continue; }
                if (value.Value <= 0 || max <= 0) { result.Add(0); continue; }
                int level = (int)Math.Round(Math.Clamp(value.Value / max, 0, 1) * (levels - 1));
                result.Add(Math.Clamp(level, 1, levels - 1));
            }

            return result;
        }

        /// <summary>
        /// Combine adjacent buckets so a series fits in at most <paramref name="columns"/> columns: sums (counts) or
        /// maxima (levels such as peak concurrency). A group with only nulls stays null.
        /// </summary>
        /// <param name="values">Values.</param>
        /// <param name="columns">Columns available.</param>
        /// <param name="useMax">True to keep the largest value of each group instead of the sum.</param>
        /// <returns>Grouped values.</returns>
        public static List<double?> Downsample(IList<double?> values, int columns, bool useMax)
        {
            List<double?> result = new List<double?>();
            if (values == null || values.Count == 0 || columns <= 0) return result;
            int group = Math.Max(1, (int)Math.Ceiling(values.Count / (double)columns));
            for (int start = 0; start < values.Count; start += group)
            {
                double? acc = null;
                for (int i = start; i < Math.Min(values.Count, start + group); i++)
                {
                    double? value = values[i];
                    if (!value.HasValue) continue;
                    if (acc == null) acc = value.Value;
                    else acc = useMax ? Math.Max(acc.Value, value.Value) : acc.Value + value.Value;
                }

                result.Add(acc);
            }

            return result;
        }

        #endregion
    }
}
