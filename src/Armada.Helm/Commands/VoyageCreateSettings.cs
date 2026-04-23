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
    /// Settings for voyage create command.
    /// </summary>
    public class VoyageCreateSettings : BaseSettings
    {
        /// <summary>
        /// Voyage title.
        /// </summary>
        [Description("Voyage title")]
        [CommandArgument(0, "<title>")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Target vessel name or ID.
        /// </summary>
        [Description("Target vessel (name or ID)")]
        [CommandOption("--vessel|-v")]
        public string? VesselId { get; set; }

        /// <summary>
        /// Mission descriptions (repeatable).
        /// </summary>
        [Description("Mission description (repeatable)")]
        [CommandOption("--mission|-m")]
        public string[]? Missions { get; set; }

        /// <summary>
        /// Playbook selections in the form id-or-file-name[:DeliveryMode].
        /// </summary>
        [Description("Playbook selection (repeatable): id-or-file-name[:InlineFullContent|InstructionWithReference|AttachIntoWorktree]")]
        [CommandOption("--playbook|-p")]
        public string[]? Playbooks { get; set; }
    }
}
