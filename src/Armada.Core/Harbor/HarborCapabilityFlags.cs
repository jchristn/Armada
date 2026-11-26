namespace Armada.Core.Harbor
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Names of the capability flags a Harbor advertises in its handshake for API captain behaviors that run on the
    /// Harbor, and the check an Admiral uses before relying on one. A Harbor that does not advertise a flag runs the
    /// earlier behavior; the Admiral falls back to it and reports "Harbor update recommended" when a flag this build
    /// expects is missing.
    /// </summary>
    public static class HarborCapabilityFlags
    {
        #region Public-Members

        /// <summary>Streams API captain responses (idle and first-response deadlines). Advertised since 1.4.0.</summary>
        public const string InferenceStreaming = HarborProtocol.InferenceStreamingCapability;

        /// <summary>Honors the native submit tool for a stage's structured output (ARMADA_SUBMIT_TOOL, RS-6.3).</summary>
        public const string SubmitTool = "api-submit-tool-v1";

        /// <summary>Limits the scoped MCP tools to the persona's scope (ARMADA_MCP_TOOL_SCOPE, RS-6.13).</summary>
        public const string ToolScope = "api-tool-scope-v1";

        /// <summary>Treats a missing completion line as incomplete (ARMADA_REQUIRES_COMPLETION_LINE, RS-6.6).</summary>
        public const string CompletionLine = "api-completion-line-v1";

        /// <summary>
        /// CLI runtimes clean their final message on the Harbor: OpenCode's JSON final message and stdout error events,
        /// no <c>--dir</c> flag for OpenCode 2, and reasoning stripped from CLI final messages (RS-7.12).
        /// </summary>
        public const string CliFinalMessageHygiene = "cli-final-message-hygiene-v1";

        /// <summary>Recovers tool calls a model wrote as text (RS-7.2).</summary>
        public const string TextToolCallRecovery = "api-text-tool-call-recovery-v1";

        /// <summary>Corrects instead of resending a request that failed on a malformed tool call (RS-7.3, RS-7.17).</summary>
        public const string MalformedCallRetry = "api-malformed-call-retry-v1";

        /// <summary>Stops repeated identical tool calls (RS-7.5).</summary>
        public const string RepetitionGuard = "api-repetition-guard-v1";

        /// <summary>Strips reasoning from the final message and history (RS-7.6).</summary>
        public const string ReasoningHygiene = "api-reasoning-hygiene-v1";

        /// <summary>Scales tool output limits to the context window (RS-7.9).</summary>
        public const string ToolLimitsScaled = "api-tool-limits-scaled-v1";

        /// <summary>Keeps a file's byte order mark and line endings on edits and writes (RS-7.14).</summary>
        public const string EditEncodingPreserve = "api-edit-encoding-preserve-v1";

        /// <summary>Rejects edits and writes whose text carries read_file line-number prefixes (RS-7.15).</summary>
        public const string EditLinePrefixGuard = "api-edit-line-prefix-guard-v1";

        /// <summary>Discovers a model's served context window on the Harbor (RS-7.8).</summary>
        public const string ContextDiscovery = "api-context-discovery-v1";

        /// <summary>Warms the model before the first turn (RS-7.16).</summary>
        public const string ModelWarmup = "api-model-warmup-v1";

        /// <summary>
        /// Every flag name this class declares, whether or not this build implements it yet.
        /// </summary>
        public static IReadOnlyList<string> All { get; } = new List<string>
        {
            InferenceStreaming, SubmitTool, ToolScope, CompletionLine, CliFinalMessageHygiene, TextToolCallRecovery, MalformedCallRetry,
            RepetitionGuard, ReasoningHygiene, ToolLimitsScaled, EditEncodingPreserve, EditLinePrefixGuard, ContextDiscovery, ModelWarmup
        };

        /// <summary>
        /// The flags this build's Harbor advertises when it runs jobs, which is also the set this build's Admiral expects:
        /// a Harbor missing one of them gets "Harbor update recommended". Add a flag here when its behavior ships.
        /// </summary>
        public static IReadOnlyList<string> Advertised { get; } = new List<string>
        {
            SubmitTool, ToolScope, CompletionLine, CliFinalMessageHygiene, ToolLimitsScaled, EditEncodingPreserve, EditLinePrefixGuard, ContextDiscovery
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a capability list advertises a flag as available.
        /// </summary>
        /// <param name="capabilities">The Harbor's advertised capabilities; null advertises nothing.</param>
        /// <param name="flag">Flag name.</param>
        /// <returns>True when the flag is present and available.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="flag"/> is null or empty.</exception>
        public static bool Supports(IEnumerable<HarborCapability>? capabilities, string flag)
        {
            if (String.IsNullOrWhiteSpace(flag)) throw new ArgumentNullException(nameof(flag));
            if (capabilities == null) return false;
            foreach (HarborCapability capability in capabilities)
                if (capability != null && capability.Available && String.Equals(capability.Name, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// The expected flags (<see cref="Advertised"/>) a capability list does not advertise, in declaration order.
        /// </summary>
        /// <param name="capabilities">The Harbor's advertised capabilities; null advertises nothing.</param>
        /// <returns>Missing flag names; empty when the Harbor is current.</returns>
        public static List<string> Missing(IEnumerable<HarborCapability>? capabilities)
        {
            List<string> missing = new List<string>();
            foreach (string flag in Advertised)
                if (!Supports(capabilities, flag)) missing.Add(flag);
            return missing;
        }

        /// <summary>
        /// Add every <see cref="Advertised"/> flag a capability list does not already carry.
        /// </summary>
        /// <param name="capabilities">The list a Harbor is about to advertise.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="capabilities"/> is null.</exception>
        public static void AddAdvertised(List<HarborCapability> capabilities)
        {
            if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
            foreach (string flag in Advertised)
                if (!capabilities.Exists(c => c != null && String.Equals(c.Name, flag, StringComparison.OrdinalIgnoreCase)))
                    capabilities.Add(new HarborCapability { Name = flag, Available = true });
        }

        #endregion
    }
}
