namespace Armada.Core.BlackBox
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// An active mission and the files it is likely to touch, as compared by <see cref="MissionOverlapAnalyzer"/>.
    /// </summary>
    public class ActiveMissionFileSet
    {
        #region Public-Members

        /// <summary>The mission.</summary>
        public Mission Mission { get; }

        /// <summary>Everything the mission is likely to touch (scoped files, brief paths, recent path commits, diff or branch).</summary>
        public MissionFileSet Likely { get; } = new MissionFileSet();

        /// <summary>The mission's scoped files only (they reserve hotspot directories).</summary>
        public MissionFileSet Scoped { get; } = new MissionFileSet();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        public ActiveMissionFileSet(Mission mission)
        {
            Mission = mission ?? throw new ArgumentNullException(nameof(mission));
        }

        #endregion
    }
}
