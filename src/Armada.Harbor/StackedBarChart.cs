namespace Armada.Harbor
{
    using System.Collections.Generic;
    using Armada.Core.Metrics.Charts;
    using Avalonia;
    using Avalonia.Media;

    /// <summary>
    /// A stacked bar chart of time buckets (jobs over time, tokens by runtime and model): one bar per bucket with each
    /// series stacked bottom to top in its theme color. Geometry from <see cref="ChartGeometry.StackedBars"/>.
    /// </summary>
    public class StackedBarChart : BucketChartControl
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="height">Control height.</param>
        public StackedBarChart(double height = 180) : base(height)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void DrawSeries(DrawingContext context, BucketChartModel model, Size plot, double axisMax)
        {
            List<IBrush> brushes = new List<IBrush>();
            foreach (ChartSeriesData series in model.Series) brushes.Add(ChartPalette.Brush(this, series.Color));
            foreach (ChartBarSegment segment in ChartGeometry.StackedBars(model, plot.Width, plot.Height, axisMax))
            {
                context.FillRectangle(brushes[segment.SeriesIndex], new Rect(segment.X, segment.Y, segment.Width, segment.Height));
            }
        }

        #endregion
    }
}
