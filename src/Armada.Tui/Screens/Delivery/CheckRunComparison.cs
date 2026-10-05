namespace Armada.Tui.Screens.Delivery
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// A check run compared with its baseline (the dashboard's <c>CheckRunComparison</c> in
    /// <c>checkRunComparison.ts</c>): the previous terminal run of the same vessel and check type, matched by branch,
    /// profile, or environment when possible, with status, duration, artifact, test, and coverage deltas.
    /// </summary>
    public class CheckRunComparison
    {
        #region Public-Members

        /// <summary>
        /// Baseline run.
        /// </summary>
        public CheckRun Baseline { get; set; }

        /// <summary>
        /// Scope: same-branch, same-profile, same-environment, or same-check-type.
        /// </summary>
        public string Scope { get; set; } = "same-check-type";

        /// <summary>
        /// True when the status differs from the baseline.
        /// </summary>
        public bool StatusChanged { get; set; } = false;

        /// <summary>
        /// True when the run regressed (status, failed tests, or coverage).
        /// </summary>
        public bool HasRegression { get; set; } = false;

        /// <summary>
        /// True when the run improved.
        /// </summary>
        public bool HasImprovement { get; set; } = false;

        /// <summary>
        /// Duration delta in milliseconds, or null.
        /// </summary>
        public double? DurationDeltaMs { get; set; } = null;

        /// <summary>
        /// Artifact count delta.
        /// </summary>
        public int ArtifactCountDelta { get; set; } = 0;

        /// <summary>
        /// Passed tests delta.
        /// </summary>
        public double? PassedDelta { get; set; } = null;

        /// <summary>
        /// Failed tests delta.
        /// </summary>
        public double? FailedDelta { get; set; } = null;

        /// <summary>
        /// Skipped tests delta.
        /// </summary>
        public double? SkippedDelta { get; set; } = null;

        /// <summary>
        /// Total tests delta.
        /// </summary>
        public double? TotalDelta { get; set; } = null;

        /// <summary>
        /// Line coverage delta (percentage points).
        /// </summary>
        public double? LinesPctDelta { get; set; } = null;

        /// <summary>
        /// Branch coverage delta.
        /// </summary>
        public double? BranchesPctDelta { get; set; } = null;

        /// <summary>
        /// Function coverage delta.
        /// </summary>
        public double? FunctionsPctDelta { get; set; } = null;

        /// <summary>
        /// Statement coverage delta.
        /// </summary>
        public double? StatementsPctDelta { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="baseline">Baseline run.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseline"/> is null.</exception>
        public CheckRunComparison(CheckRun baseline)
        {
            Baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        }

        #endregion
    }
}
