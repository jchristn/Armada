namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One invocation of a stub MCP tool registered by <see cref="AskTestHarness"/>: the arguments and the ambient caller
    /// claims the handler observed.
    /// </summary>
    public sealed class AskToolInvocation
    {
        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Raw JSON arguments.
        /// </summary>
        public string ArgumentsJson { get; set; } = "{}";

        /// <summary>
        /// Ambient caller claims (empty when there was no caller context).
        /// </summary>
        public Dictionary<string, string> Claims { get; set; } = new Dictionary<string, string>();
    }
}
