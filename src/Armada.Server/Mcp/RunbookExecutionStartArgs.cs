namespace Armada.Server.Mcp
{
    using Armada.Core.Models;

    /// <summary>
    /// Arguments for starting a runbook execution: the runbook identifier plus the execution start request fields.
    /// </summary>
    public class RunbookExecutionStartArgs : RunbookExecutionStartRequest
    {
        /// <summary>
        /// Runbook identifier (same as the playbook identifier).
        /// </summary>
        public string RunbookId { get; set; } = string.Empty;
    }
}
