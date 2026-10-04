namespace Armada.Server.Ask
{
    /// <summary>
    /// Result of executing an MCP tool in-process for an Ask Armada action.
    /// </summary>
    public class AskExecutionOutcome
    {
        #region Public-Members

        /// <summary>
        /// Whether the tool succeeded (did not throw and returned no Error field).
        /// </summary>
        public bool Ok { get; set; } = false;

        /// <summary>
        /// The tool's result serialized as JSON (truncated), or null.
        /// </summary>
        public string? ResultText { get; set; } = null;

        /// <summary>
        /// Error text when the tool failed, or null.
        /// </summary>
        public string? ErrorText { get; set; } = null;

        /// <summary>
        /// The raw result object returned by the handler, or null.
        /// </summary>
        public object? Result { get; set; } = null;

        #endregion
    }
}
