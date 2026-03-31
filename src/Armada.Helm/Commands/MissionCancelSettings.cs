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
    /// Settings for mission cancel command.
    /// </summary>
    public class MissionCancelSettings : BaseSettings
    {
        /// <summary>
        /// Mission identifier or title.
        /// </summary>
        [Description("Mission ID or title")]
        [CommandArgument(0, "<mission>")]
        public string Id { get; set; } = string.Empty;
    }
}
