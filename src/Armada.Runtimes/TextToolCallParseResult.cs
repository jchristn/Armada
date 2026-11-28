namespace Armada.Runtimes
{
    using System;
    using System.Collections.Generic;
    using PolyPrompt.Models;

    /// <summary>
    /// Tool calls recovered from a model's visible text by <see cref="TextToolCallParser"/>.
    /// </summary>
    public sealed class TextToolCallParseResult
    {
        /// <summary>Recovered calls, in the order they appeared; empty when none were recovered.</summary>
        public List<ToolCall> Calls { get; set; } = new List<ToolCall>();

        /// <summary>The text with the recovered call markup removed, trimmed.</summary>
        public string RemainingText { get; set; } = String.Empty;

        /// <summary>The format the first call was written in: hermes, function_tag or fenced_json; null when none.</summary>
        public string? Format { get; set; } = null;
    }
}
