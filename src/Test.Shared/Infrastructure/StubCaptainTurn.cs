namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Mcp;

    /// <summary>
    /// One thread-scoped turn (Ask Armada, narration) handed to a <see cref="StubCaptainBehavior"/> script by
    /// <see cref="StubCaptainRuntime"/>: the prompt plus a way to call the Admiral's MCP tools as the thread, exactly as
    /// a real captain CLI would through its per-launch MCP configuration.
    /// </summary>
    public sealed class StubCaptainTurn
    {
        #region Public-Members

        /// <summary>
        /// Prompt the server built for the turn.
        /// </summary>
        public string Prompt { get; }

        /// <summary>
        /// Working directory of the turn.
        /// </summary>
        public string WorkingDirectory { get; }

        /// <summary>
        /// MCP endpoint URL (http://127.0.0.1:port/mcp).
        /// </summary>
        public string McpUrl { get; }

        /// <summary>
        /// Thread-scoped session token the server minted for the turn.
        /// </summary>
        public string SessionToken { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="prompt">Prompt.</param>
        /// <param name="workingDirectory">Working directory.</param>
        /// <param name="mcpUrl">MCP endpoint URL.</param>
        /// <param name="sessionToken">Session token.</param>
        public StubCaptainTurn(string prompt, string workingDirectory, string mcpUrl, string sessionToken)
        {
            Prompt = prompt ?? "";
            WorkingDirectory = workingDirectory ?? "";
            McpUrl = mcpUrl ?? throw new ArgumentNullException(nameof(mcpUrl));
            SessionToken = sessionToken ?? "";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Call an MCP tool as the thread (gated tools become proposals awaiting the user's approval).
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <param name="argumentsJson">Arguments as JSON.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The tool result text.</returns>
        public async Task<string> CallToolAsync(string name, string argumentsJson, CancellationToken token = default)
        {
            using (McpToolClient client = new McpToolClient(McpUrl, SessionToken, null, null, 30))
            {
                await client.InitializeAsync(token).ConfigureAwait(false);
                return await client.CallToolAsync(name, argumentsJson, token).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
