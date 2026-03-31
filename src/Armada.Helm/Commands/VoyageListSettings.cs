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
    /// Settings for voyage list command.
    /// </summary>
    public class VoyageListSettings : BaseSettings
    {
        /// <summary>
        /// Optional status filter.
        /// </summary>
        [Description("Filter by voyage status")]
        [CommandOption("--status|-s")]
        public string? Status { get; set; }
    }
}
