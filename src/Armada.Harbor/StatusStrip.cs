namespace Armada.Harbor
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Metrics.Charts;
    using Avalonia;
    using Avalonia.Media;

    /// <summary>
    /// A status strip (link health): the window as a bar with each connected, reconnecting, and down stretch in its theme
    /// color, a short drop kept at least a few pixels wide so it stays visible, a tooltip naming the hovered stretch's state
    /// and times, and an accessible name from <see cref="StatusStripModel.Summary"/>.
    /// </summary>
    public class StatusStrip : ChartControlBase
    {
        #region Public-Members

        /// <summary>
        /// What the strip shows, or null.
        /// </summary>
        public StatusStripModel? Model
        {
            get { return _Model; }
            set
            {
                _Model = value;
                SetSummary(value?.Summary() ?? String.Empty);
                InvalidateVisual();
            }
        }

        #endregion

        #region Private-Members

        private StatusStripModel? _Model = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="height">Height.</param>
        public StatusStrip(double height = 18)
        {
            Height = height;
            MinWidth = 60;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            Rect bounds = new Rect(Bounds.Size);
            context.FillRectangle(ThemeBrush("HarborChartTrackBrush"), bounds, 3);
            StatusStripModel? model = _Model;
            if (model == null) return;
            foreach (StatusStripPart part in model.Parts)
            {
                if (part.State == HarborLinkSegmentStateEnum.Unknown) continue;
                // A drop of a few seconds in a 7-day window is under a pixel; keep problems at least 3px wide.
                double min = part.State == HarborLinkSegmentStateEnum.Connected ? 0.5 : 3;
                double x = part.Start * bounds.Width;
                double width = Math.Max(min, part.Width * bounds.Width);
                context.FillRectangle(ChartPalette.Brush(this, StatusStripModel.StateColor(part.State)), new Rect(x, 0, Math.Min(width, bounds.Width - x), bounds.Height));
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string? TipAt(Point point)
        {
            StatusStripModel? model = _Model;
            if (model == null || Bounds.Width <= 0) return null;
            StatusStripPart? part = model.PartAt(point.X / Bounds.Width);
            if (part == null) return StatusStripModel.StateLabel(HarborLinkSegmentStateEnum.Unknown);
            return StatusStripModel.Tooltip(part, TimeZoneInfo.Local);
        }

        #endregion
    }
}
