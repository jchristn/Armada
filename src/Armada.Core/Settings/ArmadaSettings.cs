namespace Armada.Core.Settings
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Top-level application settings.
    /// </summary>
    public class ArmadaSettings
    {
        #region Public-Members

        /// <summary>
        /// Root data directory for Armada.
        /// </summary>
        public string DataDirectory
        {
            get => _DataDirectory;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(DataDirectory));
                _DataDirectory = value;
            }
        }

        /// <summary>
        /// Database file path.
        /// </summary>
        public string DatabasePath
        {
            get => _DatabasePath;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(DatabasePath));
                _DatabasePath = value;
                _DatabasePathConfigured = true;
            }
        }

        /// <summary>
        /// Log directory path.
        /// </summary>
        public string LogDirectory
        {
            get => _LogDirectory;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(LogDirectory));
                _LogDirectory = value;
            }
        }

        /// <summary>
        /// Directory for git worktree docks.
        /// </summary>
        public string DocksDirectory
        {
            get => _DocksDirectory;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(DocksDirectory));
                _DocksDirectory = value;
            }
        }

        /// <summary>
        /// Directory for bare repository clones.
        /// </summary>
        public string ReposDirectory
        {
            get => _ReposDirectory;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(ReposDirectory));
                _ReposDirectory = value;
            }
        }

        /// <summary>
        /// Admiral REST API port.
        /// </summary>
        public int AdmiralPort
        {
            get => _AdmiralPort;
            set
            {
                if (value < 1 || value > 65535) throw new ArgumentOutOfRangeException(nameof(AdmiralPort));
                _AdmiralPort = value;
            }
        }

        /// <summary>
        /// MCP server port.
        /// </summary>
        public int McpPort
        {
            get => _McpPort;
            set
            {
                if (value < 1 || value > 65535) throw new ArgumentOutOfRangeException(nameof(McpPort));
                _McpPort = value;
            }
        }

        /// <summary>
        /// Whether the WebSocket endpoint (/ws) is enabled on the Admiral port.
        /// When true, clients can connect to ws://host:port/ws for real-time events.
        /// </summary>
        public bool WebSocketEnabled { get; set; } = true;

        /// <summary>
        /// Whether captains are launched in an isolated agent configuration so they cannot inherit the
        /// host user's global agent settings or MCP servers. When enabled, each launch is given a scoped
        /// configuration that contains only the Armada MCP server (built from <see cref="McpPort"/>), via
        /// runtime-appropriate strict-config flags and/or a scoped HOME/config directory. Defaults to
        /// false so existing deployments are unaffected until explicitly opted in.
        /// </summary>
        public bool IsolateCaptainLaunch { get; set; } = false;

        /// <summary>
        /// Minimum available physical memory in bytes required before launching a captain. When available
        /// memory falls below this floor, dispatch defers the mission (leaves it pending for a later tick)
        /// rather than launching a captain likely to be OOM-killed mid-run. Zero (the default) disables the
        /// gate; the check also fails open if host memory cannot be measured. Clamped to non-negative.
        /// </summary>
        public long MinAvailableMemoryBytesForLaunch
        {
            get => _MinAvailableMemoryBytesForLaunch;
            set => _MinAvailableMemoryBytesForLaunch = value < 0 ? 0 : value;
        }

        /// <summary>
        /// How long, in minutes, a captain is quarantined after a provider usage-limit / auth failure or a
        /// crash loop before it is eligible for dispatch again. Clamped to a minimum of 1. Defaults to 15.
        /// </summary>
        public int CaptainQuarantineMinutes
        {
            get => _CaptainQuarantineMinutes;
            set => _CaptainQuarantineMinutes = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Number of non-clean captain crashes within <see cref="CaptainCrashLoopWindowMinutes"/> that trips
        /// crash-loop detection and quarantines the captain. Set to 0 to disable crash-loop quarantine.
        /// Clamped to [0, 20]. Defaults to 3.
        /// </summary>
        public int CaptainCrashLoopThreshold
        {
            get => _CaptainCrashLoopThreshold;
            set => _CaptainCrashLoopThreshold = value < 0 ? 0 : (value > 20 ? 20 : value);
        }

        /// <summary>
        /// The sliding window, in minutes, over which captain crashes are counted for crash-loop detection.
        /// Clamped to a minimum of 1. Defaults to 15.
        /// </summary>
        public int CaptainCrashLoopWindowMinutes
        {
            get => _CaptainCrashLoopWindowMinutes;
            set => _CaptainCrashLoopWindowMinutes = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Heartbeat check interval in seconds. Must be >= 5.
        /// </summary>
        public int HeartbeatIntervalSeconds
        {
            get => _HeartbeatIntervalSeconds;
            set
            {
                if (value < 5) throw new ArgumentOutOfRangeException(nameof(HeartbeatIntervalSeconds), "Must be >= 5");
                _HeartbeatIntervalSeconds = value;
            }
        }

        /// <summary>
        /// Stall detection threshold in minutes. Must be >= 1.
        /// </summary>
        public int StallThresholdMinutes
        {
            get => _StallThresholdMinutes;
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(StallThresholdMinutes), "Must be >= 1");
                _StallThresholdMinutes = value;
            }
        }

        /// <summary>
        /// Maximum number of automatic landing retries when landing fails due to target-branch drift.
        /// Set to 0 to disable auto-retry. Must be in range [0, 10].
        /// </summary>
        public int MaxLandingRetries
        {
            get => _MaxLandingRetries;
            set
            {
                if (value < 0) value = 0;
                if (value > 10) value = 10;
                _MaxLandingRetries = value;
            }
        }

        /// <summary>
        /// Maximum number of times a mission is automatically re-dispatched (to a fresh captain) after a
        /// detected no-op completion before it is failed and surfaced to the operator inbox. Set to 0 to
        /// fail immediately on the first no-op. Must be in range [0, 5].
        /// </summary>
        public int MaxNoOpRedispatchAttempts
        {
            get => _MaxNoOpRedispatchAttempts;
            set
            {
                if (value < 0) value = 0;
                if (value > 5) value = 5;
                _MaxNoOpRedispatchAttempts = value;
            }
        }

        /// <summary>
        /// Maximum number of bounded rescue missions the autonomous-recovery coordinator dispatches for a
        /// single incident before it stops and leaves the incident open for a human. Set to 0 to disable
        /// autonomous rescue (incidents are still opened for visibility). Clamped to [0, 5].
        /// </summary>
        public int MaxMissionRecoveryAttempts
        {
            get => _MaxMissionRecoveryAttempts;
            set
            {
                if (value < 0) value = 0;
                if (value > 5) value = 5;
                _MaxMissionRecoveryAttempts = value;
            }
        }

        /// <summary>
        /// Global landing mode for completed missions. Determines how work is integrated.
        /// When set, takes precedence over the legacy boolean flags (AutoPush, AutoCreatePullRequests, AutoMergePullRequests).
        /// Can be overridden per-vessel or per-voyage.
        /// Resolution order: voyage.LandingMode > vessel.LandingMode > settings.LandingMode > derive from booleans.
        /// </summary>
        public LandingModeEnum? LandingMode { get; set; } = null;

        /// <summary>
        /// Global branch cleanup policy after successful landing.
        /// Can be overridden per-vessel. Default: LocalOnly.
        /// </summary>
        public BranchCleanupPolicyEnum BranchCleanupPolicy { get; set; } = BranchCleanupPolicyEnum.LocalOnly;

        /// <summary>
        /// Whether to automatically push changes to the remote on mission completion.
        /// Legacy setting — prefer LandingMode when possible.
        /// </summary>
        public bool AutoPush { get; set; } = true;

        /// <summary>
        /// Whether to automatically create pull requests on mission completion.
        /// Legacy setting — prefer LandingMode when possible.
        /// Requires AutoPush to be effective.
        /// </summary>
        public bool AutoCreatePullRequests { get; set; } = false;

        /// <summary>
        /// Whether to automatically merge pull requests after creation.
        /// Legacy setting — prefer LandingMode when possible.
        /// Requires AutoCreatePullRequests to be effective.
        /// </summary>
        public bool AutoMergePullRequests { get; set; } = false;

        /// <summary>
        /// Minutes a mission may await human review before the watchdog escalates and releases the
        /// held captain (mission and dock are preserved for the reviewer). 0 disables. Must be >= 0.
        /// </summary>
        public int ReviewTimeoutMinutes
        {
            get => _ReviewTimeoutMinutes;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(ReviewTimeoutMinutes), "Must be >= 0");
                _ReviewTimeoutMinutes = value;
            }
        }

        /// <summary>
        /// Hard ceiling, in minutes, on how long a single mission may run before it is force-failed
        /// as a runaway (a backstop independent of stall detection). 0 disables the cap. Must be >= 0.
        /// </summary>
        public int MaxMissionRuntimeMinutes
        {
            get => _MaxMissionRuntimeMinutes;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(MaxMissionRuntimeMinutes), "Must be >= 0");
                _MaxMissionRuntimeMinutes = value;
            }
        }

        /// <summary>
        /// Maximum number of automatic recovery attempts for a stalled captain before its mission
        /// is failed.
        /// </summary>
        public int MaxRecoveryAttempts
        {
            get => _MaxRecoveryAttempts;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(MaxRecoveryAttempts), "Must be >= 0");
                _MaxRecoveryAttempts = value;
            }
        }

        /// <summary>
        /// Maximum log file size in bytes before rotation. Must be >= 1024.
        /// </summary>
        public long MaxLogFileSizeBytes
        {
            get => _MaxLogFileSizeBytes;
            set
            {
                if (value < 1024) throw new ArgumentOutOfRangeException(nameof(MaxLogFileSizeBytes), "Must be >= 1024");
                _MaxLogFileSizeBytes = value;
            }
        }

        /// <summary>
        /// Maximum number of rotated log files to keep per captain. Must be >= 1.
        /// </summary>
        public int MaxLogFileCount
        {
            get => _MaxLogFileCount;
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(MaxLogFileCount), "Must be >= 1");
                _MaxLogFileCount = value;
            }
        }

        /// <summary>
        /// Syslog targets for process-level logging.
        /// Defaults to a local syslog listener on 127.0.0.1:514.
        /// </summary>
        public List<SyslogServer> SyslogServers { get; set; } = new List<SyslogServer>
        {
            new SyslogServer("127.0.0.1", 514)
        };

        /// <summary>
        /// Data retention period in days for completed voyages, missions, signals, and events.
        /// Records older than this are purged by the background expiry task.
        /// Set to 0 to disable automatic expiry. Must be >= 0.
        /// </summary>
        public int DataRetentionDays
        {
            get => _DataRetentionDays;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(DataRetentionDays), "Must be >= 0");
                _DataRetentionDays = value;
            }
        }

        /// <summary>
        /// Whether HTTP request-history capture is enabled.
        /// </summary>
        public bool RequestHistoryEnabled { get; set; } = true;

        /// <summary>
        /// Retention period in days for stored request history.
        /// Set to 0 to disable automatic request-history expiry.
        /// </summary>
        public int RequestHistoryRetentionDays
        {
            get => _RequestHistoryRetentionDays;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(RequestHistoryRetentionDays), "Must be >= 0");
                _RequestHistoryRetentionDays = value;
            }
        }

        /// <summary>
        /// Maximum number of request or response body bytes persisted per entry.
        /// Set to 0 to disable body capture.
        /// </summary>
        public int RequestHistoryMaxBodyBytes
        {
            get => _RequestHistoryMaxBodyBytes;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(RequestHistoryMaxBodyBytes), "Must be >= 0");
                _RequestHistoryMaxBodyBytes = value;
            }
        }

        /// <summary>
        /// Route prefixes or exact paths excluded from request-history capture.
        /// </summary>
        public List<string> RequestHistoryExcludeRoutes { get; set; } = new List<string>
        {
            "/api/v1/status/health",
            "/api/v1/request-history"
        };

        /// <summary>
        /// Whether sanitized request headers should be stored with request history.
        /// </summary>
        public bool RequestHistoryCaptureRequestHeaders { get; set; } = true;

        /// <summary>
        /// Whether sanitized response headers should be stored with request history.
        /// </summary>
        public bool RequestHistoryCaptureResponseHeaders { get; set; } = true;

        /// <summary>
        /// Retention period in days for stopped or failed planning sessions.
        /// Set to 0 to disable automatic planning-session deletion.
        /// </summary>
        public int PlanningSessionRetentionDays
        {
            get => _PlanningSessionRetentionDays;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(PlanningSessionRetentionDays), "Must be >= 0");
                _PlanningSessionRetentionDays = value;
            }
        }

        /// <summary>
        /// Inactivity timeout in minutes for active planning sessions with no running process.
        /// Set to 0 to disable automatic inactivity stop.
        /// </summary>
        public int PlanningSessionInactivityTimeoutMinutes
        {
            get => _PlanningSessionInactivityTimeoutMinutes;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(PlanningSessionInactivityTimeoutMinutes), "Must be >= 0");
                _PlanningSessionInactivityTimeoutMinutes = value;
            }
        }

        /// <summary>
        /// Abandonment timeout in minutes for planning sessions with no running process.
        /// Stale planning sessions older than this are reclaimed even when the standard inactivity timeout is disabled.
        /// Set to 0 to disable abandonment cleanup.
        /// </summary>
        public int PlanningSessionAbandonmentTimeoutMinutes
        {
            get => _PlanningSessionAbandonmentTimeoutMinutes;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(PlanningSessionAbandonmentTimeoutMinutes), "Must be >= 0");
                _PlanningSessionAbandonmentTimeoutMinutes = value;
            }
        }

        /// <summary>
        /// Minimum number of idle captains to maintain.
        /// When idle count drops below this, new captains are spawned automatically.
        /// Set to 0 to disable auto-scaling. Must be >= 0.
        /// </summary>
        public int MinIdleCaptains
        {
            get => _MinIdleCaptains;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(MinIdleCaptains), "Must be >= 0");
                _MinIdleCaptains = value;
            }
        }

        /// <summary>
        /// Global ceiling on missions (working captains) that may run simultaneously, enforced at
        /// dispatch admission as backpressure against unbounded concurrency. 0 = unlimited. Must be >= 0.
        /// </summary>
        public int MaxConcurrentMissions
        {
            get => _MaxConcurrentMissions;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(MaxConcurrentMissions), "Must be >= 0");
                _MaxConcurrentMissions = value;
            }
        }

        /// <summary>
        /// Maximum total captains allowed. Set to 0 for unlimited. Must be >= 0.
        /// </summary>
        public int MaxCaptains
        {
            get => _MaxCaptains;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(MaxCaptains), "Must be >= 0");
                _MaxCaptains = value;
            }
        }

        /// <summary>
        /// Default test command for merge queue verification.
        /// Individual merge entries can override this.
        /// </summary>
        public string? MergeQueueTestCommand { get; set; } = null;

        /// <summary>
        /// Optional API key for Admiral REST API authentication.
        /// Deprecated: retained for backward compatibility, maps to synthetic admin identity.
        /// </summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>
        /// Optional global GitHub token used for Armada-managed GitHub integrations.
        /// A vessel-specific override takes precedence when present.
        /// </summary>
        public string? GitHubToken { get; set; } = null;

        /// <summary>
        /// Path to the external web dashboard directory (React build output).
        /// When set, the server serves static files from this directory at /dashboard.
        /// When null/empty, falls back to embedded wwwroot resources (legacy dashboard, not the React dashboard).
        /// </summary>
        public string? DashboardPath { get; set; } = null;

        /// <summary>
        /// Whether self-registration via POST /api/v1/onboarding is enabled.
        /// </summary>
        public bool AllowSelfRegistration { get; set; } = true;

        /// <summary>
        /// Whether POST /api/v1/server/stop requires authentication.
        /// When false (default), the shutdown endpoint is accessible without credentials,
        /// suitable for local development. When true, requires an authenticated identity,
        /// suitable for centralized or Docker deployments.
        /// </summary>
        public bool RequireAuthForShutdown { get; set; } = false;

        /// <summary>
        /// When true, a mission is only assigned when an eligible Harbor owned by the requesting user (the
        /// mission's user) is connected; the Admiral never falls back to running the captain in-process or on
        /// another user's Harbor. Missions wait (stay Pending and are retried) until that user's Harbor
        /// connects. When false (default), the Admiral delegates to a connected Harbor when one is available
        /// and otherwise runs the captain locally.
        /// </summary>
        public bool RequireHarborForLaunch { get; set; } = false;

        /// <summary>
        /// AES-256 encryption key for session tokens.
        /// Auto-generated if not provided.
        /// </summary>
        public string? SessionTokenEncryptionKey { get; set; } = null;

        /// <summary>
        /// Default agent runtime to use when auto-creating captains.
        /// Null means auto-detect from PATH.
        /// </summary>
        public string? DefaultRuntime { get; set; } = null;

        /// <summary>
        /// Identifier (vsl_ prefix) of the vessel that holds Armada's own source, used by the dashboard
        /// "Rebuild Armada" feature to know which repository to build from. Null disables self-rebuild until
        /// an operator designates the vessel. Preferred over a per-vessel flag so exactly one vessel can be
        /// Armada itself. See docs/SERVER_REBUILD.md.
        /// </summary>
        public string? SelfVesselId { get; set; } = null;

        /// <summary>
        /// Number of published rebuild "slots" to retain on disk for rollback. Clamped to a minimum of 1;
        /// defaults to 3. See docs/SERVER_REBUILD.md.
        /// </summary>
        public int RebuildSlotRetentionCount
        {
            get => _RebuildSlotRetentionCount;
            set => _RebuildSlotRetentionCount = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Identifier (hbr_ prefix) of an on-box Harbor that performs the health-gated cutover during a
        /// rebuild: it launches the new slot, polls health, and rolls back to the previous slot on failure.
        /// Null uses the built-in in-process baton (no automatic rollback). Only meaningful when the Harbor is
        /// co-located with the Admiral. See docs/SERVER_REBUILD.md.
        /// </summary>
        public string? RebuildSupervisorHarborId { get; set; } = null;

        /// <summary>
        /// Enable desktop notifications on mission completion/failure.
        /// </summary>
        public bool Notifications { get; set; } = true;

        /// <summary>
        /// Ring terminal bell on mission completion/failure during watch.
        /// </summary>
        public bool TerminalBell { get; set; } = true;

        /// <summary>
        /// Idle captain timeout in seconds before auto-removal.
        /// 0 = disabled (captains persist indefinitely).
        /// </summary>
        public int IdleCaptainTimeoutSeconds
        {
            get => _IdleCaptainTimeoutSeconds;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(IdleCaptainTimeoutSeconds), "Must be >= 0");
                _IdleCaptainTimeoutSeconds = value;
            }
        }

        /// <summary>
        /// Per-runtime agent configuration.
        /// </summary>
        public List<AgentSettings> Agents { get; set; } = new List<AgentSettings>();

        /// <summary>
        /// Escalation rules for automated notifications.
        /// </summary>
        public List<EscalationRule> EscalationRules { get; set; } = new List<EscalationRule>();

        /// <summary>
        /// Database connection settings.
        /// When set, takes precedence over DatabasePath for database initialization.
        /// </summary>
        public DatabaseSettings Database
        {
            get => _Database;
            set => _Database = value ?? new DatabaseSettings();
        }

        /// <summary>
        /// REST API listener settings.
        /// </summary>
        public RestSettings Rest { get; set; } = new RestSettings();

        /// <summary>
        /// Message template settings for commit messages and PR descriptions.
        /// </summary>
        public MessageTemplateSettings MessageTemplates { get; set; } = new MessageTemplateSettings();

        /// <summary>
        /// Remote-control tunnel settings.
        /// </summary>
        public RemoteControlSettings RemoteControl
        {
            get => _RemoteControl;
            set => _RemoteControl = value ?? new RemoteControlSettings();
        }

        /// <summary>
        /// Telemetry export settings (OpenTelemetry via Prometheus/Grafana/Loki). Disabled by default.
        /// </summary>
        public TelemetrySettings Telemetry
        {
            get => _Telemetry;
            set => _Telemetry = value ?? new TelemetrySettings();
        }

        /// <summary>
        /// How the Admiral executes host operations: Local (in-process, standalone) or Split (delegated to
        /// attached Harbor runners). Defaults to Local.
        /// </summary>
        public DeploymentModeEnum DeploymentMode { get; set; } = DeploymentModeEnum.Local;

        /// <summary>
        /// Server-side Harbor subsystem settings (link endpoint, auth, heartbeats, routing capacity).
        /// </summary>
        public HarborServerSettings Harbor
        {
            get => _Harbor;
            set => _Harbor = value ?? new HarborServerSettings();
        }

        #endregion

        #region Private-Members

        private string _DataDirectory = Constants.DefaultDataDirectory;

        private string _DatabasePath = Path.Combine(
            Constants.DefaultDataDirectory,
            Constants.DefaultDatabaseFilename);

        private string _LogDirectory = Path.Combine(
            Constants.DefaultDataDirectory,
            "logs");

        private string _DocksDirectory = Path.Combine(
            Constants.DefaultDataDirectory,
            "docks");

        private string _ReposDirectory = Path.Combine(
            Constants.DefaultDataDirectory,
            "repos");

        private int _AdmiralPort = Constants.DefaultAdmiralPort;
        private int _McpPort = Constants.DefaultMcpPort;
        private int _RebuildSlotRetentionCount = 3;
        private long _MinAvailableMemoryBytesForLaunch = Constants.DefaultMinAvailableMemoryBytesForLaunch;
        private int _CaptainQuarantineMinutes = 15;
        private int _CaptainCrashLoopThreshold = 3;
        private int _CaptainCrashLoopWindowMinutes = 15;
        private int _HeartbeatIntervalSeconds = Constants.DefaultHeartbeatIntervalSeconds;
        private int _StallThresholdMinutes = Constants.DefaultStallThresholdMinutes;
        private int _MaxRecoveryAttempts = Constants.DefaultMaxRecoveryAttempts;
        private int _MaxMissionRuntimeMinutes = Constants.DefaultMaxMissionRuntimeMinutes;
        private int _ReviewTimeoutMinutes = Constants.DefaultReviewTimeoutMinutes;
        private long _MaxLogFileSizeBytes = Constants.DefaultMaxLogFileSizeBytes;
        private int _MaxLogFileCount = Constants.DefaultMaxLogFileCount;
        private int _DataRetentionDays = Constants.DefaultDataRetentionDays;
        private int _RequestHistoryRetentionDays = Constants.DefaultRequestHistoryRetentionDays;
        private int _RequestHistoryMaxBodyBytes = Constants.DefaultRequestHistoryMaxBodyBytes;
        private int _PlanningSessionRetentionDays = 0;
        private int _PlanningSessionInactivityTimeoutMinutes = Constants.DefaultPlanningSessionInactivityTimeoutMinutes;
        private int _PlanningSessionAbandonmentTimeoutMinutes = Constants.DefaultPlanningSessionAbandonmentTimeoutMinutes;
        private int _MaxLandingRetries = 3;
        private int _MaxNoOpRedispatchAttempts = 1;
        private int _MaxMissionRecoveryAttempts = 2;
        private int _MinIdleCaptains = 0;
        private int _MaxCaptains = 0;
        private int _MaxConcurrentMissions = Constants.DefaultMaxConcurrentMissions;
        private int _IdleCaptainTimeoutSeconds = Constants.DefaultIdleCaptainTimeoutSeconds;
        private RemoteControlSettings _RemoteControl = new RemoteControlSettings();
        private TelemetrySettings _Telemetry = new TelemetrySettings();
        private HarborServerSettings _Harbor = new HarborServerSettings();
        private DatabaseSettings _Database = new DatabaseSettings();
        private bool _DatabasePathConfigured = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public ArmadaSettings()
        {
            if (Agents.Count == 0)
            {
                Agents.Add(new AgentSettings(
                    Enums.AgentRuntimeEnum.ClaudeCode,
                    "claude",
                    "--dangerously-skip-permissions --print")
                {
                    SupportsResume = true,
                    Environment = new Dictionary<string, string>
                    {
                        ["CLAUDE_CODE_DISABLE_NONINTERACTIVE_HINT"] = "1"
                    }
                });

                Agents.Add(new AgentSettings(
                    Enums.AgentRuntimeEnum.Codex,
                    "codex",
                    "--approval-mode full-auto"));

                Agents.Add(new AgentSettings(
                    Enums.AgentRuntimeEnum.Gemini,
                    "gemini",
                    "--sandbox none -p"));

                Agents.Add(new AgentSettings(
                    Enums.AgentRuntimeEnum.Cursor,
                    "cursor",
                    "--agent --prompt"));

                Agents.Add(new AgentSettings(
                    Enums.AgentRuntimeEnum.Mux,
                    "mux",
                    "print --yolo"));
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Ensure all required directories exist.
        /// </summary>
        public void InitializeDirectories()
        {
            NormalizePaths();
            Directory.CreateDirectory(DataDirectory);
            string? dbDirectory = Path.GetDirectoryName(DatabasePath);
            if (!String.IsNullOrEmpty(dbDirectory))
                Directory.CreateDirectory(dbDirectory);
            Directory.CreateDirectory(LogDirectory);
            Directory.CreateDirectory(DocksDirectory);
            Directory.CreateDirectory(ReposDirectory);
        }

        /// <summary>
        /// Save settings to a JSON file.
        /// </summary>
        /// <param name="path">File path. Defaults to ~/.armada/settings.json.</param>
        public async Task SaveAsync(string? path = null)
        {
            NormalizePaths();
            path ??= DefaultSettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(this, _SerializerOptions);
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
        }

        /// <summary>
        /// Load settings from a JSON file, or return defaults if file does not exist.
        /// </summary>
        /// <param name="path">File path. Defaults to ~/.armada/settings.json.</param>
        /// <returns>Loaded or default settings.</returns>
        public static async Task<ArmadaSettings> LoadAsync(string? path = null)
        {
            path ??= DefaultSettingsPath;
            if (!File.Exists(path))
            {
                ArmadaSettings defaults = new ArmadaSettings();
                defaults.NormalizePaths();
                return defaults;
            }
            string json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            ArmadaSettings? settings = JsonSerializer.Deserialize<ArmadaSettings>(json, _SerializerOptions);
            settings ??= new ArmadaSettings();
            settings.NormalizePaths();
            return settings;
        }

        /// <summary>
        /// Resolve the effective GitHub token for the supplied vessel.
        /// </summary>
        /// <param name="vessel">Optional vessel context.</param>
        /// <returns>Vessel override first, otherwise the global settings token.</returns>
        public string? ResolveGitHubToken(Vessel? vessel = null)
        {
            if (vessel != null && !String.IsNullOrWhiteSpace(vessel.GitHubTokenOverride))
            {
                return vessel.GitHubTokenOverride!.Trim();
            }

            if (!String.IsNullOrWhiteSpace(GitHubToken))
            {
                return GitHubToken!.Trim();
            }

            return null;
        }

        #endregion

        #region Private-Methods

        private void NormalizePaths()
        {
            _Database ??= new DatabaseSettings();

            if (_Database.Type == DatabaseTypeEnum.Sqlite)
            {
                string sqlitePath = ResolveEffectiveSqlitePath();
                _Database.Filename = sqlitePath;
                _DatabasePath = sqlitePath;
                _DatabasePathConfigured = true;
            }
        }

        private string ResolveEffectiveSqlitePath()
        {
            if (_Database.FilenameConfigured)
            {
                return ResolvePathRelativeToDataDirectory(_Database.Filename);
            }

            if (_DatabasePathConfigured)
            {
                return ResolvePathRelativeToDataDirectory(_DatabasePath);
            }

            return ResolvePathRelativeToDataDirectory(_Database.Filename);
        }

        private string ResolvePathRelativeToDataDirectory(string path)
        {
            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);

            return Path.GetFullPath(Path.Combine(DataDirectory, path));
        }

        #endregion

        #region Private-Static

        /// <summary>
        /// Default settings file path.
        /// </summary>
        public static readonly string DefaultSettingsPath = Path.Combine(
            Constants.DefaultDataDirectory,
            "settings.json");

        private static readonly JsonSerializerOptions _SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        #endregion
    }
}
