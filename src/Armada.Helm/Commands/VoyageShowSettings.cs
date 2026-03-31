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
    /// Settings for voyage show command.
    /// </summary>
    public class VoyageShowSettings : BaseSettings
    {
        /// <summary>
        /// Voyage identifier or title.
        /// </summary>
        [Description("Voyage ID or title")]
        [CommandArgument(0, "<voyage>")]
        public string Id { get; set; } = string.Empty;
    }
}
