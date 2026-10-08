namespace Armada.Core.Harbor
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// Server-to-Harbor request to launch a captain process. Carries the fully resolved launch plan the
    /// Admiral built (runtime, arguments, environment, working directory, prompt); the Harbor spawns the
    /// process and streams its lifecycle back keyed by <see cref="JobId"/>.
    /// </summary>
    public class HarborLaunchRequest : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Harbor-scoped job identifier assigned by the server for this launch. All later messages about
        /// the process reference it.
        /// </summary>
        public string JobId { get; set; } = string.Empty;

        /// <summary>
        /// Runtime: an <see cref="AgentRuntimeEnum"/> member name (for example "ClaudeCode", "Codex"). Kept as a
        /// string on the wire for compatibility; read it through <see cref="RuntimeType"/>.
        /// </summary>
        public string Runtime { get; set; } = string.Empty;

        /// <summary>
        /// <see cref="Runtime"/> as a defined <see cref="AgentRuntimeEnum"/> name (case-insensitive), or null when it
        /// is empty, numeric, or unknown. Not serialized.
        /// </summary>
        [JsonIgnore]
        public AgentRuntimeEnum? RuntimeType
        {
            get { return EnumNames.ParseOrNull<AgentRuntimeEnum>(Runtime, true); }
        }

        /// <summary>
        /// Absolute working directory (a dock worktree) on the Harbor host.
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// What the launch is for: a <see cref="HarborJobKindEnum"/> member name (for example "Mission", "AskTurn").
        /// Informational, for the Harbor's job list and logs. Kept as a string on the wire so a Harbor that does not
        /// know a newer value still accepts the launch; read it through <see cref="JobKindType"/>. Null from an
        /// Admiral that predates it.
        /// </summary>
        public string? JobKind { get; set; } = null;

        /// <summary>
        /// <see cref="JobKind"/> as a defined <see cref="HarborJobKindEnum"/> name (case-insensitive), or
        /// <see cref="HarborJobKindEnum.Unknown"/> when it is empty or unknown. Not serialized.
        /// </summary>
        [JsonIgnore]
        public HarborJobKindEnum JobKindType
        {
            get { return EnumNames.ParseOrNull<HarborJobKindEnum>(JobKind, true) ?? HarborJobKindEnum.Unknown; }
        }

        /// <summary>
        /// The mission this launch runs, when it is a mission; informational. Null otherwise.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// The captain this launch runs as; informational. Null from an Admiral that predates it.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Optional model identifier to pass to the runtime.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// The prompt to deliver to the captain.
        /// </summary>
        public string? Prompt { get; set; } = null;

        /// <summary>
        /// Whether the prompt is delivered on stdin (true) or as an argument (false).
        /// </summary>
        public bool PromptViaStdin { get; set; } = true;

        /// <summary>
        /// Command-line arguments for the runtime, in order.
        /// </summary>
        public List<string> Arguments { get; set; } = new List<string>();

        /// <summary>
        /// Additional environment variables to set for the process. Secrets should be avoided; the Harbor
        /// uses its own host login for provider auth.
        /// </summary>
        public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// For an API-endpoint captain (<see cref="Runtime"/> == "ApiEndpoint"), the fully resolved inference
        /// endpoint the Harbor should drive. The Harbor has no database, so the Admiral ships the endpoint
        /// (including its API key) here over the authenticated link. Null for CLI-harness runtimes.
        /// </summary>
        public HarborInferenceEndpoint? InferenceEndpoint { get; set; } = null;

        /// <summary>
        /// Whether the CLI runtime runs with its auto-approve or permission-bypass flag, resolved on the Admiral from
        /// the captain setting and any vessel override. Null (an older Admiral) keeps the runtime default (on).
        /// </summary>
        public bool? AutoApprove { get; set; } = null;

        /// <summary>
        /// Mission-scoped MCP session token for the captain's Armada MCP connection (sent as X-Token to the MCP URL the
        /// Admiral advertised in the handshake), or null when none was minted. Valid only while the mission runs.
        /// </summary>
        public string? McpSessionToken { get; set; } = null;

        /// <summary>
        /// Whether the job may run in a Harbor-owned scratch directory. When true and <see cref="WorkingDirectory"/> is
        /// empty or does not exist on the Harbor host, the Harbor creates a per-job scratch directory, runs the job
        /// there, and removes it when the job ends. Interactive launches (chat turns, planning, refinement) set it,
        /// because the Admiral's own paths do not exist on the Harbor host. Default false (missions): the working
        /// directory must exist. Added in 1.0 additively; an older Harbor ignores it.
        /// </summary>
        public bool ScratchWorkingDirectory { get; set; } = false;

        /// <summary>
        /// Whether a Claude Code captain runs in streaming-JSON output mode, or a Codex captain in 'codex exec --json' mode
        /// (one typed event per stdout line), as the Admiral uses for chat turns so the reply streams token by token and
        /// the turn's telemetry (usage, tool calls) can be read. Ignored by other runtimes. Default false. A Harbor that
        /// predates Codex support ignores it for Codex and runs plain text, which the Admiral also reads.
        /// </summary>
        public bool StreamJsonOutput { get; set; } = false;

        /// <summary>
        /// Whether the runtime is asked to surface the model's reasoning (for example Mux --show-thinking). Default false.
        /// </summary>
        public bool ShowThinking { get; set; } = false;

        /// <summary>
        /// Whether the Harbor captures the runtime's final-message artifact (for example Codex --output-last-message) and
        /// sends it back as an <c>output</c> message on the <see cref="HarborOutputStreamEnum.FinalMessage"/> stream just
        /// before <c>exited</c>. Default false.
        /// </summary>
        public bool ReturnFinalMessage { get; set; } = false;

        #endregion
    }
}
