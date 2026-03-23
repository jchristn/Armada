namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
     /// Settings for MCP install command.
    /// </summary>
    public class McpInstallSettings : BaseSettings
    {
        /// <summary>
        /// Only show the configuration, don't write it.
        /// </summary>
        [Description("Only display the configuration, don't write it")]
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
