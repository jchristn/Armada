namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Settings for status command.
    /// </summary>
    public class StatusSettings : BaseSettings
    {
        /// <summary>
        /// Show status for all vessels, not just the current directory.
        /// </summary>
        [Description("Show status for all vessels")]
        [CommandOption("--all|-a")]
        public bool All { get; set; } = false;
    }
}
