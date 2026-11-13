namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Settings for mission list command.
    /// </summary>
    public class MissionListSettings : BaseSettings
    {
        /// <summary>
        /// Optional status filter.
        /// </summary>
        [Description("Filter by mission status")]
        [CommandOption("--status|-s <STATUS>")]
        public string? Status { get; set; }

        /// <summary>
        /// Optional vessel filter.
        /// </summary>
        [Description("Filter by vessel name or ID")]
        [CommandOption("--vessel|-v <VESSEL>")]
        public string? Vessel { get; set; }

        /// <summary>
        /// Optional captain filter.
        /// </summary>
        [Description("Filter by captain name or ID")]
        [CommandOption("--captain|-c <CAPTAIN>")]
        public string? Captain { get; set; }

        /// <summary>
        /// Optional voyage filter.
        /// </summary>
        [Description("Filter by voyage ID or title")]
        [CommandOption("--voyage <VOYAGE>")]
        public string? Voyage { get; set; }
    }
}
