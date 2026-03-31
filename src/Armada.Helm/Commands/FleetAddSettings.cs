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
    /// Settings for fleet add command.
    /// </summary>
    public class FleetAddSettings : BaseSettings
    {
        /// <summary>
        /// Fleet name.
        /// </summary>
        [Description("Name of the fleet to create")]
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Optional fleet description.
        /// </summary>
        [Description("Description of the fleet")]
        [CommandOption("--description|-d")]
        public string? Description { get; set; }
    }
}
