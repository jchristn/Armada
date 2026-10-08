namespace Armada.Harbor
{
    using System.Collections.Generic;
    using Armada.Core.Metrics.Charts;
    using Avalonia;
    using Avalonia.Media;

    /// <summary>
    /// A line chart of time buckets (slot usage, heartbeat round trip): one line per series through the bucket centers,
    /// dashed where the series says so, gaps where a bucket has no value, and a dashed reference line (the slot
    /// capacity) labeled at the right. Geometry from <see cref="ChartGeometry.LineRuns"/>.
    /// </summary>
    public class LineChart : BucketChartControl
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="height">Control height.</param>
        public LineChart(double height = 180) : base(height)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void DrawSeries(DrawingContext context, BucketChartModel model, Size plot, double axisMax)
        {
            if (model.Reference != null)
            {
                IBrush brush = ChartPalette.Brush(this, model.Reference.Color);
                double y = ChartGeometry.ValueToY(model.Reference.Value, plot.Height, axisMax);
                Pen dashed = new Pen(brush, 1.5, new DashStyle(new double[] { 4, 3 }, 0));
                context.DrawLine(dashed, new Point(0, y), new Point(plot.Width, y));
                FormattedText label = Label(model.Reference.Label, brush);
                double labelY = y - label.Height - 1 < 0 ? y + 1 : y - label.Height - 1;
                context.DrawText(label, new Point(System.Math.Max(0, plot.Width - label.Width - 2), labelY));
            }

            foreach (ChartSeriesData series in model.Series)
            {
                IBrush brush = ChartPalette.Brush(this, series.Color);
                Pen pen = series.Dashed
                    ? new Pen(brush, 1.75, new DashStyle(new double[] { 3, 2 }, 0), PenLineCap.Flat, PenLineJoin.Round)
                    : new Pen(brush, 1.75, null, PenLineCap.Round, PenLineJoin.Round);
                foreach (List<ChartPoint> run in ChartGeometry.LineRuns(series.Values, plot.Width, plot.Height, axisMax))
                {
                    if (run.Count == 1)
                    {
                        context.DrawEllipse(brush, null, new Point(run[0].X, run[0].Y), 2, 2);
                        continue;
                    }

                    StreamGeometry geometry = new StreamGeometry();
                    using (StreamGeometryContext g = geometry.Open())
                    {
                        g.BeginFigure(new Point(run[0].X, run[0].Y), false);
                        for (int i = 1; i < run.Count; i++) g.LineTo(new Point(run[i].X, run[i].Y));
                        g.EndFigure(false);
                    }

                    context.DrawGeometry(null, pen, geometry);
                }

                if (HoverIndex >= 0)
                {
                    double? value = series.At(HoverIndex);
                    if (value.HasValue)
                    {
                        double x = (HoverIndex + 0.5) * ChartGeometry.SlotWidth(plot.Width, model.BucketCount);
                        context.DrawEllipse(brush, null, new Point(x, ChartGeometry.ValueToY(value.Value, plot.Height, axisMax)), 3, 3);
                    }
                }
            }
        }

        #endregion
    }
}
