namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for persona operations.
    /// </summary>
    public class PersonaArgs
    {
        /// <summary>
        /// Persona name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Persona description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Prompt template name for this persona.
        /// </summary>
        public string? PromptTemplateName { get; set; }
    }
}
