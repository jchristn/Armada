namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// A git repository registered with Armada.
    /// </summary>
    public class Vessel
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier.
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owning user identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Fleet identifier this vessel belongs to.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Vessel name.
        /// </summary>
        public string Name
        {
            get => _Name;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// Remote repository URL.
        /// </summary>
        public string? RepoUrl
        {
            get => _RepoUrl;
            set => _RepoUrl = value;
        }

        /// <summary>
        /// Local path to the bare repository clone.
        /// </summary>
        public string? LocalPath { get; set; } = null;

        /// <summary>
        /// Local working directory (the user's checkout) for local merge on completion.
        /// </summary>
        public string? WorkingDirectory { get; set; } = null;

        /// <summary>
        /// Optional per-vessel GitHub token override.
        /// This value is accepted on create and update, but is never serialized in read responses.
        /// </summary>
        [JsonIgnore]
        public string? GitHubTokenOverride
        {
            get => _GitHubTokenOverride;
            set
            {
                _GitHubTokenOverride = value;
                _HasGitHubTokenOverride = null;
            }
        }

        /// <summary>
        /// Indicates whether this vessel has a GitHub token override configured.
        /// </summary>
        [JsonInclude]
        public bool HasGitHubTokenOverride
        {
            get => _HasGitHubTokenOverride ?? !String.IsNullOrWhiteSpace(GitHubTokenOverride);
            private set => _HasGitHubTokenOverride = value;
        }

        /// <summary>
        /// Write-only JSON input for the GitHub token override.
        /// This allows create and update requests to supply the token without Armada ever returning it.
        /// </summary>
        [JsonPropertyName("gitHubTokenOverride")]
        public string? GitHubTokenOverrideInput
        {
            set
            {
                GitHubTokenOverrideSpecified = true;
                GitHubTokenOverride = value;
            }
        }

        /// <summary>
        /// Whether the GitHub token override was explicitly supplied during JSON deserialization.
        /// </summary>
        [JsonIgnore]
        public bool GitHubTokenOverrideSpecified { get; private set; } = false;

        /// <summary>
        /// Default branch name.
        /// </summary>
        public string DefaultBranch { get; set; } = "main";

        /// <summary>
        /// Project context describing what the project is, its architecture, key files, and dependencies.
        /// </summary>
        public string? ProjectContext { get; set; } = null;

        /// <summary>
        /// Style guide describing naming conventions, patterns, language restrictions, and library preferences.
        /// </summary>
        public string? StyleGuide { get; set; } = null;

        /// <summary>
        /// Whether model context accumulation is enabled for this vessel.
        /// When true, captains are instructed to update the model context with
        /// key information discovered during missions.
        /// </summary>
        public bool EnableModelContext { get; set; } = true;

        /// <summary>
        /// Agent-accumulated context about this repository. Contains key information
        /// discovered by AI agents during missions, such as architectural insights,
        /// testing patterns, build quirks, and other knowledge useful for future missions.
        /// Updated by agents via update_vessel_context when EnableModelContext is true.
        /// </summary>
        public string? ModelContext { get; set; } = null;

        /// <summary>
        /// Landing mode for this vessel. Determines how completed mission work is integrated.
        /// Null means use the voyage or global setting. When set, this takes precedence over
        /// the legacy boolean flags (AutoPush, AutoCreatePullRequests, AutoMergePullRequests).
        /// </summary>
        public LandingModeEnum? LandingMode { get; set; } = null;

        /// <summary>
        /// Branch cleanup policy for this vessel. Determines when and how mission branches
        /// are deleted after successful landing. Null means use the global setting (default: LocalOnly).
        /// </summary>
        public BranchCleanupPolicyEnum? BranchCleanupPolicy { get; set; } = null;

        /// <summary>
        /// Whether this vessel allows multiple concurrent missions. Default false.
        /// When false, only one mission may be in an active state
        /// (Assigned, InProgress, WorkProduced, PullRequestOpen) at a time.
        /// </summary>
        public bool AllowConcurrentMissions { get; set; } = false;

        /// <summary>
        /// Whether successful landing requires at least one passing structured check
        /// for the current branch or mission context.
        /// </summary>
        public bool RequirePassingChecksToLand { get; set; } = false;

        /// <summary>
        /// Optional protected-branch glob or exact-match patterns.
        /// </summary>
        public List<string> ProtectedBranchPatterns { get; set; } = new List<string>();

        /// <summary>
        /// Whether the pre-land dock-boundary scanner runs built-in secret detection for this vessel.
        /// </summary>
        public bool SecretScanEnabled { get; set; } = false;

        /// <summary>
        /// Protected file-path globs the pre-land scanner blocks a mission from touching (e.g. ".github/**").
        /// </summary>
        public List<string> ProtectedPathPatterns { get; set; } = new List<string>();

        /// <summary>
        /// Private identifiers (company/domain strings) the pre-land scanner blocks from leaking into
        /// added diff lines for public repos.
        /// </summary>
        public List<string> PrivateIdentifierDenylist { get; set; } = new List<string>();

        /// <summary>
        /// Whether the auto-land predicate gates unattended landing on this vessel. When false, a passing
        /// mission lands per the usual review/landing-mode rules; when true, a mission must also satisfy the
        /// file/line/path rules below to land without review.
        /// </summary>
        public bool AutoLandEnabled { get; set; } = false;

        /// <summary>
        /// Maximum number of changed files that may auto-land unattended; 0 means no file-count limit.
        /// Clamped to non-negative.
        /// </summary>
        public int AutoLandMaxFiles
        {
            get => _AutoLandMaxFiles;
            set => _AutoLandMaxFiles = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Maximum number of changed lines (added + removed) that may auto-land unattended; 0 means no
        /// line-count limit. Clamped to non-negative.
        /// </summary>
        public int AutoLandMaxLines
        {
            get => _AutoLandMaxLines;
            set => _AutoLandMaxLines = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Glob patterns a changed path must match to be auto-landable. When non-empty, a mission touching
        /// any path outside this allow-list holds for review.
        /// </summary>
        public List<string> AutoLandPathAllowGlobs { get; set; } = new List<string>();

        /// <summary>
        /// Glob patterns that force a hold: a mission touching any matching path never auto-lands.
        /// </summary>
        public List<string> AutoLandPathDenyGlobs { get; set; } = new List<string>();

        /// <summary>
        /// Whether the in-dock Definition-of-Done gate runs before a mission is accepted. When true, the
        /// build and unit-test commands below run inside the mission's own checkout before landing; a
        /// failure blocks acceptance with a classified reason (Compile/TestFail/Timeout/Infra). When false,
        /// no gate runs and acceptance follows the usual rules.
        /// </summary>
        public bool DefinitionOfDoneEnabled { get; set; } = false;

        /// <summary>
        /// Shell command that builds the project inside the mission's checkout (e.g. "dotnet build").
        /// A non-zero exit classifies as Compile. Null or empty skips the build phase.
        /// </summary>
        public string? DefinitionOfDoneBuildCommand { get; set; } = null;

        /// <summary>
        /// Shell command that runs unit tests inside the mission's checkout (e.g. "dotnet test").
        /// A non-zero exit classifies as TestFail. Null or empty skips the test phase.
        /// </summary>
        public string? DefinitionOfDoneTestCommand { get; set; } = null;

        /// <summary>
        /// Per-phase timeout, in seconds, for each Definition-of-Done command. Exceeding it classifies as
        /// Timeout. Clamped to [30, 7200]; defaults to <see cref="Constants.DefaultDefinitionOfDoneTimeoutSeconds"/>.
        /// </summary>
        public int DefinitionOfDoneTimeoutSeconds
        {
            get => _DefinitionOfDoneTimeoutSeconds;
            set => _DefinitionOfDoneTimeoutSeconds = value < 30 ? 30 : (value > 7200 ? 7200 : value);
        }

        /// <summary>
        /// Prefix used to classify release branches.
        /// </summary>
        public string ReleaseBranchPrefix { get; set; } = "release/";

        /// <summary>
        /// Prefix used to classify hotfix branches.
        /// </summary>
        public string HotfixBranchPrefix { get; set; } = "hotfix/";

        /// <summary>
        /// Whether protected branches must land via PR-oriented flow.
        /// </summary>
        public bool RequirePullRequestForProtectedBranches { get; set; } = false;

        /// <summary>
        /// Whether release branches must land via merge queue.
        /// </summary>
        public bool RequireMergeQueueForReleaseBranches { get; set; } = false;

        /// <summary>
        /// Default pipeline to use for dispatches to this vessel.
        /// Vessel setting overrides fleet setting. Null uses WorkerOnly pipeline.
        /// </summary>
        public string? DefaultPipelineId { get; set; } = null;

        /// <summary>
        /// Whether the vessel is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselIdPrefix, 24);
        private string _Name = "My Vessel";
        private int _AutoLandMaxFiles = 0;
        private int _AutoLandMaxLines = 0;
        private int _DefinitionOfDoneTimeoutSeconds = Constants.DefaultDefinitionOfDoneTimeoutSeconds;
        private string? _RepoUrl = null;
        private string? _GitHubTokenOverride = null;
        private bool? _HasGitHubTokenOverride = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public Vessel()
        {
        }

        /// <summary>
        /// Instantiate with name and repository URL.
        /// </summary>
        /// <param name="name">Vessel name.</param>
        /// <param name="repoUrl">Remote repository URL.</param>
        public Vessel(string name, string repoUrl)
        {
            Name = name;
            RepoUrl = repoUrl;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalizes the GitHub token override and clears it when blank.
        /// </summary>
        public void NormalizeGitHubTokenOverride()
        {
            if (String.IsNullOrWhiteSpace(GitHubTokenOverride))
            {
                GitHubTokenOverride = null;
                return;
            }

            GitHubTokenOverride = GitHubTokenOverride.Trim();
        }

        #endregion
    }
}
