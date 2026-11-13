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
    /// Settings for mission create command.
    /// </summary>
    public class MissionCreateSettings : BaseSettings
    {
        /// <summary>
        /// Mission title.
        /// </summary>
        [Description("Mission title")]
        [CommandArgument(0, "<title>")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Target vessel identifier or name.
        /// </summary>
        [Description("Target vessel (name or ID)")]
        [CommandOption("--vessel|-v <VESSEL>")]
        public string? Vessel { get; set; }

        /// <summary>
        /// Optional voyage identifier to attach to.
        /// </summary>
        [Description("Voyage ID or title")]
        [CommandOption("--voyage <VOYAGE>")]
        public string? Voyage { get; set; }

        /// <summary>
        /// Optional mission description.
        /// </summary>
        [Description("Mission description")]
        [CommandOption("--description|-d <TEXT>")]
        public string? Description { get; set; }

        /// <summary>
        /// Optional priority (lower is higher).
        /// </summary>
        [Description("Priority (lower is higher)")]
        [CommandOption("--priority|-p <N>")]
        public int? Priority { get; set; }

        /// <summary>
        /// Optional Worker envelope: most files one Worker mission should change.
        /// </summary>
        [Description("Worker envelope: most files the Worker should change (1-1000; may only tighten the vessel default)")]
        [CommandOption("--max-files <N>")]
        public int? MaxFiles { get; set; }

        /// <summary>
        /// Optional Worker envelope: most steps one Worker mission should take.
        /// </summary>
        [Description("Worker envelope: most steps the Worker should take (1-1000; may only tighten the vessel default)")]
        [CommandOption("--max-steps <N>")]
        public int? MaxSteps { get; set; }

        /// <summary>
        /// Optional Worker envelope: smallest served context a Worker captain's model profile must record.
        /// </summary>
        [Description("Worker envelope: smallest served context in tokens the Worker captain's model profile must record")]
        [CommandOption("--min-context-tokens <TOKENS>")]
        public int? MinServedContextTokens { get; set; }

        /// <summary>
        /// Optional Worker envelope: capabilities the Worker captain's model profile must record (tools, mcp).
        /// </summary>
        [Description("Worker envelope: required capability (tools or mcp); repeat for more")]
        [CommandOption("--require-tool <CAPABILITY>")]
        public string[]? RequiredTools { get; set; }

        /// <summary>
        /// Optional parent logical mission ID (msn_ prefix); the new mission starts after the parent's last stage finishes.
        /// </summary>
        [Description("Parent mission ID (msn_): start after the parent finishes; a failed parent cancels this mission")]
        [CommandOption("--parent <MISSION>")]
        public string? Parent { get; set; }

        /// <summary>
        /// Optional branch relationship to the parent: New, ContinueParent or ForkFromParent.
        /// </summary>
        [Description("Branch relationship to the parent: New (default), ContinueParent or ForkFromParent")]
        [CommandOption("--branch-strategy <STRATEGY>")]
        public string? BranchStrategy { get; set; }
    }
}
