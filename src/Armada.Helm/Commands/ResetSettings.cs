namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;
    using Armada.Core.Settings;

    /// <summary>
    /// Settings for the reset command.
    /// </summary>
    public class ResetSettings : BaseSettings
    {
        /// <summary>
        /// Skip confirmation prompt.
        /// </summary>
        [Description("Skip confirmation prompt")]
        [CommandOption("--force|-f")]
        public bool Force { get; set; } = false;
    }
}
