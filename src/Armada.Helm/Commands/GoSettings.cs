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
        [CommandOption("--vessel|-v")]
        public string? Vessel { get; set; }

        /// <summary>
        /// Target repository path or URL.
        /// Infers from current directory if not specified.
        /// </summary>
        [Description("Repository path or URL (default: current directory)")]
        [CommandOption("--repo|-r")]
        public string? Repo { get; set; }

        /// <summary>
        /// Path to write the captain's output log.
        /// </summary>
        [Description("Write captain output to a log file")]
        [CommandOption("--log|-l")]
        public string? LogFile { get; set; }

        /// <summary>
        /// Override AutoPush: push changes to remote.
        /// </summary>
        [Description("Push changes to remote (overrides config)")]
        [CommandOption("--push")]
        public bool? Push { get; set; }

        /// <summary>
        /// Override AutoPush: do not push.
        /// </summary>
        [Description("Do not push changes to remote")]
        [CommandOption("--no-push")]
        public bool NoPush { get; set; } = false;

        /// <summary>
        /// Override AutoCreatePullRequests: create a PR.
        /// </summary>
        [Description("Create a pull request (overrides config)")]
        [CommandOption("--pr")]
        public bool? Pr { get; set; }

        /// <summary>
        /// Override AutoCreatePullRequests: do not create PR.
        /// </summary>
        [Description("Do not create a pull request")]
        [CommandOption("--no-pr")]
        public bool NoPr { get; set; } = false;

        /// <summary>
        /// Override AutoMergePullRequests: auto-merge the PR.
        /// </summary>
        [Description("Auto-merge the pull request (overrides config)")]
        [CommandOption("--merge")]
        public bool? Merge { get; set; }

        /// <summary>
        /// Override AutoMergePullRequests: do not auto-merge.
        /// </summary>
        [Description("Do not auto-merge the pull request")]
        [CommandOption("--no-merge")]
        public bool NoMerge { get; set; } = false;
    }
}
