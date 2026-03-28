namespace Armada.Server.Mcp
{
    using System.Collections.Generic;

    /// <summary>
    /// MCP tool arguments for pipeline operations.
    /// </summary>
    public class PipelineArgs
    {
        /// <summary>
        /// Pipeline name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Pipeline description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Ordered list of pipeline stages.
        /// </summary>
        public List<PipelineStageArgs>? Stages { get; set; }
    }

    /// <summary>
    /// A single stage in a pipeline definition.
    /// </summary>
    public class PipelineStageArgs
    {
        /// <summary>
        /// Persona name for this stage.
        /// </summary>
        public string PersonaName { get; set; } = "";

        /// <summary>
        /// Whether this stage is optional.
        /// </summary>
        public bool? IsOptional { get; set; }

        /// <summary>
        /// Description of what this stage does.
        /// </summary>
        public string? Description { get; set; }
    }
}
