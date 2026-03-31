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
    /// Settings for vessel remove command.
    /// </summary>
    public class VesselRemoveSettings : BaseSettings
    {
        /// <summary>
        /// Vessel name or ID.
        /// </summary>
        [Description("Vessel name or ID")]
        [CommandArgument(0, "<vessel>")]
        public string Id { get; set; } = string.Empty;
    }
}
