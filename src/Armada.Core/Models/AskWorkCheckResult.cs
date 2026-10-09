namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// The outcome of one check run of a mission of finished tracked work (see <see cref="AskWorkMissionResult"/>).
    /// </summary>
    public class AskWorkCheckResult
    {
        #region Public-Members

        /// <summary>
        /// Check run identifier.
        /// </summary>
        public string CheckRunId
        {
            get => _CheckRunId;
            set => _CheckRunId = value ?? String.Empty;
        }

        /// <summary>
        /// Display label, or null.
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Check type (CheckRunTypeEnum name).
        /// </summary>
        public string Type
        {
            get => _Type;
            set => _Type = value ?? String.Empty;
        }

        /// <summary>
        /// Check status (CheckRunStatusEnum name).
        /// </summary>
        public string Status
        {
            get => _Status;
            set => _Status = value ?? String.Empty;
        }

        /// <summary>
        /// Process exit code, or null.
        /// </summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// The check's own summary, or null.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Total tests reported, or null.
        /// </summary>
        public int? TestsTotal { get; set; } = null;

        /// <summary>
        /// Passed tests reported, or null.
        /// </summary>
        public int? TestsPassed { get; set; } = null;

        /// <summary>
        /// Failed tests reported, or null.
        /// </summary>
        public int? TestsFailed { get; set; } = null;

        /// <summary>
        /// Skipped tests reported, or null.
        /// </summary>
        public int? TestsSkipped { get; set; } = null;

        /// <summary>
        /// Check duration in milliseconds, or null.
        /// </summary>
        public long? DurationMs { get; set; } = null;

        #endregion

        #region Private-Members

        private string _CheckRunId = String.Empty;
        private string _Type = String.Empty;
        private string _Status = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskWorkCheckResult()
        {
        }

        #endregion
    }
}
