namespace Armada.Client.Socket
{
    using System;

    /// <summary>
    /// Payload of ask.tool: a tool call started or completed during a turn.
    /// </summary>
    public class AskToolEvent
    {
        #region Public-Members

        /// <summary>
        /// Thread id.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// Turn id.
        /// </summary>
        public string? TurnId { get; set; } = null;

        /// <summary>
        /// started or completed.
        /// </summary>
        public string? Phase { get; set; } = null;

        /// <summary>
        /// Tool call id.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Arguments.
        /// </summary>
        public System.Text.Json.Nodes.JsonNode? Arguments { get; set; } = null;

        /// <summary>
        /// Outcome when completed.
        /// </summary>
        public bool? Ok { get; set; } = null;

        /// <summary>
        /// Duration when completed.
        /// </summary>
        public double? ElapsedMs { get; set; } = null;

        /// <summary>
        /// Result when completed.
        /// </summary>
        public System.Text.Json.Nodes.JsonNode? Result { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskToolEvent()
        {
        }

        #endregion
    }
}
