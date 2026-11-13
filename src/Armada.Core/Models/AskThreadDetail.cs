namespace Armada.Core.Models
{
    /// <summary>
    /// A thread with counts of what it holds. The collections are read one page at a time: tracked work with POST
    /// /api/v1/ask/threads/{id}/work/enumerate, proposals with POST /api/v1/ask/threads/{id}/proposals/enumerate, CLI
    /// permission requests with POST /api/v1/cli-permissions/requests/enumerate (threadId), and the running turn's tool
    /// calls with POST /api/v1/ask/threads/{id}/turn/tool-calls/enumerate (MCP list_ask_work, list_ask_proposals,
    /// list_ask_tool_calls).
    /// </summary>
    public class AskThreadDetail
    {
        #region Public-Members

        /// <summary>
        /// The thread.
        /// </summary>
        public AskThread Thread
        {
            get => _Thread;
            set => _Thread = value ?? new AskThread();
        }

        /// <summary>
        /// Number of work items the thread tracks (every state). Thread.ActiveWorkCount counts the active ones.
        /// </summary>
        public long TrackedWorkCount { get; set; } = 0;

        /// <summary>
        /// Number of proposals still waiting for a decision.
        /// </summary>
        public long PendingProposalCount { get; set; } = 0;

        /// <summary>
        /// Number of pending CLI permission requests of the thread's captain.
        /// </summary>
        public long PendingCliPermissionCount { get; set; } = 0;

        /// <summary>
        /// What the captain turn running in the thread has produced so far (streamed text, thinking, and the number of
        /// tool calls), or null when no turn is running. Lets a client that opens the thread mid-turn restore the live
        /// reply; the tool calls themselves are paged.
        /// </summary>
        public AskTurnProgress? ActiveTurn { get; set; } = null;

        #endregion

        #region Private-Members

        private AskThread _Thread = new AskThread();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskThreadDetail()
        {
        }

        #endregion
    }
}
