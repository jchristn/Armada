namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Settings for log command.
    /// </summary>
    public class LogSettings : BaseSettings
    {
        /// <summary>
        /// Captain or mission identifier.
        /// </summary>
        [Description("Captain name/ID, mission ID, or mission title")]
        [CommandArgument(0, "<identifier>")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Number of lines to show from end of log.
        /// </summary>
        [Description("Number of lines to show (default: 50)")]
        [CommandOption("--lines|-n")]
        public int? Lines { get; set; }

        /// <summary>
        /// Follow the log file for live output.
        /// </summary>
        [Description("Follow log for live output")]
        [CommandOption("--follow|-f")]
        public bool Follow { get; set; } = false;
    }
}
