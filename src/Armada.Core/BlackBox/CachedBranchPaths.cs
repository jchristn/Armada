namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The paths an active mission's branch changed, as read from git, and when they were read. Cached by
    /// <see cref="MissionOverlapAnalyzer"/> so a burst of creates on one vessel reads each branch once.
    /// </summary>
    internal sealed class CachedBranchPaths
    {
        /// <summary>
        /// Changed paths.
        /// </summary>
        public List<string> Paths { get; set; } = new List<string>();

        /// <summary>
        /// When the paths were read.
        /// </summary>
        public DateTime ReadUtc { get; set; } = DateTime.UtcNow;
    }
}
