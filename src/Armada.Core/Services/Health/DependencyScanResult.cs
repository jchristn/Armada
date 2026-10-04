namespace Armada.Core.Services.Health
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Outcome of a dependency scan (one tool output, or a whole repository). When <see cref="ErrorCode"/> is set, the
    /// scan failed (or partly failed) and the criterion must grade Unknown, never Pass; dependencies found by the parts
    /// that succeeded are still reported.
    /// </summary>
    public class DependencyScanResult
    {
        #region Public-Members

        /// <summary>
        /// Detail code of the first failure (ToolMissing, RestoreRequired, Timeout, ParseError, ToolFailed), or null.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Value accompanying <see cref="ErrorCode"/> (timeout seconds for Timeout, exit code for ToolFailed), or null.
        /// </summary>
        public long? ErrorValue { get; set; } = null;

        /// <summary>
        /// Whether at least one NuGet or npm target was scanned.
        /// </summary>
        public bool HasTargets { get; set; } = false;

        /// <summary>
        /// Dependencies found. Never null.
        /// </summary>
        public List<VesselDependency> Dependencies { get; } = new List<VesselDependency>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DependencyScanResult()
        {
        }

        /// <summary>
        /// Create a failed result.
        /// </summary>
        /// <param name="errorCode">Detail code.</param>
        /// <param name="errorValue">Accompanying value, or null.</param>
        /// <returns>The result.</returns>
        public static DependencyScanResult Failed(string errorCode, long? errorValue = null)
        {
            DependencyScanResult result = new DependencyScanResult();
            result.ErrorCode = errorCode;
            result.ErrorValue = errorValue;
            return result;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record a failure unless one is already recorded (the first failure wins).
        /// </summary>
        /// <param name="errorCode">Detail code.</param>
        /// <param name="errorValue">Accompanying value, or null.</param>
        public void AddFailure(string? errorCode, long? errorValue)
        {
            if (errorCode == null || ErrorCode != null) return;
            ErrorCode = errorCode;
            ErrorValue = errorValue;
        }

        #endregion
    }
}
