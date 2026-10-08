namespace Armada.Core.Metrics.Charts
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// One stretch of a status strip placed on the window as fractions of its width (0 at the start, 1 at the end).
    /// </summary>
    public class StatusStripPart
    {
        #region Public-Members

        /// <summary>
        /// Link state during the stretch.
        /// </summary>
        public HarborLinkSegmentStateEnum State { get; set; } = HarborLinkSegmentStateEnum.Unknown;

        /// <summary>
        /// Start as a fraction of the window (0 to 1).
        /// </summary>
        public double Start { get; set; } = 0;

        /// <summary>
        /// Width as a fraction of the window (0 to 1).
        /// </summary>
        public double Width { get; set; } = 0;

        /// <summary>
        /// Start (UTC), clipped to the window.
        /// </summary>
        public DateTime StartUtc { get; set; }

        /// <summary>
        /// End (UTC), clipped to the window.
        /// </summary>
        public DateTime EndUtc { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public StatusStripPart()
        {
        }

        #endregion
    }
}
