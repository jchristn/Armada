namespace Armada.Core.Models
{
    /// <summary>
    /// Commit counts between two git refs, as reported by git rev-list --left-right --count base...head.
    /// </summary>
    public class GitDivergenceCounts
    {
        #region Public-Members

        /// <summary>
        /// Commits reachable from the head ref but not from the base ref. Negative values are clamped to 0.
        /// </summary>
        public int Ahead
        {
            get => _Ahead;
            set => _Ahead = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Commits reachable from the base ref but not from the head ref. Negative values are clamped to 0.
        /// </summary>
        public int Behind
        {
            get => _Behind;
            set => _Behind = value < 0 ? 0 : value;
        }

        #endregion

        #region Private-Members

        private int _Ahead = 0;
        private int _Behind = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public GitDivergenceCounts()
        {
        }

        /// <summary>
        /// Instantiate with counts.
        /// </summary>
        /// <param name="ahead">Commits ahead of the base ref.</param>
        /// <param name="behind">Commits behind the base ref.</param>
        public GitDivergenceCounts(int ahead, int behind)
        {
            Ahead = ahead;
            Behind = behind;
        }

        #endregion
    }
}
