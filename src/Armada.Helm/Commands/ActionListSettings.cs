namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for the action list command.
    /// </summary>
    public class ActionListSettings : BaseSettings
    {
        /// <summary>
        /// Include soft-deleted built-in actions.
        /// </summary>
        [Description("Include deleted built-in actions")]
        [CommandOption("--include-inactive")]
        public bool IncludeInactive { get; set; } = false;

        /// <summary>
        /// List recent runs instead of action definitions.
        /// </summary>
        [Description("List recent runs instead of actions")]
        [CommandOption("--runs")]
        public bool Runs { get; set; } = false;
    }
}
