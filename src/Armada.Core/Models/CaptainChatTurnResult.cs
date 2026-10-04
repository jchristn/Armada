namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Outcome of a headless captain turn: the reply (or error) plus the tool calls observed while it ran.
    /// </summary>
    public class CaptainChatTurnResult
    {
        #region Public-Members

        /// <summary>
        /// Reply, reasoning, model, metrics, or error.
        /// </summary>
        public CaptainChatResponse Response
        {
            get => _Response;
            set => _Response = value ?? new CaptainChatResponse();
        }

        /// <summary>
        /// Tool calls observed during the turn, merged by call id (MessageId, ThreadId, and TenantId are left for the
        /// caller to fill in).
        /// </summary>
        public List<AskMessageToolCall> ToolCalls
        {
            get => _ToolCalls;
            set => _ToolCalls = value ?? new List<AskMessageToolCall>();
        }

        #endregion

        #region Private-Members

        private CaptainChatResponse _Response = new CaptainChatResponse();
        private List<AskMessageToolCall> _ToolCalls = new List<AskMessageToolCall>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CaptainChatTurnResult()
        {
        }

        #endregion
    }
}
