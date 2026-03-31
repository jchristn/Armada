namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Response from the mission diff API endpoint.
    /// </summary>
    public class MissionDiffResponse
    {
        /// <summary>Mission identifier.</summary>
        public string MissionId { get; set; } = "";

        /// <summary>Branch name.</summary>
        public string Branch { get; set; } = "";

        /// <summary>Worktree path.</summary>
        public string WorktreePath { get; set; } = "";

        /// <summary>Unified diff output.</summary>
        public string Diff { get; set; } = "";
    }
}
