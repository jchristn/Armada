namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for creating or updating a fleet action.
    /// </summary>
    public class FleetActionUpsertArgs
    {
        /// <summary>
        /// Fleet action ID (fac_ prefix). Required for update; ignored for create.
        /// </summary>
        public string? ActionId { get; set; }

        /// <summary>
        /// Display name. Required for create.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Optional description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Kind: Command or Mission. Defaults to Command on create.
        /// </summary>
        public string? Kind { get; set; }

        /// <summary>
        /// Shell command template (Command kind).
        /// </summary>
        public string? CommandText { get; set; }

        /// <summary>
        /// Prompt template (Mission kind).
        /// </summary>
        public string? PromptTemplate { get; set; }

        /// <summary>
        /// Optional pipeline ID (Mission kind).
        /// </summary>
        public string? PipelineId { get; set; }

        /// <summary>
        /// Optional persona name (Mission kind).
        /// </summary>
        public string? Persona { get; set; }

        /// <summary>
        /// Per-target timeout in seconds (5 to 7200).
        /// </summary>
        public int? TimeoutSeconds { get; set; }

        /// <summary>
        /// Default concurrency (1 to 32).
        /// </summary>
        public int? DefaultConcurrency { get; set; }

        /// <summary>
        /// Whether targets with a dirty working tree are skipped (Command kind).
        /// </summary>
        public bool? RequiresCleanWorkingTree { get; set; }
    }
}
