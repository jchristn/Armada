namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for prompt template operations.
    /// </summary>
    public class PromptTemplateArgs
    {
        /// <summary>
        /// Template name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Template content with {Placeholder} parameters.
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// Template description.
        /// </summary>
        public string? Description { get; set; }
    }
}
