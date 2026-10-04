namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Inputs of one headless captain turn driven by a server-side caller (Ask Armada threads, milestone narration):
    /// the captain, the fully built prompt, the thread-scoped MCP token, and live callbacks.
    /// </summary>
    public class CaptainChatTurnOptions
    {
        #region Public-Members

        /// <summary>
        /// Captain whose runtime runs the turn.
        /// </summary>
        public Captain Captain
        {
            get => _Captain;
            set => _Captain = value ?? throw new ArgumentNullException(nameof(Captain));
        }

        /// <summary>
        /// The complete prompt (system prompt, history, and the new message).
        /// </summary>
        public string Prompt
        {
            get => _Prompt;
            set => _Prompt = value ?? throw new ArgumentNullException(nameof(Prompt));
        }

        /// <summary>
        /// Ask the runtime to surface its reasoning. Default false.
        /// </summary>
        public bool ShowThinking { get; set; } = false;

        /// <summary>
        /// Tenant of the caller (token accounting).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User of the caller (token accounting).
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Session token that scopes the captain's Armada MCP connection (for example bound to an Ask thread), or null
        /// for no Armada MCP access. ApiEndpoint captains receive it through ARMADA_MCP_URL / ARMADA_MCP_TOKEN; Claude
        /// Code receives a per-launch strict MCP config carrying it as an X-Token header.
        /// </summary>
        public string? McpSessionToken { get; set; } = null;

        /// <summary>
        /// Maximum turn duration in milliseconds. Default 300000, minimum 5000, maximum 7200000.
        /// </summary>
        public int TimeoutMs
        {
            get => _TimeoutMs;
            set => _TimeoutMs = value < 5000 ? 5000 : (value > 7200000 ? 7200000 : value);
        }

        /// <summary>
        /// Called with each streamed reply delta, or null.
        /// </summary>
        public Action<string>? OnChunk { get; set; } = null;

        /// <summary>
        /// Called with each streamed reasoning delta, or null.
        /// </summary>
        public Action<string>? OnThinking { get; set; } = null;

        /// <summary>
        /// Called with each tool activity event, or null.
        /// </summary>
        public Action<CaptainToolActivity>? OnTool { get; set; } = null;

        #endregion

        #region Private-Members

        private Captain _Captain = new Captain();
        private string _Prompt = String.Empty;
        private int _TimeoutMs = 300000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CaptainChatTurnOptions()
        {
        }

        #endregion
    }
}
