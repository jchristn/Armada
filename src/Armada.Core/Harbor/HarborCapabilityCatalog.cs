namespace Armada.Core.Harbor
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Plain-language names for the Harbor capability flags (<see cref="HarborCapabilityFlags"/>), for display.
    /// </summary>
    public static class HarborCapabilityCatalog
    {
        #region Private-Members

        private static readonly Dictionary<string, string> _Descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { HarborCapabilityFlags.InferenceStreaming, "streamed model responses" },
            { HarborCapabilityFlags.SubmitTool, "the submit tool for structured stage results" },
            { HarborCapabilityFlags.ToolScope, "persona-scoped Armada tools" },
            { HarborCapabilityFlags.CompletionLine, "treating a missing completion line as incomplete" },
            { HarborCapabilityFlags.CliFinalMessageHygiene, "clean final messages from CLI agents" },
            { HarborCapabilityFlags.TextToolCallRecovery, "recovering tool calls a model wrote as text" },
            { HarborCapabilityFlags.MalformedCallRetry, "correcting malformed tool calls" },
            { HarborCapabilityFlags.RepetitionGuard, "stopping repeated identical tool calls" },
            { HarborCapabilityFlags.ReasoningHygiene, "removing reasoning from final messages" },
            { HarborCapabilityFlags.ToolLimitsScaled, "tool output limits scaled to the context window" },
            { HarborCapabilityFlags.EditEncodingPreserve, "keeping file encodings and line endings on edits" },
            { HarborCapabilityFlags.EditLinePrefixGuard, "rejecting edits that carry line-number prefixes" },
            { HarborCapabilityFlags.ContextDiscovery, "discovering a local model's context window" },
            { HarborCapabilityFlags.ModelWarmup, "warming the model before the first turn" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// The plain-language name of a flag, or the flag itself when it is not known to this build.
        /// </summary>
        /// <param name="flag">Flag name.</param>
        /// <returns>The description.</returns>
        public static string Describe(string? flag)
        {
            if (String.IsNullOrWhiteSpace(flag)) return String.Empty;
            return _Descriptions.TryGetValue(flag, out string? description) ? description : flag;
        }

        #endregion
    }
}
