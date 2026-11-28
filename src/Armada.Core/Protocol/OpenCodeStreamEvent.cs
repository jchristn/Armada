namespace Armada.Core.Protocol
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One JSONL event of <c>opencode run --format json</c>, discriminated by <c>type</c>. Only lines whose <c>type</c>
    /// is one of OpenCode's event types (<see cref="KnownTypes"/>) are treated as protocol events; any other line
    /// (including JSON a model printed) is reply text.
    /// </summary>
    public class OpenCodeStreamEvent
    {
        #region Public-Members

        /// <summary>text (assistant reply text).</summary>
        public const string TypeText = "text";

        /// <summary>reasoning (model reasoning, with --thinking).</summary>
        public const string TypeReasoning = "reasoning";

        /// <summary>tool_use (a tool part with its state).</summary>
        public const string TypeToolUse = "tool_use";

        /// <summary>step_start.</summary>
        public const string TypeStepStart = "step_start";

        /// <summary>step_finish.</summary>
        public const string TypeStepFinish = "step_finish";

        /// <summary>error.</summary>
        public const string TypeError = "error";

        /// <summary>
        /// Most characters kept from an untrusted provider message on an error event.
        /// </summary>
        public const int MaxProviderFailureMessageCharacters = 240;

        /// <summary>
        /// The OpenCode event types.
        /// </summary>
        public static readonly IReadOnlySet<string> KnownTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            TypeText, TypeReasoning, TypeToolUse, TypeStepStart, TypeStepFinish, TypeError
        };

        /// <summary>
        /// Event type.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Timestamp in milliseconds since the epoch, or null.
        /// </summary>
        [JsonPropertyName("timestamp")]
        public long? Timestamp { get; set; } = null;

        /// <summary>
        /// OpenCode session identifier, or null.
        /// </summary>
        [JsonPropertyName("sessionID")]
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// The event's part, or null.
        /// </summary>
        [JsonPropertyName("part")]
        public OpenCodePart? Part { get; set; } = null;

        /// <summary>
        /// Error payload of an error event as raw JSON, or null.
        /// </summary>
        [JsonPropertyName("error")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        public string? Error { get; set; } = null;

        /// <summary>
        /// Assistant reply text of a text event, or null for any other event (reasoning is never reply text).
        /// </summary>
        [JsonIgnore]
        public string? AssistantText
        {
            get
            {
                if (!String.Equals(Type, TypeText, StringComparison.Ordinal)) return null;
                return String.IsNullOrEmpty(Part?.Text) ? null : Part!.Text;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse one output line. Returns false for plain text, malformed JSON, and JSON without a known OpenCode type.
        /// </summary>
        /// <param name="line">Raw line.</param>
        /// <param name="value">The event, or null.</param>
        /// <returns>True when the line is an OpenCode protocol event.</returns>
        public static bool TryParse(string? line, out OpenCodeStreamEvent? value)
        {
            if (RuntimeStreamJson.TryDeserializeLine<OpenCodeStreamEvent>(line, out value)
                && value!.Type != null && KnownTypes.Contains(value.Type))
                return true;

            value = null;
            return false;
        }

        /// <summary>
        /// Operator text for an error event. Uses <c>data.message</c> when it is present, otherwise the
        /// top-level message, and appends the numeric status when one is present. Returns null when this
        /// event is not an error. The text omits the session id, response body, headers, URL, and the rest
        /// of the raw payload.
        /// </summary>
        /// <returns>The bounded failure text, or null.</returns>
        public string? DescribeProviderFailure()
        {
            string? message = ProviderFailureMessage();
            if (message == null) return null;
            int? status = ProviderFailureStatus();
            if (status.HasValue) return message + " (status " + status.Value.ToString() + ")";
            return message;
        }

        /// <summary>
        /// The bounded provider message of an error event (<c>data.message</c>, otherwise the top-level message), a
        /// generic text when the payload names none, or null when this event is not an error.
        /// </summary>
        /// <returns>The message, or null.</returns>
        public string? ProviderFailureMessage()
        {
            if (!String.Equals(Type, TypeError, StringComparison.Ordinal)) return null;
            string message = "OpenCode provider request failed";
            OpenCodeProviderErrorDocument? document = ReadErrorDocument();
            if (document != null)
            {
                string? raw = !String.IsNullOrWhiteSpace(document.Data?.Message) ? document.Data!.Message : document.Message;
                if (!String.IsNullOrWhiteSpace(raw)) message = raw!.Trim();
            }

            if (message.Length > MaxProviderFailureMessageCharacters)
                message = message.Substring(0, MaxProviderFailureMessageCharacters);
            return message;
        }

        /// <summary>
        /// The HTTP status of an error event (<c>data.statusCode</c>, otherwise the top-level <c>statusCode</c>), or null.
        /// </summary>
        /// <returns>The status, or null.</returns>
        public int? ProviderFailureStatus()
        {
            if (!String.Equals(Type, TypeError, StringComparison.Ordinal)) return null;
            OpenCodeProviderErrorDocument? document = ReadErrorDocument();
            if (document == null) return null;
            if (document.Data != null && document.Data.StatusCode.HasValue) return document.Data.StatusCode;
            return document.StatusCode;
        }

        /// <summary>
        /// The error's own kind (<c>type</c> in OpenCode 2, <c>name</c> before it, for example <c>APIError</c>), or null.
        /// </summary>
        /// <returns>The kind, or null.</returns>
        public string? ProviderFailureKind()
        {
            if (!String.Equals(Type, TypeError, StringComparison.Ordinal)) return null;
            OpenCodeProviderErrorDocument? document = ReadErrorDocument();
            if (document == null) return null;
            string? kind = !String.IsNullOrWhiteSpace(document.Type) ? document.Type : document.Name;
            return String.IsNullOrWhiteSpace(kind) ? null : kind!.Trim();
        }

        #endregion

        #region Private-Methods

        private OpenCodeProviderErrorDocument? ReadErrorDocument()
        {
            if (String.IsNullOrWhiteSpace(Error)) return null;
            try
            {
                return JsonSerializer.Deserialize<OpenCodeProviderErrorDocument>(Error!);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion

        #region Private-Members

        private sealed class OpenCodeProviderErrorDocument
        {
            [JsonPropertyName("type")]
            public string? Type { get; set; } = null;

            [JsonPropertyName("name")]
            public string? Name { get; set; } = null;

            [JsonPropertyName("message")]
            public string? Message { get; set; } = null;

            [JsonPropertyName("statusCode")]
            public int? StatusCode { get; set; } = null;

            [JsonPropertyName("data")]
            public OpenCodeProviderErrorData? Data { get; set; } = null;
        }

        private sealed class OpenCodeProviderErrorData
        {
            [JsonPropertyName("message")]
            public string? Message { get; set; } = null;

            [JsonPropertyName("statusCode")]
            public int? StatusCode { get; set; } = null;
        }

        #endregion
    }
}
