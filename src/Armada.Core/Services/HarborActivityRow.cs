namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// One line of the activity log's summary view. A line can change in place after it is added: a request's line
    /// becomes the request and its result, a run of git and file work counts each new command, and a run of heartbeats
    /// counts each new heartbeat.
    /// </summary>
    internal class HarborActivityRow
    {
        #region Public-Members

        /// <summary>
        /// When the line's first entry happened (UTC); for a heartbeat run, the latest heartbeat.
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The text after the time.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// For a request's line still waiting for its result, the request ID; otherwise null.
        /// </summary>
        public string? PendingRequestId { get; set; } = null;

        /// <summary>
        /// For a run of git and file work, what it is about (its key); otherwise null.
        /// </summary>
        public string? RunKey { get; set; } = null;

        /// <summary>
        /// For a run, the label at the start of its line.
        /// </summary>
        public string RunLabel { get; set; } = String.Empty;

        /// <summary>
        /// For a run, the stage of the dock it is in.
        /// </summary>
        public HarborLogStageEnum RunStage { get; set; } = HarborLogStageEnum.None;

        /// <summary>
        /// For a run, what kind of directory its work is in.
        /// </summary>
        public HarborLogPathKindEnum RunPathKind { get; set; } = HarborLogPathKindEnum.None;

        /// <summary>
        /// For a run, the git commands counted.
        /// </summary>
        public int GitCommands { get; set; } = 0;

        /// <summary>
        /// For a run, the file writes counted.
        /// </summary>
        public int FileWrites { get; set; } = 0;

        /// <summary>
        /// For a heartbeat run, the heartbeats counted; otherwise 0.
        /// </summary>
        public int Heartbeats { get; set; } = 0;

        /// <summary>
        /// For a heartbeat run, when its first heartbeat happened (UTC).
        /// </summary>
        public DateTime HeartbeatRunStartUtc { get; set; } = DateTime.MinValue;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The line: "HH:mm:ss  text".
        /// </summary>
        /// <returns>The line.</returns>
        public override string ToString()
        {
            return TimestampUtc.ToLocalTime().ToString("HH:mm:ss") + "  " + Text;
        }

        #endregion
    }
}
