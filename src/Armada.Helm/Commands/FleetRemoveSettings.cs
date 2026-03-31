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
    /// Settings for fleet remove command.
    /// </summary>
    public class FleetRemoveSettings : BaseSettings
    {
        /// <summary>
        /// Fleet name or ID.
        /// </summary>
        [Description("Fleet name or ID")]
        [CommandArgument(0, "<fleet>")]
        public string Id { get; set; } = string.Empty;
    }
}
