namespace Armada.Core.Models
{
    /// <summary>
    /// Detail response for a planning session: the session, its captain and vessel, and counts. The transcript is not
    /// embedded: <see cref="MessageCount"/> says how many messages it has, and POST
    /// /api/v1/planning-sessions/{id}/messages/enumerate (MCP list_backlog_planning_session_messages) reads it one page
    /// at a time.
    /// </summary>
    public class PlanningSessionDetail
    {
        /// <summary>
        /// The planning session.
        /// </summary>
        public PlanningSession Session { get; set; } = new PlanningSession();

        /// <summary>
        /// Number of messages in the session transcript.
        /// </summary>
        public long MessageCount { get; set; } = 0;

        /// <summary>
        /// The session's captain, or null when it is no longer visible.
        /// </summary>
        public Captain? Captain { get; set; } = null;

        /// <summary>
        /// The session's vessel, or null when it is no longer visible.
        /// </summary>
        public Vessel? Vessel { get; set; } = null;

        /// <summary>
        /// Number of objectives linked to the session that the caller can see. List them with the objectives or backlog
        /// enumeration filtered by PlanningSessionId.
        /// </summary>
        public long LinkedObjectiveCount { get; set; } = 0;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PlanningSessionDetail()
        {
        }
    }
}
