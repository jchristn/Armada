namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Services;

    /// <summary>Inference defaults and execution restrictions shared by local and Harbor API captains.</summary>
    public class InferenceOptions
    {
        /// <summary>Advertise and execute tools. False uses plain chat with no tools.</summary>
        public bool ToolsEnabled { get; set; } = true;
        /// <summary>Allow only built-in read-only tools; excludes shell, file mutations, and unclassified MCP tools.</summary>
        public bool ReadOnlyTools { get; set; } = false;
        /// <summary>Permit discovery of tools from the session's scoped MCP server.</summary>
        public bool McpToolsEnabled { get; set; } = true;
        /// <summary>Exact allowed tool names. Null allows all otherwise permitted tools; an empty list allows none.</summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string>? AllowedTools { get; set; } = null;
        /// <summary>Maximum model turns in an API captain run, from 1 through 1000.</summary>
        public int MaxIterations { get; set; } = 100;
        /// <summary>Optional maximum output tokens, from 1 through 10000000.</summary>
        public int? MaxTokens { get; set; } = null;
        /// <summary>Optional temperature, from 0 through 2; null uses provider defaults.</summary>
        public double? Temperature { get; set; } = null;
        /// <summary>Optional nucleus sampling threshold, from 0 through 1.</summary>
        public double? TopP { get; set; } = null;
        /// <summary>Require an eligible Harbor for any launch of this endpoint's API captain.</summary>
        public bool RequireHarbor { get; set; } = false;
        /// <summary>
        /// Optional cap, from 1 through 64, on missions running at once against this endpoint's base URL across every captain
        /// that uses it. Admiral-side admission only: a local model server serving one GPU stays within what it can hold
        /// instead of swapping models and timing out. Null is unlimited.
        /// </summary>
        public int? MaxConcurrentMissions { get; set; } = null;
        /// <summary>
        /// Optional context window, from 512 through 2097152 tokens, sent as Ollama's num_ctx on Ollama and Ollama Cloud
        /// endpoints. When set, an API captain whose prompt reaches the window fails with context_limit_exceeded instead
        /// of continuing on silently truncated history. Null uses the model default.
        /// </summary>
        public int? ContextLength { get; set; } = null;
        /// <summary>
        /// Stream model responses. Each call then fails only when the model sends nothing for the endpoint's request
        /// timeout (an idle deadline) instead of when the whole response takes longer, so long generations survive an
        /// idle-based proxy or load balancer deadline. Default false.
        /// </summary>
        public bool Streaming { get; set; } = false;
        /// <summary>
        /// Optional deadline, from 1000 through 1800000 ms, for the model to start responding: cold model loads and long
        /// prompt evaluation. Streaming applies it to each call's first chunk; without streaming it extends each call's
        /// timeout. Null uses the endpoint's request timeout.
        /// </summary>
        public int? FirstResponseTimeoutMs { get; set; } = null;

        /// <summary>Whether launching on a Harbor requires policy-aware execution support.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool RequiresPolicySupport => !ToolsEnabled || ReadOnlyTools || !McpToolsEnabled || AllowedTools != null
            || MaxIterations != 100 || MaxTokens.HasValue || Temperature.HasValue || TopP.HasValue || RequireHarbor;

        /// <summary>Whether launching on a Harbor requires a Harbor that applies the context window.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool RequiresContextSupport => ContextLength.HasValue;

        /// <summary>Whether launching on a Harbor requires a Harbor that streams and applies the first-response deadline.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool RequiresStreamingSupport => Streaming || FirstResponseTimeoutMs.HasValue;

        /// <summary>Whether any setting differs from the defaults.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasNonDefaultSettings => RequiresPolicySupport || RequiresContextSupport || RequiresStreamingSupport || MaxConcurrentMissions.HasValue;

        /// <summary>Validate supplied settings without silently changing them.</summary>
        public void Validate()
        {
            if (MaxIterations < 1 || MaxIterations > 1000) throw new ArgumentException("InferenceOptions.MaxIterations must be between 1 and 1000.");
            if (MaxTokens.HasValue && (MaxTokens < 1 || MaxTokens > 10000000)) throw new ArgumentException("InferenceOptions.MaxTokens must be between 1 and 10000000.");
            if (Temperature.HasValue && (!Double.IsFinite(Temperature.Value) || Temperature < 0 || Temperature > 2)) throw new ArgumentException("InferenceOptions.Temperature must be between 0 and 2.");
            if (TopP.HasValue && (!Double.IsFinite(TopP.Value) || TopP < 0 || TopP > 1)) throw new ArgumentException("InferenceOptions.TopP must be between 0 and 1.");
            if (MaxConcurrentMissions.HasValue && (MaxConcurrentMissions < 1 || MaxConcurrentMissions > 64)) throw new ArgumentException("InferenceOptions.MaxConcurrentMissions must be between 1 and 64.");
            if (ContextLength.HasValue && (ContextLength < 512 || ContextLength > 2097152)) throw new ArgumentException("InferenceOptions.ContextLength must be between 512 and 2097152.");
            if (FirstResponseTimeoutMs.HasValue && (FirstResponseTimeoutMs < 1000 || FirstResponseTimeoutMs > 1800000)) throw new ArgumentException("InferenceOptions.FirstResponseTimeoutMs must be between 1000 and 1800000.");
            if (AllowedTools != null && (AllowedTools.Count > 256 || AllowedTools.Any(n => String.IsNullOrWhiteSpace(n) || n.Length > 256 || n != n.Trim()) || AllowedTools.Distinct(StringComparer.Ordinal).Count() != AllowedTools.Count))
                throw new ArgumentException("InferenceOptions.AllowedTools must contain at most 256 distinct, nonblank exact tool names (maximum 256 characters each).");
        }
    }
}
