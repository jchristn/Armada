namespace Armada.Server
{
    /// <summary>
    /// The fields of an MCP tool result that say whether the operation itself succeeded (some tools, such as
    /// retry_landing, return a result object with Success false instead of an error). Read by
    /// <see cref="BlackBoxMcpActionExecutor"/>.
    /// </summary>
    public class BlackBoxToolResultProbe
    {
        /// <summary>
        /// Success flag, when the tool reports one.
        /// </summary>
        public bool? Success { get; set; } = null;

        /// <summary>
        /// Reason, when the tool reports one.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Error text, when the tool reports one.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxToolResultProbe()
        {
        }
    }
}
