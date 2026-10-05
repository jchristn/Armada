namespace Armada.Core.Protocol
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One JSONL protocol event of the Mux CLI (<c>--output-format jsonl</c>), discriminated by <c>eventType</c>. Only
    /// lines whose <c>eventType</c> is one of Mux's event types (<see cref="KnownEventTypes"/>) are treated as protocol
    /// events; any other line (including JSON a model printed) is reply text.
    /// </summary>
    public class MuxProtocolEvent
    {
        #region Public-Members

        /// <summary>run_started.</summary>
        public const string RunStarted = "run_started";

        /// <summary>assistant_text (a streamed reply delta).</summary>
        public const string AssistantText = "assistant_text";

        /// <summary>assistant_thinking (a streamed reasoning delta).</summary>
        public const string AssistantThinking = "assistant_thinking";

        /// <summary>tool_call_proposed.</summary>
        public const string ToolCallProposed = "tool_call_proposed";

        /// <summary>tool_call_approved.</summary>
        public const string ToolCallApproved = "tool_call_approved";

        /// <summary>tool_call_completed.</summary>
        public const string ToolCallCompleted = "tool_call_completed";

        /// <summary>error.</summary>
        public const string Error = "error";

        /// <summary>heartbeat.</summary>
        public const string Heartbeat = "heartbeat";

        /// <summary>context_status.</summary>
        public const string ContextStatus = "context_status";

        /// <summary>context_compacted.</summary>
        public const string ContextCompacted = "context_compacted";

        /// <summary>run_completed.</summary>
        public const string RunCompleted = "run_completed";

        /// <summary>task_plan_updated.</summary>
        public const string TaskPlanUpdated = "task_plan_updated";

        /// <summary>
        /// The Mux event types (Mux.Core AgentEventTypeEnum).
        /// </summary>
        public static readonly IReadOnlySet<string> KnownEventTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            RunStarted, AssistantText, AssistantThinking, ToolCallProposed, ToolCallApproved, ToolCallCompleted,
            Error, Heartbeat, ContextStatus, ContextCompacted, RunCompleted, TaskPlanUpdated
        };

        /// <summary>
        /// Contract version, or null.
        /// </summary>
        [JsonPropertyName("contractVersion")]
        public int? ContractVersion { get; set; } = null;

        /// <summary>
        /// Event type.
        /// </summary>
        [JsonPropertyName("eventType")]
        public string? EventType { get; set; } = null;

        /// <summary>
        /// Model (run_started, error), or null.
        /// </summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; } = null;

        /// <summary>
        /// Text delta (assistant_text, assistant_thinking), or null.
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; } = null;

        /// <summary>
        /// Proposed tool call (tool_call_proposed), or null.
        /// </summary>
        [JsonPropertyName("toolCall")]
        public MuxToolCall? ToolCall { get; set; } = null;

        /// <summary>
        /// Tool call identifier (tool_call_approved, tool_call_completed), or null.
        /// </summary>
        [JsonPropertyName("toolCallId")]
        public string? ToolCallId { get; set; } = null;

        /// <summary>
        /// Tool name (tool_call_completed), or null.
        /// </summary>
        [JsonPropertyName("toolName")]
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// Elapsed milliseconds (tool_call_completed), or null.
        /// </summary>
        [JsonPropertyName("elapsedMs")]
        public double? ElapsedMs { get; set; } = null;

        /// <summary>
        /// Tool result (tool_call_completed), or null.
        /// </summary>
        [JsonPropertyName("result")]
        public MuxToolResult? Result { get; set; } = null;

        /// <summary>
        /// Run status (run_completed), or null.
        /// </summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; } = null;

        /// <summary>
        /// Run duration in milliseconds (run_completed), or null.
        /// </summary>
        [JsonPropertyName("durationMs")]
        public double? DurationMs { get; set; } = null;

        /// <summary>
        /// Estimated whole-context tokens at the end of the run (run_completed), or null.
        /// </summary>
        [JsonPropertyName("finalEstimatedTokens")]
        public long? FinalEstimatedTokens { get; set; } = null;

        /// <summary>
        /// Iterations completed (run_completed), or null.
        /// </summary>
        [JsonPropertyName("iterationsCompleted")]
        public int? IterationsCompleted { get; set; } = null;

        /// <summary>
        /// Tool call count (run_completed), or null.
        /// </summary>
        [JsonPropertyName("toolCallCount")]
        public int? ToolCallCount { get; set; } = null;

        /// <summary>
        /// Error code (error), or null.
        /// </summary>
        [JsonPropertyName("code")]
        public string? Code { get; set; } = null;

        /// <summary>
        /// Error message (error), or null.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse one output line. Returns false for plain text, malformed JSON, and JSON without a known Mux event type.
        /// </summary>
        /// <param name="line">Raw line.</param>
        /// <param name="value">The event, or null.</param>
        /// <returns>True when the line is a Mux protocol event.</returns>
        public static bool TryParse(string? line, out MuxProtocolEvent? value)
        {
            if (RuntimeStreamJson.TryDeserializeLine<MuxProtocolEvent>(line, out value)
                && value!.EventType != null && KnownEventTypes.Contains(value.EventType))
                return true;

            value = null;
            return false;
        }

        #endregion
    }
}
