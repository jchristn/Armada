namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// What a model can use as served by its runtime and provider. Null means not known. What a captain can do on its
    /// Harbor (read worktree files, run builds) is recorded on the captain, not here. An administrator's value
    /// (<see cref="ToolsAvailable"/>, <see cref="McpAvailable"/>) always wins over what a model probe measured
    /// (<see cref="ProbedToolsAvailable"/>, <see cref="ProbedMcpAvailable"/>); routing reads the effective values.
    /// </summary>
    public class ModelProfileCapabilities
    {
        /// <summary>
        /// Whether the model can call tools as served, set by an administrator, or null when not set.
        /// </summary>
        public bool? ToolsAvailable { get; set; } = null;

        /// <summary>
        /// Whether MCP tools are available to the model as served, set by an administrator, or null when not set.
        /// </summary>
        public bool? McpAvailable { get; set; } = null;

        /// <summary>
        /// Whether the latest model probe that ran the tool-calling probe found working tool calls, or null when no probe
        /// measured it. Written by probes only; an update request cannot set it.
        /// </summary>
        public bool? ProbedToolsAvailable { get; set; } = null;

        /// <summary>
        /// Whether MCP tools were available as probed (working tool calls on a runtime that gives the captain the Armada MCP
        /// server), or null when no probe measured it. Written by probes only; an update request cannot set it.
        /// </summary>
        public bool? ProbedMcpAvailable { get; set; } = null;

        /// <summary>
        /// When a probe last wrote the probed values (UTC), or null.
        /// </summary>
        public DateTime? ProbedUtc { get; set; } = null;

        /// <summary>
        /// Tool calls in effect: the administrator's value, else the probed one, else null (not known). Read-only.
        /// </summary>
        public bool? EffectiveToolsAvailable
        {
            get => ToolsAvailable ?? ProbedToolsAvailable;
        }

        /// <summary>
        /// MCP tools in effect: the administrator's value, else the probed one, else null (not known). Read-only.
        /// </summary>
        public bool? EffectiveMcpAvailable
        {
            get => McpAvailable ?? ProbedMcpAvailable;
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileCapabilities()
        {
        }
    }
}
