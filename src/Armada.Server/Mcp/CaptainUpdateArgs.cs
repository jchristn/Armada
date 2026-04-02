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
        /// New agent runtime: ClaudeCode, Codex, Gemini, Cursor, or Custom.
        /// </summary>
        public string? Runtime { get; set; }

        /// <summary>
        /// New runtime model override. Null lets the runtime choose its default model.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// New system instructions for this captain.
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

    }
}
