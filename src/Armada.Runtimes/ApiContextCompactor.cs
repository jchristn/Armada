namespace Armada.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using Armada.Core.Services;
    using PolyPrompt.Models;

    /// <summary>
    /// Keeps an API captain's conversation inside the model's context window (R2). Without it, a long mission on a model
    /// with a small window either stops at the context tripwire or, when the provider truncates silently, continues on a
    /// history that has lost the mission brief. Compaction is deterministic and needs no extra model call: the oldest tool
    /// outputs (file reads, command output, the bulk of the growth) are replaced by a one-line note saying what was there,
    /// and older assistant turns lose the file bodies they wrote (write_file, edit_file, multi_edit arguments keep only the
    /// path and a size note) and their reasoning. The system prompt, the mission brief and everything from the most recent
    /// tool outputs on are kept whole. The captain can run a tool again or read the file when it needs what was removed.
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

        /// <summary>
        /// Opening text of the note that replaces an old write or edit call's file text.
        /// </summary>
        public const string CompactedArgumentsMarker = "Armada removed the file text of this earlier call to keep the conversation inside the model's context window";

        /// <summary>
        /// Text left in an old assistant reply that held only reasoning.
        /// </summary>
        public const string CompactedReasoningNote = "[Armada removed this earlier reasoning to keep the conversation inside the model's context window.]";

        /// <summary>
        /// Tokens assumed for each message's role and framing.
        /// </summary>
        public const int MessageOverheadTokens = 4;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Estimate the prompt size of a conversation in tokens: message text, tool-call names and arguments, and a small
        /// per-message overhead, counted by the estimator (about 3.2 characters per token by default). Used when the
        /// provider's own count is missing or low (Ollama does not count prompt tokens served from its cache).
        /// </summary>
        /// <param name="messages">The conversation.</param>
        /// <param name="estimator">Token counter, or null for <see cref="CharacterRatioTokenEstimator.Default"/>.</param>
        /// <returns>The estimated token count.</returns>
        /// <exception cref="ArgumentNullException">Thrown when messages is null.</exception>
        public static long EstimateTokens(IReadOnlyList<ChatMessage> messages, ITokenEstimator? estimator = null)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            ITokenEstimator counter = estimator ?? CharacterRatioTokenEstimator.Default;
            long tokens = 0;
            foreach (ChatMessage message in messages)
            {
                tokens += MessageOverheadTokens + counter.CountTokens(message.Text);
                if (message.ToolCalls == null) continue;
                foreach (ToolCall call in message.ToolCalls) tokens += counter.CountTokens(call.Name) + counter.CountTokens(call.ArgumentsJson);
            }
            return tokens;
        }

        /// <summary>
        /// Estimate the input tokens the tool schemas of a request cost, with the same estimator as
        /// <see cref="EstimateTokens"/>. Every model turn resends them, so they count toward the window like messages.
        /// </summary>
        /// <param name="tools">Tool definitions sent with the request.</param>
        /// <param name="estimator">Token counter, or null for <see cref="CharacterRatioTokenEstimator.Default"/>.</param>
        /// <returns>Estimated tokens.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tools"/> is null.</exception>
        public static long EstimateToolTokens(IReadOnlyList<ToolDefinition> tools, ITokenEstimator? estimator = null)
        {
            if (tools == null) throw new ArgumentNullException(nameof(tools));
            ITokenEstimator counter = estimator ?? CharacterRatioTokenEstimator.Default;
            long tokens = 0;
            foreach (ToolDefinition tool in tools)
            {
                if (tool == null) continue;
                tokens += MessageOverheadTokens + counter.CountTokens(JsonSerializer.Serialize(tool));
            }
            return tokens;
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
        /// Compact everything older than the most recent <see cref="KeepRecentToolResults"/> tool outputs, oldest first:
        /// tool outputs become a short note, write_file, edit_file and multi_edit calls keep only their path and a size
        /// note, and assistant reasoning is removed. Text already compacted and text shorter than
        /// <see cref="MinimumCompactedLength"/> is left alone. Arguments that are not valid JSON are left as they are.
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

            int oldCount = toolResults.Count - KeepRecentToolResults;
            if (oldCount <= 0) return 0;

            long removed = 0;
            for (int n = 0; n < oldCount; n++)
            {
                int index = toolResults[n];
                ChatMessage original = messages[index];
                string content = original.Text ?? String.Empty;
                if (content.Length < MinimumCompactedLength || content.StartsWith(CompactedMarker, StringComparison.Ordinal)) continue;

                string note = CompactedMarker + ": " + content.Length.ToString(CultureInfo.InvariantCulture) + " characters from "
                    + (String.IsNullOrEmpty(original.ToolName) ? "a tool" : original.ToolName) + ". Run the tool again if you still need it.]";
                messages[index] = ChatMessage.ToolResult(original.ToolCallId!, original.ToolName ?? String.Empty, note);
                removed += content.Length - note.Length;
            }

            // Assistant turns before the newest compacted tool output are old by the same recency rule; the turn that
            // issued the oldest kept output stays whole.
            int lastOld = toolResults[oldCount - 1];
            for (int index = 0; index < lastOld; index++)
            {
                ChatMessage message = messages[index];
                if (!IsAssistant(message)) continue;
                if (message.ToolCalls != null)
                    foreach (ToolCall call in message.ToolCalls) removed += CompactArguments(call);
                removed += CompactReasoning(message);
            }
            return removed;
        }

        /// <summary>
        /// Replace the file text in a write_file, edit_file or multi_edit call with its path and a size note.
        /// </summary>
        /// <param name="call">The call, changed in place.</param>
        /// <returns>Characters removed; zero when the call is another tool, small, already compacted, or not valid JSON.</returns>
        /// <exception cref="ArgumentNullException">Thrown when call is null.</exception>
        public static long CompactArguments(ToolCall call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            string json = call.ArgumentsJson ?? String.Empty;
            if (json.Length < MinimumCompactedLength || !IsWriteTool(call.Name) || json.Contains(CompactedArgumentsMarker, StringComparison.Ordinal)) return 0;

            CompactableToolArguments? args;
            try
            {
                args = JsonSerializer.Deserialize<CompactableToolArguments>(json);
            }
            catch (JsonException)
            {
                return 0;
            }
            if (args == null) return 0;

            long bodyCharacters = Length(args.Content) + Length(args.OldString) + Length(args.NewString);
            int edits = 0;
            if (args.Edits != null)
                foreach (CompactableToolArguments edit in args.Edits)
                {
                    if (edit == null) continue;
                    edits++;
                    bodyCharacters += Length(edit.OldString) + Length(edit.NewString);
                }
            if (bodyCharacters == 0) return 0;

            CompactedToolArguments compacted = new CompactedToolArguments
            {
                FilePath = args.FilePath,
                Note = CompactedArgumentsMarker + ": " + bodyCharacters.ToString(CultureInfo.InvariantCulture) + " characters"
                    + (edits > 0 ? " in " + edits.ToString(CultureInfo.InvariantCulture) + " edits" : String.Empty)
                    + ". The call ran; read the file if you need its current text."
            };
            string replacement = JsonSerializer.Serialize(compacted);
            if (replacement.Length >= json.Length) return 0;
            call.ArgumentsJson = replacement;
            return json.Length - replacement.Length;
        }

        /// <summary>
        /// Remove reasoning (think blocks, text before a bare closing think tag) from an assistant message's text.
        /// Provider continuation state is not changed: providers that replay reasoning keep what they need there.
        /// </summary>
        /// <param name="message">The message, changed in place.</param>
        /// <returns>Characters removed; zero when the text holds no reasoning.</returns>
        /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
        public static long CompactReasoning(ChatMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            string text = message.Text ?? String.Empty;
            if (text.Length == 0 || !ReasoningText.HasReasoning(text)) return 0;
            string stripped = ReasoningText.Strip(text).Trim();
            bool hasCalls = message.ToolCalls != null && message.ToolCalls.Count > 0;
            if (stripped.Length == 0) stripped = hasCalls ? String.Empty : CompactedReasoningNote;
            if (stripped.Length >= text.Length) return 0;
            message.Text = stripped.Length == 0 ? null : stripped;
            return text.Length - stripped.Length;
        }

        #endregion

        #region Private-Methods

        private static bool IsAssistant(ChatMessage message)
        {
            return String.Equals(message.Role.ToString(), "assistant", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWriteTool(string? name)
        {
            return String.Equals(name, "write_file", StringComparison.Ordinal)
                || String.Equals(name, "edit_file", StringComparison.Ordinal)
                || String.Equals(name, "multi_edit", StringComparison.Ordinal);
        }

        private static long Length(string? text)
        {
            return text?.Length ?? 0;
        }

        #endregion
    }
}
