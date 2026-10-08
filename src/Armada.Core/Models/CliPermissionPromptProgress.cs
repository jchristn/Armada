namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// A snapshot of one CLI permission prompt that is still running in this process: which request, which step it is
    /// in, and since when. Diagnostics only; the stored request stays the source of truth.
    /// </summary>
    public class CliPermissionPromptProgress
    {
        #region Public-Members

        /// <summary>
        /// Request identifier (assigned before the request is stored).
        /// </summary>
        public string RequestId { get; set; } = String.Empty;

        /// <summary>
        /// Ask thread of the prompt, or null.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// Mission of the prompt, or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Current step.
        /// </summary>
        public CliPermissionPromptStageEnum Stage { get; set; } = CliPermissionPromptStageEnum.EvaluatingRules;

        /// <summary>
        /// When the prompt arrived (UTC).
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the current step started (UTC).
        /// </summary>
        public DateTime StageStartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Message id of the posted Ask card, or null while it is not posted (or the prompt has no thread).
        /// </summary>
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// Error from posting the Ask card, or null.
        /// </summary>
        public string? CardError { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliPermissionPromptProgress()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// A copy of this snapshot.
        /// </summary>
        /// <returns>The copy.</returns>
        public CliPermissionPromptProgress Clone()
        {
            return new CliPermissionPromptProgress
            {
                RequestId = RequestId,
                ThreadId = ThreadId,
                MissionId = MissionId,
                ToolName = ToolName,
                Stage = Stage,
                StartedUtc = StartedUtc,
                StageStartedUtc = StageStartedUtc,
                MessageId = MessageId,
                CardError = CardError
            };
        }

        /// <summary>
        /// One line describing the snapshot, with ages relative to <paramref name="nowUtc"/>.
        /// </summary>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <returns>The description.</returns>
        public string Describe(DateTime nowUtc)
        {
            return RequestId + " " + ToolName
                + " stage=" + Stage + " for " + (int)(nowUtc - StageStartedUtc).TotalMilliseconds + " ms"
                + " (prompt age " + (int)(nowUtc - StartedUtc).TotalMilliseconds + " ms)"
                + " thread=" + (ThreadId ?? "-") + " mission=" + (MissionId ?? "-")
                + " messageId=" + (MessageId ?? "-")
                + (CardError != null ? " cardError=" + CardError : "");
        }

        #endregion
    }
}
