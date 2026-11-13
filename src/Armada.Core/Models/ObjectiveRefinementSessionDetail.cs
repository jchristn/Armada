namespace Armada.Core.Models
{
    /// <summary>
    /// Detail response for a refinement session. The transcript is not embedded: <see cref="MessageCount"/> says how
    /// many messages it has, and POST /api/v1/objective-refinement-sessions/{id}/messages/enumerate (MCP
    /// list_backlog_refinement_session_messages) reads it one page at a time.
    /// </summary>
    public class ObjectiveRefinementSessionDetail
    {
        /// <summary>
        /// Gets or sets the refinement session.
        /// </summary>
        public ObjectiveRefinementSession Session { get; set; } = new ObjectiveRefinementSession();
        /// <summary>
        /// Number of messages in the session transcript.
        /// </summary>
        public long MessageCount { get; set; } = 0;
        /// <summary>
        /// Gets or sets the captain.
        /// </summary>
        public Captain? Captain { get; set; } = null;
        /// <summary>
        /// Gets or sets the vessel.
        /// </summary>
        public Vessel? Vessel { get; set; } = null;
        /// <summary>
        /// Gets or sets the objective.
        /// </summary>
        public Objective? Objective { get; set; } = null;
    }
}
