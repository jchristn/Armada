namespace Armada.Server.Mcp
{
    using System.Collections.Generic;
    using Armada.Core.Services;

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
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<PipelineStageArgs>? Stages { get; set; }

        /// <summary>
        /// Optional ownership scope for create (UserSpecific or TenantWide), resolved against what the caller may create.
        /// </summary>
        public Armada.Core.Enums.ScopeEnum? Scope { get; set; }
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

        /// <summary>
        /// Whether the stage's mission waits for human review before the pipeline advances.
        /// </summary>
        public bool? RequiresReview { get; set; }

        /// <summary>
        /// What a review denial does: RetryStage or FailPipeline (ReviewDenyActionEnum names, case-insensitive).
        /// </summary>
        public string? ReviewDenyAction { get; set; }
    }
}
