namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of the review_blackbox and explain_blackbox MCP prompts. Read from either the prompt's argument map
    /// directly or a prompts/get params object that nests it under arguments.
    /// </summary>
    public class BlackBoxPromptArgs
    {
        /// <summary>
        /// Mission (msn_ prefix).
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Goal (review_blackbox): explain, fix, prevent, fix Armada, compare or clean up.
        /// </summary>
        public string? Goal { get; set; } = null;

        /// <summary>
        /// Nested argument map, when the params object carries one.
        /// </summary>
        public BlackBoxPromptArgs? Arguments { get; set; } = null;
    }
}
