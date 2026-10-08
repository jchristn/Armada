namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Protocol;

    /// <summary>
    /// Records the telemetry of one captain chat turn (Ask Armada, captain chat, planning) as the runtime's output
    /// arrives, for every runtime and wherever the turn runs (on the Admiral host, or on a Harbor whose output streams
    /// back over the link line by line): time to the first output of any kind (reasoning, a tool call, or reply text),
    /// time to the first visible reply text, total time, and the token counts and cost the runtime reports. All
    /// durations are on the monotonic clock of the supplied <see cref="TimeProvider"/>, so a host that sleeps during the
    /// turn or a wall-clock step cannot skew them. <see cref="Build"/> turns the observations into
    /// <see cref="CaptainChatMetrics"/> through <see cref="ChatTurnMetricsBuilder"/>.
    /// </summary>
    /// <remarks>
    /// Usage per runtime: Claude Code's stream-json <c>result</c> event (input, output, cache-read and cache-creation
    /// tokens, and total_cost_usd); Codex's <c>turn.completed</c> usage (input including cached, cached input, output);
    /// OpenCode's <c>step_finish</c> parts (input, output, reasoning, cache read and write, cost; summed across steps).
    /// Mux, Gemini, Cursor, and the in-process API runtime report no per-turn usage, so their completion tokens are
    /// estimated from the reply. Input tokens always include cached tokens, and cached tokens are the cache-read
    /// subset, matching <see cref="TokenUsageCapture"/>. Thread safe: output callbacks may arrive on any thread.
    /// </remarks>
    public class ChatTurnTelemetryRecorder
    {
        #region Public-Members

        /// <summary>
        /// Time since the turn started, on the monotonic clock.
        /// </summary>
        public TimeSpan Elapsed
        {
            get { return _Time.GetElapsedTime(_Start); }
        }

        /// <summary>
        /// How long after the start the first output of any kind arrived, or null when nothing has.
        /// </summary>
        public TimeSpan? FirstOutputAfter
        {
            get { lock (_Lock) return _FirstOutput; }
        }

        /// <summary>
        /// How long after the start the first visible reply text arrived, or null when none has.
        /// </summary>
        public TimeSpan? FirstTextAfter
        {
            get { lock (_Lock) return _FirstText; }
        }

        /// <summary>
        /// Input tokens the runtime reported (cached included), or null.
        /// </summary>
        public long? InputTokens
        {
            get { lock (_Lock) return _InputTokens; }
        }

        /// <summary>
        /// Output tokens the runtime reported, or null.
        /// </summary>
        public long? OutputTokens
        {
            get { lock (_Lock) return _OutputTokens; }
        }

        /// <summary>
        /// Cache-read input tokens the runtime reported, or null.
        /// </summary>
        public long? CachedTokens
        {
            get { lock (_Lock) return _CachedTokens; }
        }

        /// <summary>
        /// Cost in US dollars the runtime reported, or null.
        /// </summary>
        public double? CostUsd
        {
            get { lock (_Lock) return _CostUsd; }
        }

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly TimeProvider _Time;
        private readonly long _Start;
        private TimeSpan? _FirstOutput = null;
        private TimeSpan? _FirstText = null;
        private long? _InputTokens = null;
        private long? _OutputTokens = null;
        private long? _CachedTokens = null;
        private double? _CostUsd = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start recording a turn now, on the system's monotonic clock.
        /// </summary>
        public ChatTurnTelemetryRecorder() : this(TimeProvider.System)
        {
        }

        /// <summary>
        /// Start recording a turn now, on the monotonic clock of <paramref name="time"/> (tests supply a manual clock).
        /// </summary>
        /// <param name="time">Time provider whose timestamps measure the turn.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="time"/> is null.</exception>
        public ChatTurnTelemetryRecorder(TimeProvider time)
        {
            _Time = time ?? throw new ArgumentNullException(nameof(time));
            _Start = _Time.GetTimestamp();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Note model output of any kind (reasoning, a tool call, reply text). Only the first call is kept.
        /// </summary>
        public void MarkOutput()
        {
            TimeSpan now = Elapsed;
            lock (_Lock)
            {
                if (_FirstOutput == null) _FirstOutput = now;
            }
        }

        /// <summary>
        /// Note visible reply text (also output). Only the first call is kept.
        /// </summary>
        public void MarkText()
        {
            TimeSpan now = Elapsed;
            lock (_Lock)
            {
                if (_FirstOutput == null) _FirstOutput = now;
                if (_FirstText == null) _FirstText = now;
            }
        }

        /// <summary>
        /// Add a usage report. Counts add up across reports (OpenCode reports each step); a null count leaves that
        /// count as it was.
        /// </summary>
        /// <param name="inputTokens">Input tokens, cached included, or null.</param>
        /// <param name="outputTokens">Output tokens, or null.</param>
        /// <param name="cachedTokens">Cache-read input tokens, or null.</param>
        /// <param name="costUsd">Cost in US dollars, or null.</param>
        public void AddUsage(long? inputTokens, long? outputTokens, long? cachedTokens, double? costUsd)
        {
            lock (_Lock)
            {
                if (inputTokens.HasValue) _InputTokens = (_InputTokens ?? 0) + Math.Max(0, inputTokens.Value);
                if (outputTokens.HasValue) _OutputTokens = (_OutputTokens ?? 0) + Math.Max(0, outputTokens.Value);
                if (cachedTokens.HasValue) _CachedTokens = (_CachedTokens ?? 0) + Math.Max(0, cachedTokens.Value);
                if (costUsd.HasValue) _CostUsd = (_CostUsd ?? 0) + Math.Max(0, costUsd.Value);
            }
        }

        /// <summary>
        /// Observe one stdout line of a runtime: its typed event when the line is one (Claude Code stream-json, Codex
        /// exec --json, OpenCode --format json, Mux protocol), otherwise plain reply text.
        /// </summary>
        /// <param name="runtime">The captain's runtime.</param>
        /// <param name="line">The stdout line.</param>
        /// <returns>True when the line was a typed event of the runtime; false when it was treated as plain text.</returns>
        public bool ObserveLine(AgentRuntimeEnum runtime, string? line)
        {
            if (runtime == AgentRuntimeEnum.ClaudeCode && ClaudeStreamLine.TryParse(line, out ClaudeStreamLine? claude) && claude != null)
            {
                ObserveClaude(claude);
                return true;
            }

            if (runtime == AgentRuntimeEnum.Codex && CodexStreamEvent.TryParse(line, out CodexStreamEvent? codex) && codex != null)
            {
                ObserveCodex(codex);
                return true;
            }

            if (runtime == AgentRuntimeEnum.OpenCode && OpenCodeStreamEvent.TryParse(line, out OpenCodeStreamEvent? openCode) && openCode != null)
            {
                ObserveOpenCode(openCode);
                return true;
            }

            if (runtime == AgentRuntimeEnum.Mux && MuxProtocolEvent.TryParse(line, out MuxProtocolEvent? mux) && mux != null)
            {
                ObserveMux(mux);
                return true;
            }

            ObservePlainText(line);
            return false;
        }

        /// <summary>
        /// Observe a Claude Code stream-json event: content blocks starting or streaming are output, text deltas (or a
        /// whole assistant message with text) are reply text, and the result event carries usage and cost.
        /// </summary>
        /// <param name="evt">The event.</param>
        public void ObserveClaude(ClaudeStreamLine evt)
        {
            if (evt == null) return;
            if (!String.IsNullOrEmpty(evt.TextDelta))
            {
                MarkText();
                return;
            }

            if (evt.Type == ClaudeStreamLine.TypeStreamEvent && evt.Event != null
                && (String.Equals(evt.Event.Type, "content_block_start", StringComparison.Ordinal)
                    || String.Equals(evt.Event.Type, "content_block_delta", StringComparison.Ordinal)))
            {
                MarkOutput();
                return;
            }

            if (evt.Type == ClaudeStreamLine.TypeAssistant && evt.Message?.Content != null)
            {
                bool any = false;
                bool text = false;
                foreach (ClaudeStreamContentBlock block in evt.Message.Content)
                {
                    if (block == null) continue;
                    any = true;
                    if (String.Equals(block.Type, ClaudeStreamContentBlock.TypeText, StringComparison.Ordinal) && !String.IsNullOrEmpty(block.Text)) text = true;
                }

                if (text) MarkText();
                else if (any) MarkOutput();
                return;
            }

            if (evt.Type == ClaudeStreamLine.TypeResult && evt.Usage != null)
            {
                ClaudeStreamUsage usage = evt.Usage;
                long? input = null;
                if (usage.InputTokens.HasValue || usage.CacheReadInputTokens.HasValue || usage.CacheCreationInputTokens.HasValue)
                    input = (usage.InputTokens ?? 0) + (usage.CacheReadInputTokens ?? 0) + (usage.CacheCreationInputTokens ?? 0);
                AddUsage(input, usage.OutputTokens, usage.CacheReadInputTokens, evt.TotalCostUsd);
            }
            else if (evt.Type == ClaudeStreamLine.TypeResult && evt.TotalCostUsd.HasValue)
            {
                AddUsage(null, null, null, evt.TotalCostUsd);
            }
        }

        /// <summary>
        /// Observe a Codex exec --json event: any item is output, a completed agent message is reply text, and
        /// turn.completed carries usage.
        /// </summary>
        /// <param name="evt">The event.</param>
        public void ObserveCodex(CodexStreamEvent evt)
        {
            if (evt == null) return;
            if (evt.Type == CodexStreamEvent.TypeItemStarted || evt.Type == CodexStreamEvent.TypeItemUpdated || evt.Type == CodexStreamEvent.TypeItemCompleted)
            {
                if (evt.Item != null
                    && String.Equals(evt.Item.Type, CodexStreamItem.TypeAgentMessage, StringComparison.Ordinal)
                    && !String.IsNullOrEmpty(evt.Item.Text))
                    MarkText();
                else if (evt.Item != null)
                    MarkOutput();
                return;
            }

            if (evt.Type == CodexStreamEvent.TypeTurnCompleted && evt.Usage != null)
                AddUsage(evt.Usage.InputTokens, evt.Usage.OutputTokens, evt.Usage.CachedInputTokens, null);
        }

        /// <summary>
        /// Observe an OpenCode --format json event: text is reply text, reasoning and tool use are output, and each
        /// step_finish carries that step's tokens and cost.
        /// </summary>
        /// <param name="evt">The event.</param>
        public void ObserveOpenCode(OpenCodeStreamEvent evt)
        {
            if (evt == null) return;
            if (!String.IsNullOrEmpty(evt.AssistantText))
            {
                MarkText();
                return;
            }

            if (evt.Type == OpenCodeStreamEvent.TypeReasoning || evt.Type == OpenCodeStreamEvent.TypeToolUse)
            {
                MarkOutput();
                return;
            }

            if (evt.Type == OpenCodeStreamEvent.TypeStepFinish && evt.Part != null)
            {
                OpenCodeTokens? tokens = evt.Part.Tokens;
                long? input = null;
                long? output = null;
                long? cached = null;
                if (tokens != null)
                {
                    if (tokens.Input.HasValue || tokens.Cache != null)
                        input = (tokens.Input ?? 0) + (tokens.Cache?.Read ?? 0) + (tokens.Cache?.Write ?? 0);
                    if (tokens.Output.HasValue || tokens.Reasoning.HasValue)
                        output = (tokens.Output ?? 0) + (tokens.Reasoning ?? 0);
                    cached = tokens.Cache?.Read;
                }

                AddUsage(input, output, cached, evt.Part.Cost);
            }
        }

        /// <summary>
        /// Observe a Mux protocol event: assistant text is reply text; thinking and tool calls are output. Mux reports
        /// only a whole-context token estimate, which is not the reply's size, so no usage is taken from it.
        /// </summary>
        /// <param name="evt">The event.</param>
        public void ObserveMux(MuxProtocolEvent evt)
        {
            if (evt == null) return;
            if (evt.EventType == MuxProtocolEvent.AssistantText && !String.IsNullOrEmpty(evt.Text))
                MarkText();
            else if (evt.EventType == MuxProtocolEvent.AssistantThinking || evt.EventType == MuxProtocolEvent.ToolCallProposed)
                MarkOutput();
        }

        /// <summary>
        /// Observe a plain stdout line (runtimes without a typed stream): a line with any text is reply text.
        /// </summary>
        /// <param name="line">The line.</param>
        public void ObservePlainText(string? line)
        {
            if (!String.IsNullOrWhiteSpace(line)) MarkText();
        }

        /// <summary>
        /// Build the turn's metrics now: total time, time to first output and to first text, streaming time, tokens
        /// (reported, or completion tokens estimated from <paramref name="reply"/>), tokens per second over the
        /// streaming window, cost, and the tool call count and total tool time.
        /// </summary>
        /// <param name="reply">The final reply text (used to estimate completion tokens when none were reported).</param>
        /// <param name="toolCalls">The turn's tool calls, or null when the caller does not track them.</param>
        /// <returns>The metrics.</returns>
        public CaptainChatMetrics Build(string? reply, IEnumerable<AskMessageToolCall>? toolCalls)
        {
            double totalMs = Elapsed.TotalMilliseconds;
            TimeSpan? firstOutput;
            TimeSpan? firstText;
            long? input;
            long? output;
            long? cached;
            double? cost;
            lock (_Lock)
            {
                firstOutput = _FirstOutput;
                firstText = _FirstText;
                input = _InputTokens;
                output = _OutputTokens;
                cached = _CachedTokens;
                cost = _CostUsd;
            }

            double? ttftMs = firstOutput.HasValue ? Math.Min(firstOutput.Value.TotalMilliseconds, totalMs) : (double?)null;
            CaptainChatMetrics metrics = ChatTurnMetricsBuilder.Build(totalMs, ttftMs, reply, output.HasValue ? ClampToInt(output.Value) : (int?)null);
            metrics.TimeToFirstTextMs = firstText.HasValue ? Math.Min(firstText.Value.TotalMilliseconds, totalMs) : (double?)null;
            metrics.PromptTokens = input.HasValue ? ClampToInt(input.Value) : (int?)null;
            metrics.CachedTokens = cached.HasValue ? ClampToInt(cached.Value) : (int?)null;
            metrics.CostUsd = cost;
            if (metrics.PromptTokens.HasValue && metrics.CompletionTokens.HasValue)
                metrics.TotalTokens = ClampToInt((long)metrics.PromptTokens.Value + metrics.CompletionTokens.Value);

            if (toolCalls != null)
            {
                int count = 0;
                double toolMs = 0;
                foreach (AskMessageToolCall call in toolCalls)
                {
                    if (call == null || !IsCompleted(call)) continue;
                    count++;
                    toolMs += call.ElapsedMs ?? 0;
                }

                metrics.ToolCallCount = count;
                metrics.ToolTimeMs = toolMs;
            }

            return metrics;
        }

        #endregion

        #region Private-Methods

        private static bool IsCompleted(AskMessageToolCall call)
        {
            return call.PermissionDenied == true || call.Ok.HasValue || call.ElapsedMs.HasValue || !String.IsNullOrEmpty(call.ResultText);
        }

        private static int ClampToInt(long value)
        {
            if (value > Int32.MaxValue) return Int32.MaxValue;
            if (value < 0) return 0;
            return (int)value;
        }

        #endregion
    }
}
