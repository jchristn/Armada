namespace Armada.Core.Protocol
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One newline-delimited event of Claude Code's <c>--output-format stream-json</c> output. Only lines whose
    /// <c>type</c> is one of the known event types (<see cref="KnownTypes"/>) are treated as protocol events.
    /// </summary>
    public class ClaudeStreamLine
    {
        #region Public-Members

        /// <summary>
        /// Session initialization and other system events.
        /// </summary>
        public const string TypeSystem = "system";

        /// <summary>
        /// A complete assistant message (text, thinking, and tool_use blocks).
        /// </summary>
        public const string TypeAssistant = "assistant";

        /// <summary>
        /// A user message (tool_result blocks).
        /// </summary>
        public const string TypeUser = "user";

        /// <summary>
        /// The terminal result event (final text, duration, usage).
        /// </summary>
        public const string TypeResult = "result";

        /// <summary>
        /// A raw API streaming event (partial messages).
        /// </summary>
        public const string TypeStreamEvent = "stream_event";

        /// <summary>
        /// A bare tool_use event.
        /// </summary>
        public const string TypeToolUse = "tool_use";

        /// <summary>
        /// The known event discriminators.
        /// </summary>
        public static readonly IReadOnlySet<string> KnownTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            TypeSystem, TypeAssistant, TypeUser, TypeResult, TypeStreamEvent, TypeToolUse
        };

        /// <summary>
        /// Event type.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Event subtype (for example init on a system event, success or error_* on a result event).
        /// </summary>
        [JsonPropertyName("subtype")]
        public string? Subtype { get; set; } = null;

        /// <summary>
        /// Message of an assistant or user event, or null.
        /// </summary>
        [JsonPropertyName("message")]
        public ClaudeStreamMessage? Message { get; set; } = null;

        /// <summary>
        /// Raw API event of a stream_event line, or null.
        /// </summary>
        [JsonPropertyName("event")]
        public ClaudeStreamApiEvent? Event { get; set; } = null;

        /// <summary>
        /// Final reply text of a result event, or null.
        /// </summary>
        [JsonPropertyName("result")]
        public string? Result { get; set; } = null;

        /// <summary>
        /// Whether a result event reports an error, or null.
        /// </summary>
        [JsonPropertyName("is_error")]
        public bool? IsError { get; set; } = null;

        /// <summary>
        /// Run duration in milliseconds on a result event, or null.
        /// </summary>
        [JsonPropertyName("duration_ms")]
        public double? DurationMs { get; set; } = null;

        /// <summary>
        /// Token usage on a result event, or null.
        /// </summary>
        [JsonPropertyName("usage")]
        public ClaudeStreamUsage? Usage { get; set; } = null;

        /// <summary>
        /// Tool name of a bare tool_use event, or null.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Tool calls the CLI refused for lack of permission, on a result event (permission_denials), or null.
        /// </summary>
        [JsonPropertyName("permission_denials")]
        public List<ClaudePermissionDenial>? PermissionDenials { get; set; } = null;

        /// <summary>
        /// Model of a system init event, or null.
        /// </summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; } = null;

        /// <summary>
        /// The incremental reply text of a stream_event content_block_delta/text_delta, or null for any other event.
        /// </summary>
        [JsonIgnore]
        public string? TextDelta
        {
            get
            {
                if (!String.Equals(Type, TypeStreamEvent, StringComparison.Ordinal)) return null;
                if (Event == null || !String.Equals(Event.Type, "content_block_delta", StringComparison.Ordinal)) return null;
                if (Event.Delta == null || !String.Equals(Event.Delta.Type, "text_delta", StringComparison.Ordinal)) return null;
                return Event.Delta.Text;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse one stream line. Returns false for plain text, malformed JSON, and JSON whose <c>type</c> is not a known
        /// Claude Code event type.
        /// </summary>
        /// <param name="line">Raw line.</param>
        /// <param name="value">The event, or null.</param>
        /// <returns>True when the line is a known Claude Code stream-json event.</returns>
        public static bool TryParse(string? line, out ClaudeStreamLine? value)
        {
            if (RuntimeStreamJson.TryDeserializeLine<ClaudeStreamLine>(line, out value)
                && value!.Type != null && KnownTypes.Contains(value.Type))
                return true;

            value = null;
            return false;
        }

        #endregion
    }
}
