namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// One stretch of a Harbor link-health timeline.
    /// </summary>
    public class HarborLinkSegment
    {
        #region Public-Members

        /// <summary>
        /// The link state during the stretch.
        /// </summary>
        public HarborLinkSegmentStateEnum State { get; set; } = HarborLinkSegmentStateEnum.Unknown;

        /// <summary>
        /// Inclusive start (UTC).
        /// </summary>
        public DateTime StartUtc { get; set; }

        /// <summary>
        /// Exclusive end (UTC).
        /// </summary>
        public DateTime EndUtc { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborLinkSegment()
        {
        }

        #endregion
    }
}
