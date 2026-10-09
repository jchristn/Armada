namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// A single Harbor link log entry: when it happened, whether it was work coming in from the Admiral or
    /// status going back out, and a human-readable message. Surfaced to the Harbor app's log view so an
    /// operator can watch work being issued and results propagating back over the link. Typed fields set where the
    /// entry is logged (level, category, outcome, request and result pairing, and what the entry is about) let the
    /// activity log summarize without reading the message.
    /// </summary>
    public class HarborLogEntry
    {
        #region Public-Members

        /// <summary>
        /// When the entry was created (UTC).
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The direction of the entry relative to the Harbor.
        /// </summary>
        public HarborLogDirection Direction { get; set; } = HarborLogDirection.Info;

        /// <summary>
        /// True for a link heartbeat, so the activity log can collapse a run of them into one line.
        /// </summary>
        public bool IsHeartbeat { get; set; } = false;

        /// <summary>
        /// Summary (shown by default) or Detail (shown only in the detail view; a failure is always shown).
        /// </summary>
        public HarborLogLevelEnum Level { get; set; } = HarborLogLevelEnum.Summary;

        /// <summary>
        /// What the entry is about: the link, a job, a dock, git, a file, a check run, and so on.
        /// </summary>
        public HarborLogCategoryEnum Category { get; set; } = HarborLogCategoryEnum.General;

        /// <summary>
        /// How the operation turned out: None for a request or an event, Ok, or Failed. A command whose non-zero exit the
        /// caller declared expected is Ok.
        /// </summary>
        public HarborLogOutcomeEnum Outcome { get; set; } = HarborLogOutcomeEnum.None;

        /// <summary>
        /// Whether the entry is a request, the result of one (with the same <see cref="RequestId"/>), or neither.
        /// </summary>
        public HarborLogPhaseEnum Phase { get; set; } = HarborLogPhaseEnum.None;

        /// <summary>
        /// The request a request or result entry belongs to (a launch and its start share the job ID), or null.
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// The job the entry is about, or null.
        /// </summary>
        public string? JobId { get; set; } = null;

        /// <summary>
        /// The mission the entry is about (a mission dock's mission, or a mission job), or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// The vessel the entry is about, or null.
        /// </summary>
        public string? VesselName { get; set; } = null;

        /// <summary>
        /// The directory the work happens in (a dock, a checkout, a scratch directory), or null.
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// What <see cref="Path"/> is, so the activity log can shorten it.
        /// </summary>
        public HarborLogPathKindEnum PathKind { get; set; } = HarborLogPathKindEnum.None;

        /// <summary>
        /// For work in a mission dock, where the dock is in its life (preparing, running, finishing).
        /// </summary>
        public HarborLogStageEnum Stage { get; set; } = HarborLogStageEnum.None;

        /// <summary>
        /// The line for the activity log's summary view: no request IDs, short paths, and for a result the request and
        /// its outcome on one line. Falls back to <see cref="Message"/> when not set. Never ends with a period.
        /// </summary>
        public string Summary
        {
            get => _Summary.Length > 0 ? _Summary : _Message;
            set => _Summary = Normalize(value);
        }

        /// <summary>
        /// The message. Never contains secrets. Never null (null becomes empty). Harbor log lines never end with a
        /// period: surrounding whitespace and a trailing period are removed on set (an ellipsis is kept), which also
        /// covers messages that end with an exception's own text.
        /// </summary>
        public string Message
        {
            get => _Message;
            set => _Message = Normalize(value);
        }

        #endregion

        #region Private-Members

        private string _Message = string.Empty;
        private string _Summary = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborLogEntry()
        {
        }

        /// <summary>
        /// Instantiate with a direction and message.
        /// </summary>
        /// <param name="direction">Entry direction.</param>
        /// <param name="message">Entry message.</param>
        public HarborLogEntry(HarborLogDirection direction, string message)
        {
            Direction = direction;
            Message = message;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Render the entry as a single log line: "HH:mm:ss [DIR] message".
        /// </summary>
        /// <returns>The formatted line.</returns>
        public override string ToString()
        {
            string arrow = Direction == HarborLogDirection.In ? "<-" : Direction == HarborLogDirection.Out ? "->" : "  ";
            return TimestampUtc.ToLocalTime().ToString("HH:mm:ss") + " " + arrow + " " + Message;
        }

        #endregion

        #region Private-Methods

        private static string Normalize(string? message)
        {
            string text = (message ?? string.Empty).Trim();
            while (text.EndsWith(".", StringComparison.Ordinal) && !text.EndsWith("...", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 1).TrimEnd();
            }

            return text;
        }

        #endregion
    }
}
