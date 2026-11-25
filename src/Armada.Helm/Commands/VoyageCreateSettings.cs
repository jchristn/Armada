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
        [CommandOption("--vessel|-v <VESSEL>")]
        public string? VesselId { get; set; }

        /// <summary>
        /// Mission descriptions (repeatable).
        /// </summary>
        [Description("Mission description (repeatable)")]
        [CommandOption("--mission|-m <TEXT>")]
        public string[]? Missions { get; set; }

        /// <summary>
        /// Playbook selections in the form id-or-file-name[:DeliveryMode].
        /// </summary>
        [Description("Playbook selection (repeatable): id-or-file-name[[:InlineFullContent|InstructionWithReference|AttachIntoWorktree]] (default InstructionWithReference)")]
        [CommandOption("--playbook|-p <PLAYBOOK>")]
        public string[]? Playbooks { get; set; }

        /// <summary>
        /// Persona for every mission. With no pipeline, each mission runs as one stage with this persona.
        /// </summary>
        [Description("Persona for every mission (for example Judge). With no pipeline each mission runs as one step with it; a multi-step pipeline picks its own personas")]
        [CommandOption("--persona <PERSONA>")]
        public string? Persona { get; set; }

        /// <summary>
        /// Mode for every mission.
        /// </summary>
        [Description("Mode for every mission: Implementation (default), Audit, or Research (read-only report modes)")]
        [CommandOption("--mode <MODE>")]
        public MissionModeEnum? Mode { get; set; }
    }
}
