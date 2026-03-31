namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for MCP remove command.
    /// </summary>
    public class McpRemoveSettings : BaseSettings
    {
        /// <summary>
        /// Only show what would be removed.
        /// </summary>
        [Description("Only display what would be removed, don't write files")]
        [CommandOption("--dry-run")]
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// Apply changes without interactive confirmation prompts.
        /// </summary>
        [Description("Apply changes without confirmation prompts")]
        [CommandOption("--yes")]
        public bool Yes { get; set; } = false;
    }
}
