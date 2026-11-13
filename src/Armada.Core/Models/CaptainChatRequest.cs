namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Request body for a captain chat turn: the new user message plus the prior conversation so the
    /// model has context. The captain is identified by the route.
    /// </summary>
    public class CaptainChatRequest
    {
        #region Public-Members

        /// <summary>
        /// The new user message to send to the captain's model.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Prior conversation turns (oldest first), excluding the new message. Optional.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems)]
        public List<CaptainChatMessage> History { get; set; } = new List<CaptainChatMessage>();

        /// <summary>
        /// Optional client-generated turn identifier. When set, the server streams the reply as it is
        /// produced by broadcasting <c>ask.chunk</c> WebSocket events tagged with this id, so the
        /// dashboard can render tokens live. The HTTP response still returns the full reply and metrics.
        /// </summary>
        public string? TurnId { get; set; } = null;

        /// <summary>
        /// When true, ask the captain's runtime to surface the model's reasoning ("thinking") for this turn.
        /// Currently honored by the Mux runtime (launched with <c>--show-thinking</c>), which streams the
        /// reasoning as <c>assistant_thinking</c> events; runtimes without a thinking channel ignore it.
        /// </summary>
        public bool ShowThinking { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CaptainChatRequest()
        {
        }

        #endregion
    }
}
