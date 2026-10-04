namespace Armada.Core.Settings
{
    /// <summary>
    /// Thresholds that grade vessel health criteria. For each pair, a measured value at or above the Warn
    /// threshold grades Warn, and a value at or above the Fail threshold grades Fail (Fail takes precedence).
    /// All values apply live.
    /// </summary>
    public class RepositoryHealthThresholds
    {
        #region Public-Members

        /// <summary>
        /// Commits behind the default branch at which divergence grades Warn. Default 1, minimum 1, maximum 100000;
        /// out-of-range values are clamped.
        /// </summary>
        public int BehindWarn
        {
            get => _BehindWarn;
            set => _BehindWarn = Clamp(value, 1, 100000);
        }

        /// <summary>
        /// Commits behind the default branch at which divergence grades Fail (behind &gt;= this value). Default 21,
        /// minimum 1, maximum 100000; out-of-range values are clamped.
        /// </summary>
        public int BehindFail
        {
            get => _BehindFail;
            set => _BehindFail = Clamp(value, 1, 100000);
        }

        /// <summary>
        /// Stale branch count at which the branches criterion grades Warn. Default 4, minimum 1, maximum 10000;
        /// out-of-range values are clamped.
        /// </summary>
        public int StaleBranchWarn
        {
            get => _StaleBranchWarn;
            set => _StaleBranchWarn = Clamp(value, 1, 10000);
        }

        /// <summary>
        /// Stale branch count at which the branches criterion grades Fail. Default 11, minimum 1, maximum 10000;
        /// out-of-range values are clamped.
        /// </summary>
        public int StaleBranchFail
        {
            get => _StaleBranchFail;
            set => _StaleBranchFail = Clamp(value, 1, 10000);
        }

        /// <summary>
        /// Recent failed missions at which the mission outcomes criterion grades Warn. Default 1, minimum 1,
        /// maximum 1000; out-of-range values are clamped.
        /// </summary>
        public int MissionFailureWarn
        {
            get => _MissionFailureWarn;
            set => _MissionFailureWarn = Clamp(value, 1, 1000);
        }

        /// <summary>
        /// Recent failed missions at which the mission outcomes criterion grades Fail. Default 3, minimum 1,
        /// maximum 1000; out-of-range values are clamped.
        /// </summary>
        public int MissionFailureFail
        {
            get => _MissionFailureFail;
            set => _MissionFailureFail = Clamp(value, 1, 1000);
        }

        #endregion

        #region Private-Members

        private int _BehindWarn = 1;
        private int _BehindFail = 21;
        private int _StaleBranchWarn = 4;
        private int _StaleBranchFail = 11;
        private int _MissionFailureWarn = 1;
        private int _MissionFailureFail = 3;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public RepositoryHealthThresholds()
        {
        }

        #endregion

        #region Private-Methods

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        #endregion
    }
}
