namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Metrics.Charts;
    using Avalonia;
    using Avalonia.Media;

    /// <summary>
    /// A small trend line with no axes (launch speed per runtime), scaled between its lowest and highest value, with a
    /// tooltip for the hovered point and an accessible name from <see cref="SparklineModel.Summary"/>. Geometry from
    /// <see cref="ChartGeometry.SparklineRuns"/>.
    /// </summary>
    public class Sparkline : ChartControlBase
    {
        #region Public-Members

        /// <summary>
        /// What the sparkline shows, or null.
        /// </summary>
        public SparklineModel? Model
        {
            get { return _Model; }
            set
            {
                _Model = value;
                SetSummary(value?.Summary() ?? String.Empty);
                InvalidateVisual();
            }
        }

        /// <summary>
        /// Bucket starts (UTC) for the tooltip's time, or empty to leave the time out.
        /// </summary>
        public List<DateTime> BucketStartsUtc { get; set; } = new List<DateTime>();

        /// <summary>
        /// Bucket width in minutes, for the tooltip's time.
        /// </summary>
        public int BucketMinutes { get; set; } = 30;

        #endregion

        #region Private-Members

        private const double _Pad = 3;
        private SparklineModel? _Model = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="width">Width.</param>
        /// <param name="height">Height.</param>
        public Sparkline(double width = 110, double height = 24)
        {
            Width = width;
            Height = height;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            SparklineModel? model = _Model;
            if (model == null || !model.HasData)
            {
                FormattedText dash = Label("-");
                context.DrawText(dash, new Point(0, (Bounds.Height - dash.Height) / 2));
                return;
            }

            IBrush brush = ChartPalette.Brush(this, model.Color);
            Pen pen = new Pen(brush, 1.5, null, PenLineCap.Round, PenLineJoin.Round);
            double width = Math.Max(1, Bounds.Width - 2 * _Pad);
            double height = Math.Max(1, Bounds.Height - 2 * _Pad);
            foreach (List<ChartPoint> run in ChartGeometry.SparklineRuns(model.Values, width, height))
            {
                if (run.Count == 1)
                {
                    context.DrawEllipse(brush, null, new Point(run[0].X + _Pad, run[0].Y + _Pad), 1.75, 1.75);
                    continue;
                }

                StreamGeometry geometry = new StreamGeometry();
                using (StreamGeometryContext g = geometry.Open())
                {
                    g.BeginFigure(new Point(run[0].X + _Pad, run[0].Y + _Pad), false);
                    for (int i = 1; i < run.Count; i++) g.LineTo(new Point(run[i].X + _Pad, run[i].Y + _Pad));
                    g.EndFigure(false);
                }

                context.DrawGeometry(null, pen, geometry);
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string? TipAt(Point point)
        {
            SparklineModel? model = _Model;
            if (model == null || !model.HasData) return null;
            int count = model.Values.Count;
            double width = Math.Max(1, Bounds.Width - 2 * _Pad);
            int index = count > 1 ? (int)Math.Round((point.X - _Pad) / (width / (count - 1))) : 0;
            index = Math.Clamp(index, 0, count - 1);
            string value = ChartFormat.Value(model.Values[index], model.Format);
            string time = index < BucketStartsUtc.Count ? ChartFormat.BucketSpan(BucketStartsUtc[index], BucketMinutes, TimeZoneInfo.Local) + "\n" : String.Empty;
            return time + model.Name + ": " + value;
        }

        #endregion
    }
}
