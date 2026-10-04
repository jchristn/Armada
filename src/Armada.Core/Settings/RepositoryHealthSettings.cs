namespace Armada.Core.Settings
{
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Settings for vessel (repository) health evaluation. All values apply live.
    /// </summary>
    public class RepositoryHealthSettings
    {
        #region Public-Members

        /// <summary>
        /// Minutes between scheduled evaluations of every active vessel. Default 360, minimum 0, maximum 10080;
        /// 0 disables scheduled evaluation (manual evaluation stays available). Out-of-range values are clamped.
        /// </summary>
        public int IntervalMinutes
        {
            get => _IntervalMinutes;
            set => _IntervalMinutes = value < 0 ? 0 : (value > 10080 ? 10080 : value);
        }

        /// <summary>
        /// Maximum number of vessels evaluated at once. Default 4, minimum 1, maximum 32; out-of-range values are
        /// clamped.
        /// </summary>
        public int MaxConcurrency
        {
            get => _MaxConcurrency;
            set => _MaxConcurrency = value < 1 ? 1 : (value > 32 ? 32 : value);
        }

        /// <summary>
        /// Whether to run git fetch --prune before computing divergence, so ahead and behind counts are current.
        /// Default true.
        /// </summary>
        public bool FetchBeforeEvaluate { get; set; } = true;

        /// <summary>
        /// Maximum age in hours of dependency and vulnerability results before they are re-evaluated even when the
        /// manifest hash is unchanged. Default 24, minimum 1, maximum 720; out-of-range values are clamped.
        /// </summary>
        public int DependencyMaxAgeHours
        {
            get => _DependencyMaxAgeHours;
            set => _DependencyMaxAgeHours = value < 1 ? 1 : (value > 720 ? 720 : value);
        }

        /// <summary>
        /// Timeout in seconds for each dependency tool invocation (for example dotnet list package). A timeout
        /// grades Unknown, never Pass. Default 120, minimum 10, maximum 900; out-of-range values are clamped.
        /// </summary>
        public int DependencyCommandTimeoutSeconds
        {
            get => _DependencyCommandTimeoutSeconds;
            set => _DependencyCommandTimeoutSeconds = value < 10 ? 10 : (value > 900 ? 900 : value);
        }

        /// <summary>
        /// Days without a commit after which a branch counts as stale. Default 90, minimum 1, maximum 3650;
        /// out-of-range values are clamped.
        /// </summary>
        public int StaleBranchDays
        {
            get => _StaleBranchDays;
            set => _StaleBranchDays = value < 1 ? 1 : (value > 3650 ? 3650 : value);
        }

        /// <summary>
        /// Window in days for counting failed and landing-failed missions. Default 7, minimum 1, maximum 90;
        /// out-of-range values are clamped.
        /// </summary>
        public int MissionWindowDays
        {
            get => _MissionWindowDays;
            set => _MissionWindowDays = value < 1 ? 1 : (value > 90 ? 90 : value);
        }

        /// <summary>
        /// Criteria that feed the overall (worst-of) status. Default: every criterion except ContinuousIntegration,
        /// CommitRecency, and Overall. Setting null yields an empty list (no criteria scored, so overall is Unknown).
        /// </summary>
        public List<VesselHealthCriterionEnum> ScoredCriteria
        {
            get => _ScoredCriteria;
            set => _ScoredCriteria = value ?? new List<VesselHealthCriterionEnum>();
        }

        /// <summary>
        /// Grading thresholds. Never null; setting null restores the defaults.
        /// </summary>
        public RepositoryHealthThresholds Thresholds
        {
            get => _Thresholds;
            set => _Thresholds = value ?? new RepositoryHealthThresholds();
        }

        #endregion

        #region Private-Members

        private int _IntervalMinutes = 360;
        private int _MaxConcurrency = 4;
        private int _DependencyMaxAgeHours = 24;
        private int _DependencyCommandTimeoutSeconds = 120;
        private int _StaleBranchDays = 90;
        private int _MissionWindowDays = 7;
        private List<VesselHealthCriterionEnum> _ScoredCriteria = new List<VesselHealthCriterionEnum>
        {
            VesselHealthCriterionEnum.GitDivergence,
            VesselHealthCriterionEnum.WorkingTree,
            VesselHealthCriterionEnum.Branches,
            VesselHealthCriterionEnum.Dependencies,
            VesselHealthCriterionEnum.Vulnerabilities,
            VesselHealthCriterionEnum.TestInfrastructure,
            VesselHealthCriterionEnum.ArmadaReadiness,
            VesselHealthCriterionEnum.MissionOutcomes
        };
        private RepositoryHealthThresholds _Thresholds = new RepositoryHealthThresholds();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public RepositoryHealthSettings()
        {
        }

        #endregion
    }
}
