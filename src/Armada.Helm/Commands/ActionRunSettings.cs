namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for the action run command.
    /// </summary>
    public class ActionRunSettings : BaseSettings
    {
        /// <summary>
        /// Fleet action ID or name.
        /// </summary>
        [Description("Fleet action ID or name")]
        [CommandArgument(0, "<action>")]
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Target vessels (repeatable), by ID or name.
        /// </summary>
        [Description("Target vessel ID or name (repeatable)")]
        [CommandOption("--vessel|-v <VESSEL>")]
        public string[]? Vessels { get; set; }

        /// <summary>
        /// Target every vessel in a fleet.
        /// </summary>
        [Description("Target every vessel in this fleet (ID)")]
        [CommandOption("--fleet|-f <FLEET>")]
        public string? Fleet { get; set; }

        /// <summary>
        /// Runtime input values (repeatable), as name=value.
        /// </summary>
        [Description("Input value as name=value (repeatable), for example --input package=Newtonsoft.Json")]
        [CommandOption("--input|-i <NAME=VALUE>")]
        public string[]? Inputs { get; set; }

        /// <summary>
        /// Follow-up run: target the vessels of this earlier run instead of --vessel or --fleet.
        /// </summary>
        [Description("Follow up on an earlier run (far_ ID): target its vessels whose status matches --from-status")]
        [CommandOption("--from-run <RUN>")]
        public string? FromRun { get; set; }

        /// <summary>
        /// Target statuses selected from --from-run (repeatable).
        /// </summary>
        [Description("Target status to select from --from-run (repeatable; default Findings and Failed)")]
        [CommandOption("--from-status <STATUS>")]
        public string[]? FromStatuses { get; set; }

        /// <summary>
        /// Concurrency for this run.
        /// </summary>
        [Description("Concurrency for this run (1-32)")]
        [CommandOption("--concurrency|-c <N>")]
        public int? Concurrency { get; set; }
        /// <summary>Approve tool prompts and human mission reviews for this run.</summary>
        [Description("Approve all tool prompts and mission reviews for this run (admin only)")]
        [CommandOption("--approve-all")]
        public bool ApproveAll { get; set; }

        /// <summary>Per-run approval countdown.</summary>
        [Description("Approval countdown in seconds (10-3600)")]
        [CommandOption("--approval-timeout <SECONDS>")]
        public int? ApprovalTimeoutSeconds { get; set; }

        /// <summary>Decision applied when the countdown expires.</summary>
        [Description("Default answer on timeout: Deny or Approve (Approve requires admin)")]
        [CommandOption("--approval-default <ANSWER>")]
        public string? ApprovalDefault { get; set; }
    }
}
