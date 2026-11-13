namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// The result of a stage check: whether the stage's work was verified, and when it was not, why.
    /// </summary>
    public class StageCheckResult
    {
        #region Public-Members

        /// <summary>
        /// What the check established.
        /// </summary>
        public StageCheckStatusEnum Status { get; set; } = StageCheckStatusEnum.Skipped;

        /// <summary>
        /// Why the check ended with this status.
        /// </summary>
        public StageCheckReasonEnum Reason { get; set; } = StageCheckReasonEnum.None;

        /// <summary>
        /// The commands the check ran or would have run, in order.
        /// </summary>
        [CollectionBound(2, Reason = "The workflow profile build command and one unit-test command")]
        public List<string> Commands { get; set; } = new List<string>();

        /// <summary>
        /// Which unit-test command the check chose (full or abbreviated), or null when the profile had no unit-test command.
        /// </summary>
        public Armada.Core.Enums.UnitTestScopeEnum? UnitTestScope { get; set; } = null;

        /// <summary>
        /// The command that failed or timed out, or null.
        /// </summary>
        public string? FailedCommand { get; set; } = null;

        /// <summary>
        /// Exit code of the failed command, or null.
        /// </summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// Commands that failed on the stage's starting commit too, so they did not block the stage.
        /// </summary>
        [CollectionBound(2, Reason = "A subset of Commands")]
        public List<string> PreExistingFailures { get; set; } = new List<string>();

        /// <summary>
        /// The note sent back to the captain describing a failure or an unverified check, or null.
        /// </summary>
        public string? FailureMessage { get; set; } = null;

        /// <summary>
        /// How long the check took, in milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// When the check ended (UTC).
        /// </summary>
        public DateTime CheckedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the check blocks the stage. Only a confirmed Failed result blocks, even without a failure message; CouldNotRun and TimedOut are unverified warnings that never count as passing.
        /// </summary>
        public bool IsBlocking
        {
            get { return Status == StageCheckStatusEnum.Failed; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public StageCheckResult()
        {
        }

        /// <summary>
        /// A skipped check.
        /// </summary>
        /// <param name="reason">Why it was skipped.</param>
        /// <returns>The result.</returns>
        public static StageCheckResult Skip(StageCheckReasonEnum reason)
        {
            return new StageCheckResult { Status = StageCheckStatusEnum.Skipped, Reason = reason };
        }

        #endregion
    }
}
