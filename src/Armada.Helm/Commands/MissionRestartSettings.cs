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
    /// Settings for mission restart command.
    /// </summary>
    public class MissionRestartSettings : BaseSettings
    {
        /// <summary>
        /// Mission identifier or title.
        /// </summary>
        [Description("Mission ID or title")]
        [CommandArgument(0, "<mission>")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Optional new title for the restarted mission.
        /// </summary>
        [Description("New mission title")]
        [CommandOption("--title|-t")]
        public string? Title { get; set; }

        /// <summary>
        /// Optional new description for the restarted mission.
        /// </summary>
        [Description("New mission description/instructions")]
        [CommandOption("--description|-d")]
        public string? Description { get; set; }
    }
}
