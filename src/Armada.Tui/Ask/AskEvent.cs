namespace Armada.Tui.Ask
{
    using System;
    using Armada.Client.Socket;
    using Armada.Core.Models;

    /// <summary>
    /// A normalized Ask Armada socket event (the dashboard's <c>AskEvent</c>): every event carries the thread it belongs
    /// to, so it can be routed to the open conversation or to the thread list. Only the fields of its
    /// <see cref="Type"/> are set.
    /// </summary>
    public class AskEvent
    {
        #region Public-Members

        /// <summary>
        /// Event type (<c>ask.chunk</c>, <c>ask.thinking</c>, <c>ask.tool</c>, <c>ask.turn</c>, <c>ask.message</c>,
        /// <c>ask.proposal</c>, <c>ask.work</c>, <c>ask.thread</c>).
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// Thread id.
        /// </summary>
        public string ThreadId { get; set; } = "";

        /// <summary>
        /// Turn id (chunk, thinking, tool, turn).
        /// </summary>
        public string TurnId { get; set; } = "";

        /// <summary>
        /// Text delta (chunk, thinking).
        /// </summary>
        public string Delta { get; set; } = "";

        /// <summary>
        /// Tool phase, or null.
        /// </summary>
        public ToolCallPhaseEnum? ToolPhase { get; set; } = null;

        /// <summary>
        /// Tool call id.
        /// </summary>
        public string? ToolId { get; set; } = null;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// Tool arguments text.
        /// </summary>
        public string? ToolArguments { get; set; } = null;

        /// <summary>
        /// Tool result text.
        /// </summary>
        public string? ToolResult { get; set; } = null;

        /// <summary>
        /// Tool success flag.
        /// </summary>
        public bool? ToolOk { get; set; } = null;

        /// <summary>
        /// Tool elapsed milliseconds.
        /// </summary>
        public double? ToolElapsedMs { get; set; } = null;

        /// <summary>
        /// True when the CLI refused the tool call for lack of permission (CLI tool permission policy), or null.
        /// </summary>
        public bool? ToolPermissionDenied { get; set; } = null;

        /// <summary>
        /// Turn state.
        /// </summary>
        public AskTurnStateEnum State { get; set; } = AskTurnStateEnum.Started;

        /// <summary>
        /// Persisted message id of a finished turn, or null.
        /// </summary>
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// Turn failure reason, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Message (ask.message).
        /// </summary>
        public AskMessage? Message { get; set; } = null;

        /// <summary>
        /// Proposal (ask.proposal).
        /// </summary>
        public AskActionProposal? Proposal { get; set; } = null;

        /// <summary>
        /// Tracked work id (ask.work).
        /// </summary>
        public string TrackedWorkId { get; set; } = "";

        /// <summary>
        /// Snapshot (ask.work), or null.
        /// </summary>
        public AskWorkSnapshot? Snapshot { get; set; } = null;

        /// <summary>
        /// Tracked work row (ask.work), or null.
        /// </summary>
        public AskTrackedWork? TrackedWork { get; set; } = null;

        /// <summary>
        /// Thread (ask.thread).
        /// </summary>
        public AskThread? Thread { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskEvent()
        {
        }

        #endregion
    }
}
