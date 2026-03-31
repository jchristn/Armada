namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for creating a captain.
    /// </summary>
    public class CaptainCreateArgs
    {
        /// <summary>
        /// Captain display name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Agent runtime: ClaudeCode, Codex, Gemini, Cursor, or Custom.
        /// </summary>
        public string? Runtime { get; set; }

        /// <summary>
        /// System instructions for this captain. Injected into every mission prompt.
        /// </summary>
        public string? SystemInstructions { get; set; }

        /// <summary>
        /// JSON array of persona names this captain can fill. Null means any persona.
        /// </summary>
        public string? AllowedPersonas { get; set; }

        /// <summary>
        /// Preferred persona for dispatch routing priority.
        /// </summary>
        public string? PreferredPersona { get; set; }

        /// <summary>
        /// AI model to use for this captain. When null, the runtime selects its default model.
        /// </summary>
        public string? Model { get; set; }

    }
}
