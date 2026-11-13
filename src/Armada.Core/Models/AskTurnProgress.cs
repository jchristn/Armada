namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a captain turn that is still running in an Ask Armada thread has produced so far: the streamed reply text,
    /// thinking, and tool calls. The reply and its tool calls are persisted only when the turn ends, so a client that
    /// opens the thread mid-turn (after navigating away and back, or a page reload) restores the live reply from this.
    /// </summary>
    public class AskTurnProgress
    {
        #region Public-Members

        /// <summary>
        /// Turn identifier (the turnId of the turn's ask.* events).
        /// </summary>
        public string TurnId
        {
            get => _TurnId;
            set => _TurnId = value ?? String.Empty;
        }

        /// <summary>
        /// The reply message reserved for this turn (amg_ prefix), or null before it is reserved.
        /// </summary>
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// Reply text streamed so far.
        /// </summary>
        public string Text
        {
            get => _Text;
            set => _Text = value ?? String.Empty;
        }

        /// <summary>
        /// Thinking streamed so far (only when the turn shows thinking).
        /// </summary>
        public string ThinkingText
        {
            get => _ThinkingText;
            set => _ThinkingText = value ?? String.Empty;
        }

        /// <summary>
        /// Number of tool calls seen so far. The calls are read one page at a time with POST
        /// /api/v1/ask/threads/{id}/turn/tool-calls/enumerate (MCP list_ask_tool_calls without messageId).
        /// </summary>
        public int ToolCallCount { get; set; } = 0;

        /// <summary>
        /// Tool calls seen so far, in first-seen order (server-side live state, never serialized; read it through the
        /// paged enumeration). A call still running has a null Ok and no result.
        /// </summary>
        [JsonIgnore]
        public List<AskMessageToolCall> ToolCalls
        {
            get => _ToolCalls;
            set => _ToolCalls = value ?? new List<AskMessageToolCall>();
        }

        /// <summary>
        /// UTC time the turn started.
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _TurnId = String.Empty;
        private string _Text = String.Empty;
        private string _ThinkingText = String.Empty;
        private List<AskMessageToolCall> _ToolCalls = new List<AskMessageToolCall>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskTurnProgress()
        {
        }

        #endregion
    }
}
