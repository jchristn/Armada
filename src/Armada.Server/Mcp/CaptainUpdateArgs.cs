namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for updating a captain.
    /// </summary>
    public class CaptainUpdateArgs
    {
        /// <summary>
        /// Captain ID (cpt_ prefix).
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// New display name.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// New agent runtime: ClaudeCode, Codex, Gemini, Cursor, Mux, or Custom.
        /// </summary>
        public string? Runtime { get; set; }

        /// <summary>
        /// New system instructions for this captain.
        /// </summary>
        public string? SystemInstructions { get; set; }

        /// <summary>
        /// New AI model identifier. Null means the runtime selects its default model.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Optional reasoning-effort level: Off, Minimal, Low, Medium, or High. Translated to each
        /// runtime's native control at launch (Claude thinking budget, Codex reasoning effort, Mux --effort).
        /// </summary>
        public string? ReasoningEffort { get; set; }

        /// <summary>
        /// Optional capability tier: Economy, Standard, or Premium. Empty/null auto-classifies from model.
        /// </summary>
        public string? Tier { get; set; }

        /// <summary>
        /// JSON array of persona names this captain can fill. Null means any persona.
        /// </summary>
        public string? AllowedPersonas { get; set; }

        /// <summary>
        /// Preferred persona for dispatch routing priority.
        /// </summary>
        public string? PreferredPersona { get; set; }

        /// <summary>
        /// Optional Mux config directory override.
        /// Provide an empty string to clear it.
        /// </summary>
        public string? MuxConfigDirectory { get; set; }

        /// <summary>
        /// Named Mux endpoint to use for this captain.
        /// Provide an empty string to clear it.
        /// </summary>
        public string? MuxEndpoint { get; set; }

        /// <summary>
        /// Optional Mux base URL override.
        /// Provide an empty string to clear it.
        /// </summary>
        public string? MuxBaseUrl { get; set; }

        /// <summary>
        /// Optional Mux adapter type override.
        /// Provide an empty string to clear it.
        /// </summary>
        public string? MuxAdapterType { get; set; }

        /// <summary>
        /// Optional Mux temperature override.
        /// </summary>
        public double? MuxTemperature { get; set; }

        /// <summary>
        /// Optional Mux max tokens override.
        /// </summary>
        public int? MuxMaxTokens { get; set; }

        /// <summary>
        /// Optional Mux system prompt file path.
        /// Provide an empty string to clear it.
        /// </summary>
        public string? MuxSystemPromptPath { get; set; }

        /// <summary>
        /// Optional Mux approval policy override.
        /// Provide an empty string to clear it.
        /// </summary>
        public string? MuxApprovalPolicy { get; set; }

        /// <summary>
        /// Whether the CLI captain runs with its runtime's auto-approve or permission-bypass flag. Null leaves the
        /// current value (default true); false runs it without auto-approve where the runtime supports it.
        /// </summary>
        public bool? AutoApprove { get; set; }

    }
}
