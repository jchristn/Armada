namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A worker agent instance executing missions.
    /// </summary>
    public class Captain : IJsonOnDeserialized
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
        /// Captain name.
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
        /// Agent runtime type.
        /// </summary>
        public AgentRuntimeEnum Runtime { get; set; } = AgentRuntimeEnum.ClaudeCode;

        /// <summary>
        /// Whether this captain's runtime is currently supported by Armada planning sessions. Derived from
        /// <see cref="Armada.Core.Services.AgentRuntimeCapabilities"/>, the same source the runtime adapters report.
        /// </summary>
        public bool SupportsPlanningSessions => Armada.Core.Services.AgentRuntimeCapabilities.SupportsPlanningSessions(Runtime);

        /// <summary>
        /// Reason the captain cannot be used for planning sessions, if any.
        /// </summary>
        public string? PlanningSessionSupportReason => Armada.Core.Services.AgentRuntimeCapabilities.PlanningSessionUnsupportedReason(Runtime);

        /// <summary>
        /// Optional model override for the captain's runtime.
        /// Null means the runtime selects its default model.
        /// </summary>
        public string? Model
        {
            get => _Model;
            set
            {
                if (String.IsNullOrEmpty(value)) _Model = null;
                else _Model = value;
            }
        }

        /// <summary>
        /// Identifier of the inference ModelEndpoint this captain drives, when <see cref="Runtime"/> is
        /// <see cref="AgentRuntimeEnum.ApiEndpoint"/>. Null for CLI-harness runtimes. Must reference a
        /// configured Inference endpoint.
        /// </summary>
        public string? ModelEndpointId
        {
            get => _ModelEndpointId;
            set => _ModelEndpointId = String.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>
        /// Model profile (mdl_ prefix) of the model this captain runs as served, or null until Armada has observed it.
        /// Advisory and server-owned: Armada sets it when it records the captain's runtime and model (a stage outcome or a
        /// Quick probe run). It does not set <see cref="Tier"/> or change routing. Harbor-specific facts stay on the captain.
        /// </summary>
        public string? ModelProfileId
        {
            get => _ModelProfileId;
            set => _ModelProfileId = String.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>
        /// User-supplied system instructions for this captain. Injected into every mission's
        /// instructions before vessel context and mission details. Use this to specialize
        /// captain behavior, add guardrails, or provide persistent context.
        /// </summary>
        public string? SystemInstructions { get; set; } = null;

        /// <summary>
        /// JSON array of persona names this captain is allowed to fill.
        /// Null means the captain can take on any persona.
        /// Example: ["Worker", "Judge"]
        /// </summary>
        public string? AllowedPersonas { get; set; } = null;

        /// <summary>
        /// Preferred persona for dispatch routing priority.
        /// The Admiral prefers to assign work matching this persona to this captain.
        /// </summary>
        public string? PreferredPersona { get; set; } = null;

        /// <summary>
        /// Optional reasoning-effort level for this captain, translated to each runtime's native control
        /// at launch (Claude Code thinking budget, Codex model_reasoning_effort, Mux --effort). Runtimes
        /// without a native control ignore it. Null means "use the runtime default".
        /// </summary>
        public ReasoningEffortEnum? ReasoningEffort { get; set; } = null;

        /// <summary>
        /// Optional capability/cost tier used by dispatch to route missions of a given complexity. Null
        /// means the tier is auto-classified from the model name at selection time (defaulting to Standard); a
        /// Claude Code, Codex or Gemini captain with no model also takes Premium work, since it runs the vendor default.
        /// </summary>
        public CaptainTierEnum? Tier { get; set; } = null;

        /// <summary>
        /// Runtime-specific configuration serialized as JSON.
        /// Use this for settings that should not be promoted into generic captain fields.
        /// </summary>
        public string? RuntimeOptionsJson { get; set; } = null;

        /// <summary>
        /// CLI tool permission policy for this captain's own tools, or null to inherit (the legacy autoApprove runtime
        /// option, then the server default). Accepted on captain create (which requires an admin); captain updates keep the
        /// stored value: change it through PUT /api/v1/captains/{id}/cli-permission-policy (global admins, or tenant
        /// admins of the captain's tenant).
        /// </summary>
        public CliPermissionPolicyEnum? CliPermissionPolicy { get; set; } = null;

        /// <summary>
        /// Whether this captain participates in account usage capture. Default false: capture stays disabled
        /// until explicitly enabled, and old or minimally deserialized captains stay disabled. An explicit
        /// JSON null reads as false. Auto and Custom mode both additionally require this to be true; Disabled
        /// mode opts the captain out of limit capture and account membership regardless of this flag.
        /// </summary>
        [JsonConverter(typeof(UsageCaptureEnabledJsonConverter))]
        public bool UsageCaptureEnabled { get; set; } = false;

        /// <summary>
        /// Requested seconds between usage captures for this captain. Default 300, clamped to 60..86400 in
        /// the setter so model, storage and API agree on the effective value. Groups run at the shortest
        /// requested interval among enabled members, bounded by the global minimum interval. An explicit JSON
        /// null reads as 300.
        /// </summary>
        [JsonConverter(typeof(UsageCaptureIntervalJsonConverter))]
        public int UsageCaptureIntervalSeconds
        {
            get => _UsageCaptureIntervalSeconds;
            set => _UsageCaptureIntervalSeconds = value < 60 ? 60 : (value > 86400 ? 86400 : value);
        }

        /// <summary>
        /// How this captain captures usage: Auto (verified read-only sources only), Disabled (opt out
        /// entirely) or Custom (the configured local executable). Default Auto. Setting a mode other than
        /// Custom clears <see cref="UsageCaptureCommand"/>, so leaving Custom never keeps a stale command. An
        /// explicit JSON null reads as Auto.
        /// </summary>
        [JsonConverter(typeof(UsageCaptureModeJsonConverter))]
        public UsageCaptureModeEnum UsageCaptureMode
        {
            get => _UsageCaptureMode;
            set
            {
                _UsageCaptureMode = value;
                if (value != UsageCaptureModeEnum.Custom) _UsageCaptureCommand = null;
            }
        }

        /// <summary>
        /// The Custom usage-capture executable contract (JSON with executable and argument array), or null.
        /// Only meaningful in Custom mode: outside Custom it always reads as null whatever order the mode and
        /// command were assigned in (JSON property order included), so storage and API never see a command
        /// for an Auto or Disabled captain. Null or whitespace is normalized to null. Rejecting shell syntax
        /// and requiring a command in Custom mode is the service layer's contract, enforced before storage.
        /// </summary>
        public string? UsageCaptureCommand
        {
            get => _UsageCaptureMode == UsageCaptureModeEnum.Custom ? _UsageCaptureCommand : null;
            set => _UsageCaptureCommand = String.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// Whether usage capture is effectively active for this captain: enabled AND not opted out with
        /// Disabled mode. Derived; not stored.
        /// </summary>
        public bool UsageCaptureActive => UsageCaptureEnabled && UsageCaptureMode != UsageCaptureModeEnum.Disabled;

        /// <summary>
        /// Current state of the captain.
        /// </summary>
        public CaptainStateEnum State { get; set; } = CaptainStateEnum.Idle;

        /// <summary>Whether this captain may accept new work. Disabling leaves in-flight work running.</summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Currently assigned mission identifier.
        /// </summary>
        public string? CurrentMissionId { get; set; } = null;

        /// <summary>
        /// Currently assigned dock identifier.
        /// </summary>
        public string? CurrentDockId { get; set; } = null;

        /// <summary>
        /// Operating system process identifier.
        /// </summary>
        public int? ProcessId { get; set; } = null;

        /// <summary>
        /// Number of auto-recovery attempts for the current mission.
        /// </summary>
        public int RecoveryAttempts { get; set; } = 0;

        /// <summary>
        /// UTC time until which the captain is quarantined and excluded from dispatch selection. Null when
        /// the captain is not quarantined; a time in the past means the quarantine has expired and will be
        /// lifted on the next health tick.
        /// </summary>
        public DateTime? QuarantineUntilUtc { get; set; } = null;

        /// <summary>
        /// Why the captain was quarantined (e.g. "provider usage limit", "auth failure", "crash loop").
        /// Null when not quarantined.
        /// </summary>
        public string? QuarantineReason { get; set; } = null;

        /// <summary>
        /// Last heartbeat timestamp in UTC.
        /// </summary>
        public DateTime? LastHeartbeatUtc { get; set; } = null;

        /// <summary>
        /// UTC time the captain's OS process was last observed alive by the supervisor. This is
        /// distinct from <see cref="LastHeartbeatUtc"/>, which advances only on real agent output:
        /// stall detection compares output-heartbeat age so a process that is alive but silent is
        /// still detected as stalled, while liveness telemetry stays fresh here.
        /// </summary>
        public DateTime? LastProcessAliveUtc { get; set; } = null;

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

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.CaptainIdPrefix, 24);
        private string _Name = "Captain";
        private string? _Model = null;
        private string? _ModelEndpointId = null;
        private string? _ModelProfileId = null;
        private int _UsageCaptureIntervalSeconds = 300;
        private UsageCaptureModeEnum _UsageCaptureMode = UsageCaptureModeEnum.Auto;
        private string? _UsageCaptureCommand = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public Captain()
        {
        }

        /// <summary>
        /// Instantiate with name and runtime.
        /// </summary>
        /// <param name="name">Captain name.</param>
        /// <param name="runtime">Agent runtime type.</param>
        public Captain(string name, AgentRuntimeEnum runtime = AgentRuntimeEnum.ClaudeCode)
        {
            Name = name;
            Runtime = runtime;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalize usage capture settings after JSON deserialization: a command supplied for a non-Custom
        /// mode is dropped regardless of property order, so a later switch to Custom cannot revive it.
        /// </summary>
        public void OnDeserialized()
        {
            if (_UsageCaptureMode != UsageCaptureModeEnum.Custom) _UsageCaptureCommand = null;
        }

        #endregion
    }
}
