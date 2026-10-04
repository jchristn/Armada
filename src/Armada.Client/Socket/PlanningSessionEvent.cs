namespace Armada.Client.Socket
{
    using System;

    /// <summary>
    /// Payload of planning-session.* events. Fields not carried by a given event are null.
    /// </summary>
    public class PlanningSessionEvent
    {
        #region Public-Members

        /// <summary>
        /// Session id.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Session (changed).
        /// </summary>
        public Armada.Core.Models.PlanningSession? Session { get; set; } = null;

        /// <summary>
        /// Message (message.created, message.updated).
        /// </summary>
        public Armada.Core.Models.PlanningSessionMessage? Message { get; set; } = null;

        /// <summary>
        /// Message id (tool, thinking).
        /// </summary>
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// Thinking text (thinking).
        /// </summary>
        public string? Delta { get; set; } = null;

        /// <summary>
        /// Tool phase: started or completed (tool).
        /// </summary>
        public string? Phase { get; set; } = null;

        /// <summary>
        /// Tool call id (tool).
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Tool name (tool).
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Tool arguments (tool).
        /// </summary>
        public System.Text.Json.Nodes.JsonNode? Arguments { get; set; } = null;

        /// <summary>
        /// Tool outcome (tool).
        /// </summary>
        public bool? Ok { get; set; } = null;

        /// <summary>
        /// Tool duration (tool).
        /// </summary>
        public double? ElapsedMs { get; set; } = null;

        /// <summary>
        /// Tool result (tool).
        /// </summary>
        public System.Text.Json.Nodes.JsonNode? Result { get; set; } = null;

        /// <summary>
        /// Dispatched voyage id (dispatch.created).
        /// </summary>
        public string? VoyageId { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PlanningSessionEvent()
        {
        }

        #endregion
    }
}
