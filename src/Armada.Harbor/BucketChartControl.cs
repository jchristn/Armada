namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Metrics.Charts;
    using Avalonia;
    using Avalonia.Media;

    /// <summary>
    /// Base for the bucketed time charts (<see cref="StackedBarChart"/> and <see cref="LineChart"/>): the value axis with
    /// gridlines, sparse time labels, the hovered bucket's highlight and tooltip (its time and every series' value), and an
    /// accessible name from <see cref="BucketChartModel.Summary"/>.
    /// </summary>
    public abstract class BucketChartControl : ChartControlBase
    {
        #region Public-Members

        /// <summary>
        /// What the chart shows, or null for nothing.
        /// </summary>
        public BucketChartModel? Model
        {
            get { return _Model; }
            set
            {
                _Model = value;
                _HoverIndex = -1;
                SetSummary(value?.Summary() ?? String.Empty);
                InvalidateVisual();
            }
        }

        /// <summary>
        /// Text drawn when the model has no data.
        /// </summary>
        public string EmptyText { get; set; } = "No data in this range";

        /// <summary>
        /// The hovered bucket, or -1.
        /// </summary>
        public int HoverIndex
        {
            get { return _HoverIndex; }
        }

        #endregion

        #region Private-Members

        private BucketChartModel? _Model = null;
        private int _HoverIndex = -1;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="height">Control height.</param>
        protected BucketChartControl(double height)
        {
            Height = height;
            MinWidth = 120;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            BucketChartModel? model = _Model;
            Rect bounds = new Rect(Bounds.Size);
            if (model == null || model.BucketCount == 0 || !model.HasData)
            {
                FormattedText empty = Label(EmptyText);
                context.DrawText(empty, new Point(Math.Max(0, (bounds.Width - empty.Width) / 2), Math.Max(0, (bounds.Height - empty.Height) / 2)));
                return;
            }

            double axisMax = model.AxisMax();
            Rect plot = PlotRect(model, axisMax);
            if (plot.Width <= 4 || plot.Height <= 4) return;
            DrawAxes(context, model, plot, axisMax);
            if (_HoverIndex >= 0 && _HoverIndex < model.BucketCount)
            {
                double slot = ChartGeometry.SlotWidth(plot.Width, model.BucketCount);
                context.FillRectangle(ThemeBrush("HarborChartHoverBrush"), new Rect(plot.X + _HoverIndex * slot, plot.Y, Math.Max(1, slot), plot.Height));
            }

            using (context.PushTransform(Matrix.CreateTranslation(plot.X, plot.Y)))
            {
                DrawSeries(context, model, plot.Size, axisMax);
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Draw the series inside the plot (origin at the plot's top left).
        /// </summary>
        /// <param name="context">Drawing context.</param>
        /// <param name="model">Model.</param>
        /// <param name="plot">Plot size.</param>
        /// <param name="axisMax">Axis maximum.</param>
        protected abstract void DrawSeries(DrawingContext context, BucketChartModel model, Size plot, double axisMax);

        /// <inheritdoc />
        protected override string? TipAt(Point point)
        {
            BucketChartModel? model = _Model;
            if (model == null || model.BucketCount == 0 || !model.HasData) return null;
            Rect plot = PlotRect(model, model.AxisMax());
            if (point.Y < plot.Top || point.Y > plot.Bottom) return null;
            int index = ChartGeometry.BucketAt(point.X - plot.X, plot.Width, model.BucketCount);
            return index < 0 ? null : model.Tooltip(index, TimeZoneInfo.Local);
        }

        /// <inheritdoc />
        protected override void OnHoverChanged(Point? point)
        {
            BucketChartModel? model = _Model;
            if (point == null || model == null || model.BucketCount == 0)
            {
                _HoverIndex = -1;
                return;
            }

            Rect plot = PlotRect(model, model.AxisMax());
            _HoverIndex = point.Value.Y < plot.Top || point.Value.Y > plot.Bottom ? -1 : ChartGeometry.BucketAt(point.Value.X - plot.X, plot.Width, model.BucketCount);
        }

        #endregion

        #region Private-Methods

        private Rect PlotRect(BucketChartModel model, double axisMax)
        {
            double gutter = 0;
            foreach (double value in AxisValues(axisMax)) gutter = Math.Max(gutter, Label(ChartFormat.Value(value, model.Format)).Width);
            double left = Math.Ceiling(gutter) + 6;
            double top = Math.Ceiling(LabelFontSize / 2) + 2;
            double bottom = LabelFontSize + 8;
            return new Rect(left, top, Math.Max(0, Bounds.Width - left - 4), Math.Max(0, Bounds.Height - top - bottom));
        }

        private static IEnumerable<double> AxisValues(double axisMax)
        {
            return new double[] { 0, axisMax / 2, axisMax };
        }

        private void DrawAxes(DrawingContext context, BucketChartModel model, Rect plot, double axisMax)
        {
            Pen grid = new Pen(ThemeBrush("HarborChartGridBrush"), 1);
            foreach (double value in AxisValues(axisMax))
            {
                double y = Math.Round(plot.Y + ChartGeometry.ValueToY(value, plot.Height, axisMax)) + 0.5;
                context.DrawLine(grid, new Point(plot.X, y), new Point(plot.Right, y));
                FormattedText label = Label(ChartFormat.Value(value, model.Format));
                context.DrawText(label, new Point(plot.X - 4 - label.Width, y - label.Height / 2));
            }

            double slot = ChartGeometry.SlotWidth(plot.Width, model.BucketCount);
            FormattedText sample = Label("Wed 00:00");
            int maxLabels = Math.Max(1, (int)Math.Floor(plot.Width / (sample.Width + 14)));
            int step = Math.Max(1, (int)Math.Ceiling(model.BucketCount / (double)maxLabels));
            double lastRight = Double.NegativeInfinity;
            for (int i = 0; i < model.BucketCount; i += step)
            {
                FormattedText label = Label(model.BucketLabel(i, TimeZoneInfo.Local));
                double x = plot.X + (i + 0.5) * slot - label.Width / 2;
                x = Math.Clamp(x, plot.X, Math.Max(plot.X, Bounds.Width - label.Width));
                if (x < lastRight + 6) continue;
                context.DrawText(label, new Point(x, plot.Bottom + 3));
                lastRight = x + label.Width;
            }
        }

        #endregion
    }
}
