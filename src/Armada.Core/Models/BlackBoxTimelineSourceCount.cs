namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Items one source contributed to a combined timeline.
    /// </summary>
    public class BlackBoxTimelineSourceCount
    {
        #region Public-Members

        /// <summary>
        /// Source.
        /// </summary>
        public BlackBoxTimelineSourceEnum Source { get; set; } = BlackBoxTimelineSourceEnum.Mission;

        /// <summary>
        /// Items collected from it.
        /// </summary>
        public int Count { get; set; } = 0;

        /// <summary>
        /// True when the source had more items than the per-source cap (200).
        /// </summary>
        public bool Truncated { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxTimelineSourceCount()
        {
        }

        #endregion
    }
}
