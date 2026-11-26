namespace Armada.Runtimes
{
    using System;
    using System.Collections.Generic;
    using PolyPrompt.Models;

    /// <summary>
    /// Keeps an API captain's conversation inside the model's context window (R2). Without it, a long mission on a model
    /// with a small window either stops at the context tripwire or, when the provider truncates silently, continues on a
    /// history that has lost the mission brief. Compaction is deterministic and needs no extra model call: the oldest tool
    /// outputs (file reads, command output, the bulk of the growth) are replaced by a one-line note saying what was there,
    /// while the system prompt, the mission brief, every assistant turn and the most recent tool outputs are kept whole.
    /// The captain can run a tool again when it needs an output that was removed.
    /// </summary>
    public static class ApiContextCompactor
    {
        #region Public-Members

        /// <summary>
        /// Share of the context window at which older tool outputs are compacted.
        /// </summary>
        public const double CompactAtShare = 0.80;

        /// <summary>
        /// Number of most recent tool outputs that are never compacted.
        /// </summary>
        public const int KeepRecentToolResults = 6;

        /// <summary>
        /// Tool outputs shorter than this many characters are left alone; replacing them saves nothing.
        /// </summary>
        public const int MinimumCompactedLength = 600;

        /// <summary>
        /// Opening text of a compacted tool output.
        /// </summary>
        public const string CompactedMarker = "[Armada removed this earlier tool output to keep the conversation inside the model's context window";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Estimate the prompt size of a conversation in tokens, at about four characters per token plus a small
        /// per-message overhead. Used when the provider's own count is missing or low (Ollama does not count prompt tokens
        /// served from its cache).
        /// </summary>
        /// <param name="messages">The conversation.</param>
        /// <returns>The estimated token count.</returns>
        /// <exception cref="ArgumentNullException">Thrown when messages is null.</exception>
        public static long EstimateTokens(IReadOnlyList<ChatMessage> messages)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            long characters = 0;
            foreach (ChatMessage message in messages)
            {
                characters += 16 + (message.Content?.Length ?? 0);
                if (message.ToolCalls == null) continue;
                foreach (ToolCall call in message.ToolCalls) characters += (call.Name?.Length ?? 0) + (call.ArgumentsJson?.Length ?? 0);
            }
            return characters / 4;
        }

        /// <summary>
        /// Estimate the input tokens the tool schemas of a request cost (the same four characters per token as
        /// <see cref="EstimateTokens"/>). Every model turn resends them, so they count toward the window like messages.
        /// </summary>
        /// <param name="tools">Tool definitions sent with the request.</param>
        /// <returns>Estimated tokens.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tools"/> is null.</exception>
        public static long EstimateToolTokens(IReadOnlyList<ToolDefinition> tools)
        {
            if (tools == null) throw new ArgumentNullException(nameof(tools));
            long characters = 0;
            foreach (ToolDefinition tool in tools)
            {
                if (tool == null) continue;
                characters += 16 + System.Text.Json.JsonSerializer.Serialize(tool).Length;
            }
            return characters / 4;
        }

        /// <summary>
        /// Whether a conversation of this size should be compacted for this window.
        /// </summary>
        /// <param name="promptTokens">The larger of the provider's prompt count and the local estimate.</param>
        /// <param name="contextWindow">The configured context window, or null when unknown (nothing is compacted).</param>
        /// <returns>True when compaction should run.</returns>
        public static bool ShouldCompact(long promptTokens, int? contextWindow)
        {
            if (!contextWindow.HasValue || contextWindow.Value <= 0) return false;
            return promptTokens >= (long)(contextWindow.Value * CompactAtShare);
        }

        /// <summary>
        /// Replace every tool output except the most recent <see cref="KeepRecentToolResults"/> with a short note, oldest
        /// first. Outputs already compacted and outputs shorter than <see cref="MinimumCompactedLength"/> are left alone.
        /// </summary>
        /// <param name="messages">The conversation, changed in place.</param>
        /// <returns>The number of characters removed; zero when nothing could be compacted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when messages is null.</exception>
        public static long Compact(List<ChatMessage> messages)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));

            List<int> toolResults = new List<int>();
            for (int i = 0; i < messages.Count; i++)
                if (!String.IsNullOrEmpty(messages[i].ToolCallId) && (messages[i].ToolCalls == null || messages[i].ToolCalls!.Count == 0)) toolResults.Add(i);

            long removed = 0;
            for (int n = 0; n < toolResults.Count - KeepRecentToolResults; n++)
            {
                int index = toolResults[n];
                ChatMessage original = messages[index];
                string content = original.Content ?? String.Empty;
                if (content.Length < MinimumCompactedLength || content.StartsWith(CompactedMarker, StringComparison.Ordinal)) continue;

                string note = CompactedMarker + ": " + content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " characters from "
                    + (String.IsNullOrEmpty(original.ToolName) ? "a tool" : original.ToolName) + ". Run the tool again if you still need it.]";
                messages[index] = ChatMessage.ToolResult(original.ToolCallId!, original.ToolName ?? String.Empty, note);
                removed += content.Length - note.Length;
            }
            return removed;
        }

        #endregion
    }
}
