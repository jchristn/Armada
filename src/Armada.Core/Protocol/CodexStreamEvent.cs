namespace Armada.Core.Protocol
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One JSONL event of <c>codex exec --json</c>, discriminated by <c>type</c>. Only lines whose <c>type</c> is one of
    /// Codex's event types (<see cref="KnownTypes"/>) are treated as protocol events.
    /// </summary>
    public class CodexStreamEvent
    {
        #region Public-Members

        /// <summary>thread.started.</summary>
        public const string TypeThreadStarted = "thread.started";

        /// <summary>turn.started.</summary>
        public const string TypeTurnStarted = "turn.started";

        /// <summary>turn.completed (carries usage).</summary>
        public const string TypeTurnCompleted = "turn.completed";

        /// <summary>turn.failed.</summary>
        public const string TypeTurnFailed = "turn.failed";

        /// <summary>item.started.</summary>
        public const string TypeItemStarted = "item.started";

        /// <summary>item.updated.</summary>
        public const string TypeItemUpdated = "item.updated";

        /// <summary>item.completed.</summary>
        public const string TypeItemCompleted = "item.completed";

        /// <summary>error.</summary>
        public const string TypeError = "error";

        /// <summary>
        /// The Codex event types.
        /// </summary>
        public static readonly IReadOnlySet<string> KnownTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            TypeThreadStarted, TypeTurnStarted, TypeTurnCompleted, TypeTurnFailed,
            TypeItemStarted, TypeItemUpdated, TypeItemCompleted, TypeError
        };

        /// <summary>
        /// Event type.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Thread identifier (thread.started), or null.
        /// </summary>
        [JsonPropertyName("thread_id")]
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// Item (item.*), or null.
        /// </summary>
        [JsonPropertyName("item")]
        public CodexStreamItem? Item { get; set; } = null;

        /// <summary>
        /// Usage (turn.completed), or null.
        /// </summary>
        [JsonPropertyName("usage")]
        public CodexUsage? Usage { get; set; } = null;

        /// <summary>
        /// Message (error), or null.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; } = null;

        /// <summary>
        /// Error payload (turn.failed) as raw JSON, or null.
        /// </summary>
        [JsonPropertyName("error")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        public string? Error { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse one output line. Returns false for plain text, malformed JSON, and JSON without a known Codex type.
        /// </summary>
        /// <param name="line">Raw line.</param>
        /// <param name="value">The event, or null.</param>
        /// <returns>True when the line is a Codex protocol event.</returns>
        public static bool TryParse(string? line, out CodexStreamEvent? value)
        {
            if (RuntimeStreamJson.TryDeserializeLine<CodexStreamEvent>(line, out value)
                && value!.Type != null && KnownTypes.Contains(value.Type))
                return true;

            value = null;
            return false;
        }

        #endregion
    }
}
