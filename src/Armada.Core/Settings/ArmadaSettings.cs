namespace Armada.Core.Settings
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

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
        /// WebSocket server port for real-time event streaming.
        /// </summary>
        public int WebSocketPort
        {
            get => _WebSocketPort;
            set
            {
                if (value < 1 || value > 65535) throw new ArgumentOutOfRangeException(nameof(WebSocketPort));
                _WebSocketPort = value;
            }
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
        /// Maximum number of auto-recovery attempts for stalled captains. Must be >= 0.
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
        /// Path to the external web dashboard directory (React build output).
        /// When set, the server serves static files from this directory at /dashboard.
        /// When null/empty, falls back to embedded wwwroot resources (legacy dashboard).
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
        public DatabaseSettings Database { get; set; } = new DatabaseSettings();

        /// <summary>
        /// REST API listener settings.
        /// </summary>
        public RestSettings Rest { get; set; } = new RestSettings();

        /// <summary>
        /// Message template settings for commit messages and PR descriptions.
        /// </summary>
        public MessageTemplateSettings MessageTemplates { get; set; } = new MessageTemplateSettings();

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
        private int _WebSocketPort = Constants.DefaultWebSocketPort;
        private int _HeartbeatIntervalSeconds = Constants.DefaultHeartbeatIntervalSeconds;
        private int _StallThresholdMinutes = Constants.DefaultStallThresholdMinutes;
        private int _MaxRecoveryAttempts = Constants.DefaultMaxRecoveryAttempts;
        private long _MaxLogFileSizeBytes = Constants.DefaultMaxLogFileSizeBytes;
        private int _MaxLogFileCount = Constants.DefaultMaxLogFileCount;
        private int _DataRetentionDays = Constants.DefaultDataRetentionDays;
        private int _MaxLandingRetries = 3;
        private int _MinIdleCaptains = 0;
        private int _MaxCaptains = 0;
        private int _IdleCaptainTimeoutSeconds = Constants.DefaultIdleCaptainTimeoutSeconds;

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
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Ensure all required directories exist.
        /// </summary>
        public void InitializeDirectories()
        {
            Directory.CreateDirectory(DataDirectory);
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
            if (!File.Exists(path)) return new ArmadaSettings();
            string json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            ArmadaSettings? settings = JsonSerializer.Deserialize<ArmadaSettings>(json, _SerializerOptions);
            return settings ?? new ArmadaSettings();
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
