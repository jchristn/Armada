namespace Armada.Core.Metrics.Charts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// A status strip: a Harbor link's connected, reconnecting, and down stretches across a window. Places the stretches
    /// on the window, picks the state to show in each column of a text strip (the worst state in the column, so a short
    /// outage stays visible), and writes tooltips and an accessible summary that do not depend on color.
    /// </summary>
    public class StatusStripModel
    {
        #region Public-Members

        /// <summary>
        /// Title (for example "Link health").
        /// </summary>
        public string Title { get; set; } = "Link health";

        /// <summary>
        /// Plain-language range (for example "last 24 hours").
        /// </summary>
        public string RangeName { get; set; } = String.Empty;

        /// <summary>
        /// Window start (UTC).
        /// </summary>
        public DateTime FromUtc { get; set; }

        /// <summary>
        /// Window end (UTC).
        /// </summary>
        public DateTime ToUtc { get; set; }

        /// <summary>
        /// Stretches in time order, clipped to the window. Never null.
        /// </summary>
        public List<StatusStripPart> Parts
        {
            get { return _Parts; }
            set { _Parts = value ?? new List<StatusStripPart>(); }
        }

        /// <summary>
        /// Share of the known time the link was connected (0 to 100), or null.
        /// </summary>
        public double? ConnectedPercent { get; set; } = null;

        /// <summary>
        /// Times the link dropped in the window.
        /// </summary>
        public int Disconnects { get; set; } = 0;

        /// <summary>
        /// The states in the order a legend lists them.
        /// </summary>
        public static IReadOnlyList<HarborLinkSegmentStateEnum> LegendStates { get; } = new List<HarborLinkSegmentStateEnum>
        {
            HarborLinkSegmentStateEnum.Connected,
            HarborLinkSegmentStateEnum.Reconnecting,
            HarborLinkSegmentStateEnum.Down,
            HarborLinkSegmentStateEnum.Unknown
        };

        #endregion

        #region Private-Members

        private List<StatusStripPart> _Parts = new List<StatusStripPart>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public StatusStripModel()
        {
        }

        /// <summary>
        /// Place link segments on a window: each segment is clipped to [from, to), and empty ones are dropped.
        /// </summary>
        /// <param name="segments">Segments in time order.</param>
        /// <param name="fromUtc">Window start (UTC).</param>
        /// <param name="toUtc">Window end (UTC).</param>
        /// <returns>The strip.</returns>
        public static StatusStripModel FromSegments(IEnumerable<HarborLinkSegment> segments, DateTime fromUtc, DateTime toUtc)
        {
            StatusStripModel model = new StatusStripModel();
            model.FromUtc = fromUtc;
            model.ToUtc = toUtc;
            double span = (toUtc - fromUtc).TotalMilliseconds;
            if (segments == null || span <= 0) return model;
            foreach (HarborLinkSegment segment in segments)
            {
                if (segment == null) continue;
                DateTime start = segment.StartUtc > fromUtc ? segment.StartUtc : fromUtc;
                DateTime end = segment.EndUtc < toUtc ? segment.EndUtc : toUtc;
                if (end <= start) continue;
                StatusStripPart part = new StatusStripPart();
                part.State = segment.State;
                part.StartUtc = start;
                part.EndUtc = end;
                part.Start = (start - fromUtc).TotalMilliseconds / span;
                part.Width = (end - start).TotalMilliseconds / span;
                model._Parts.Add(part);
            }

            return model;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Display name of a state.
        /// </summary>
        /// <param name="state">State.</param>
        /// <returns>Connected, Reconnecting, Down, or No data.</returns>
        public static string StateLabel(HarborLinkSegmentStateEnum state)
        {
            switch (state)
            {
                case HarborLinkSegmentStateEnum.Connected: return "Connected";
                case HarborLinkSegmentStateEnum.Reconnecting: return "Reconnecting";
                case HarborLinkSegmentStateEnum.Down: return "Down";
                default: return "No data";
            }
        }

        /// <summary>
        /// Color role of a state.
        /// </summary>
        /// <param name="state">State.</param>
        /// <returns>Success, Warning, Danger, or Idle.</returns>
        public static ChartColorEnum StateColor(HarborLinkSegmentStateEnum state)
        {
            switch (state)
            {
                case HarborLinkSegmentStateEnum.Connected: return ChartColorEnum.Success;
                case HarborLinkSegmentStateEnum.Reconnecting: return ChartColorEnum.Warning;
                case HarborLinkSegmentStateEnum.Down: return ChartColorEnum.Danger;
                default: return ChartColorEnum.Idle;
            }
        }

        /// <summary>
        /// The stretch at a position across the window (0 to 1), or null where nothing is known.
        /// </summary>
        /// <param name="fraction">Position.</param>
        /// <returns>The part or null.</returns>
        public StatusStripPart? PartAt(double fraction)
        {
            if (Double.IsNaN(fraction) || fraction < 0 || fraction > 1) return null;
            foreach (StatusStripPart part in _Parts)
            {
                if (fraction >= part.Start && fraction < part.Start + part.Width) return part;
            }

            return null;
        }

        /// <summary>
        /// The state each of <paramref name="columns"/> equal columns shows: the worst state any stretch has in that column
        /// (Down, then Reconnecting, then Connected), or Unknown where nothing is known.
        /// </summary>
        /// <param name="columns">Columns.</param>
        /// <returns>One state per column.</returns>
        public List<HarborLinkSegmentStateEnum> StatesForColumns(int columns)
        {
            List<HarborLinkSegmentStateEnum> result = new List<HarborLinkSegmentStateEnum>();
            if (columns <= 0) return result;
            for (int c = 0; c < columns; c++)
            {
                double left = c / (double)columns;
                double right = (c + 1) / (double)columns;
                HarborLinkSegmentStateEnum worst = HarborLinkSegmentStateEnum.Unknown;
                foreach (StatusStripPart part in _Parts)
                {
                    double end = part.Start + part.Width;
                    if (end <= left || part.Start >= right) continue;
                    if (Severity(part.State) > Severity(worst)) worst = part.State;
                }

                result.Add(worst);
            }

            return result;
        }

        /// <summary>
        /// Total time in a state over the window.
        /// </summary>
        /// <param name="state">State.</param>
        /// <returns>Duration.</returns>
        public TimeSpan TimeIn(HarborLinkSegmentStateEnum state)
        {
            TimeSpan total = TimeSpan.Zero;
            foreach (StatusStripPart part in _Parts.Where(p => p.State == state)) total += part.EndUtc - part.StartUtc;
            return total;
        }

        /// <summary>
        /// Tooltip text of a stretch: its state, the time it covers, and how long it lasted.
        /// </summary>
        /// <param name="part">Stretch.</param>
        /// <param name="zone">Viewer's time zone.</param>
        /// <returns>Text.</returns>
        public static string Tooltip(StatusStripPart part, TimeZoneInfo zone)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));
            return StateLabel(part.State) + "\n" + ChartFormat.Span(part.StartUtc, part.EndUtc, zone)
                + "\n" + ChartFormat.Duration((part.EndUtc - part.StartUtc).TotalMilliseconds);
        }

        /// <summary>
        /// One-sentence description for assistive technology: the share of time connected, the drops, and the time in
        /// each state.
        /// </summary>
        /// <returns>Text.</returns>
        public string Summary()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Title);
            if (!String.IsNullOrEmpty(RangeName)) sb.Append(", ").Append(RangeName);
            sb.Append(": ");
            if (_Parts.All(p => p.State == HarborLinkSegmentStateEnum.Unknown))
            {
                sb.Append("no link data");
                return sb.ToString();
            }

            if (ConnectedPercent.HasValue) sb.Append("connected ").Append(Math.Round(ConnectedPercent.Value, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Append("% of the time, ");
            sb.Append(Disconnects == 1 ? "1 disconnect" : Disconnects.ToString(System.Globalization.CultureInfo.InvariantCulture) + " disconnects");
            List<string> times = new List<string>();
            foreach (HarborLinkSegmentStateEnum state in LegendStates)
            {
                TimeSpan time = TimeIn(state);
                if (time > TimeSpan.Zero) times.Add(StateLabel(state) + " " + ChartFormat.Duration(time.TotalMilliseconds));
            }

            if (times.Count > 0) sb.Append("; ").Append(String.Join(", ", times));
            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private static int Severity(HarborLinkSegmentStateEnum state)
        {
            switch (state)
            {
                case HarborLinkSegmentStateEnum.Down: return 3;
                case HarborLinkSegmentStateEnum.Reconnecting: return 2;
                case HarborLinkSegmentStateEnum.Connected: return 1;
                default: return 0;
            }
        }

        #endregion
    }
}
