namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Settings for watch command.
    /// </summary>
    public class WatchSettings : BaseSettings
    {
        /// <summary>
        /// Refresh interval in seconds.
        /// </summary>
        [Description("Refresh interval in seconds (default: 5)")]
        [CommandOption("--interval|-i")]
        public int? Interval { get; set; }

        /// <summary>
        /// Filter to a specific captain.
        /// </summary>
        [Description("Filter to a specific captain")]
        [CommandOption("--captain|-c")]
        public string? Captain { get; set; }
    }
}
