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
    /// Settings for vessel add command.
    /// </summary>
    public class VesselAddSettings : BaseSettings
    {
        /// <summary>
        /// Vessel name.
        /// </summary>
        [Description("Name of the vessel")]
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Repository URL.
        /// </summary>
        [Description("Remote repository URL")]
        [CommandArgument(1, "<repoUrl>")]
        public string RepoUrl { get; set; } = string.Empty;

        /// <summary>
        /// Optional fleet identifier or name.
        /// </summary>
        [Description("Fleet name or ID")]
        [CommandOption("--fleet|-f")]
        public string? Fleet { get; set; }

        /// <summary>
        /// Optional branch name.
        /// </summary>
        [Description("Default branch name")]
        [CommandOption("--branch|-b")]
        public string? Branch { get; set; }
    }
}
