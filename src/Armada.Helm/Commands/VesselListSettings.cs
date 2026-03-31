namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Settings for vessel list command.
    /// </summary>
    public class VesselListSettings : BaseSettings
    {
        /// <summary>
        /// Optional fleet filter.
        /// </summary>
        [Description("Filter by fleet name or ID")]
        [CommandOption("--fleet|-f")]
        public string? Fleet { get; set; }
    }
}
