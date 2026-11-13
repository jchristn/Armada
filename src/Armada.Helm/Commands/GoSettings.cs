namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Text.RegularExpressions;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Settings for the go (quick dispatch) command.
    /// </summary>
    public class GoSettings : BaseSettings
    {
        /// <summary>
        /// Mission prompt.
        /// </summary>
        [Description("What to do")]
        [CommandArgument(0, "<prompt>")]
        public string Prompt { get; set; } = string.Empty;

        /// <summary>
        /// Target vessel name or ID.
        /// </summary>
        [Description("Target vessel (name, ID, or URL)")]
        [CommandOption("--vessel|-v <VESSEL>")]
        public string? Vessel { get; set; }

        /// <summary>
        /// Explicit tasks. Each --task becomes one mission in the voyage and the prompt becomes the voyage title.
        /// Without --task the prompt is a single mission; it is never split.
        /// </summary>
        [Description("Add a task as its own mission (repeatable); the prompt becomes the voyage title")]
        [CommandOption("--task|-t <TEXT>")]
        public string[]? Tasks { get; set; }

        /// <summary>
        /// Target repository path or URL.
        /// Infers from current directory if not specified.
        /// </summary>
        [Description("Repository path or URL (default: current directory)")]
        [CommandOption("--repo|-r <REPO>")]
        public string? Repo { get; set; }

        /// <summary>
        /// Path to write the captain's output log.
        /// </summary>
        [Description("Write captain output to a log file")]
        [CommandOption("--log|-l <PATH>")]
        public string? LogFile { get; set; }

        /// <summary>
        /// Landing mode for this voyage (LocalMerge, MergeAndPush, PullRequest, MergeQueue, None); unset inherits the
        /// vessel's landing mode, then the global default.
        /// </summary>
        [Description("Landing mode for this voyage: LocalMerge, MergeAndPush, PullRequest, MergeQueue, or None (default: the vessel's, then the global default)")]
        [CommandOption("--landing-mode <MODE>")]
        public string? LandingMode { get; set; }
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
