namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for updating a vessel.
    /// </summary>
    public class VesselUpdateArgs
    {
        /// <summary>
        /// Vessel ID (vsl_ prefix).
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// New display name.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// New repository URL.
        /// </summary>
        public string? RepoUrl { get; set; }

        /// <summary>
        /// New default branch.
        /// </summary>
        public string? DefaultBranch { get; set; }

        /// <summary>
        /// New project context.
        /// </summary>
        public string? ProjectContext { get; set; }

        /// <summary>
        /// New style guide.
        /// </summary>
        public string? StyleGuide { get; set; }

        /// <summary>
        /// New local working directory where completed mission changes will be pulled after merge.
        /// </summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// Whether to allow multiple concurrent missions on this vessel.
        /// </summary>
        public bool? AllowConcurrentMissions { get; set; }

        /// <summary>
        /// Whether to enable model context accumulation on this vessel.
        /// </summary>
        public bool? EnableModelContext { get; set; }

        /// <summary>
        /// Agent-accumulated context about this repository.
        /// </summary>
        public string? ModelContext { get; set; }

        /// <summary>
        /// Default pipeline ID for dispatches to this vessel (ppl_ prefix).
        /// </summary>
        public string? DefaultPipelineId { get; set; }

        /// <summary>
        /// Optional per-vessel GitHub token override. Supply an empty string to clear it.
        /// </summary>
        public string? GitHubTokenOverride { get; set; }

        /// <summary>
        /// Whether the auto-land predicate gates unattended landing on this vessel.
        /// </summary>
        public bool? AutoLandEnabled { get; set; }

        /// <summary>
        /// Maximum number of changed files that may auto-land unattended (0 = no limit).
        /// </summary>
        public int? AutoLandMaxFiles { get; set; }

        /// <summary>
        /// Maximum number of changed lines that may auto-land unattended (0 = no limit).
        /// </summary>
        public int? AutoLandMaxLines { get; set; }

        /// <summary>
        /// Glob patterns a changed path must match to be auto-landable.
        /// </summary>
        public System.Collections.Generic.List<string>? AutoLandPathAllowGlobs { get; set; }

        /// <summary>
        /// Glob patterns that force a hold: a change touching any matching path never auto-lands.
        /// </summary>
        public System.Collections.Generic.List<string>? AutoLandPathDenyGlobs { get; set; }

        /// <summary>
        /// Whether the in-dock Definition-of-Done gate runs build + unit tests before acceptance.
        /// </summary>
        public bool? DefinitionOfDoneEnabled { get; set; }

        /// <summary>
        /// Shell command that builds the project inside the mission checkout (e.g. "dotnet build").
        /// </summary>
        public string? DefinitionOfDoneBuildCommand { get; set; }

        /// <summary>
        /// Shell command that runs unit tests inside the mission checkout (e.g. "dotnet test").
        /// </summary>
        public string? DefinitionOfDoneTestCommand { get; set; }

        /// <summary>
        /// Per-phase timeout, in seconds, for each Definition-of-Done command (clamped to [30, 7200]).
        /// </summary>
        public int? DefinitionOfDoneTimeoutSeconds { get; set; }
    }
}
