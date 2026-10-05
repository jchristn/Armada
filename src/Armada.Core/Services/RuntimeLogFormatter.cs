namespace Armada.Core.Services
{
    using System;
    using System.Text.RegularExpressions;
    using Armada.Core.Protocol;

    /// <summary>
    /// Turns a raw captain-runtime log line into something readable: resolves the real tool name out of a
    /// runtime's nested JSON event, redacts secret-shaped values, truncates oversized payloads with a
    /// marker, and drops known-noise lines. Pure (no I/O) so it can be unit tested against captured
    /// fixtures and slotted into the runtime output handlers and the log read-back paths without changing
    /// where logs are stored.
    /// </summary>
    public static class RuntimeLogFormatter
    {
        #region Private-Members

        private const int _MaxLineChars = 2000;

        // Lines that are pure noise and should not clutter the readable view.
        private static readonly Regex _NoiseLine = new Regex(@"^\s*(\[dotnet\]|Determining projects to restore|Restored\s|MSBuild version|Welcome to \.NET)", RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Format one raw log line for a given runtime.
        /// </summary>
        /// <param name="rawLine">The raw line as captured from the runtime's stdout/stderr.</param>
        /// <param name="runtime">The runtime that produced the line (informational; JSONL handling is shared).</param>
        /// <returns>The formatted line; never null.</returns>
        public static FormattedLogLine Format(string? rawLine, Armada.Core.Enums.AgentRuntimeEnum runtime)
        {
            FormattedLogLine result = new FormattedLogLine();

            if (rawLine == null) { result.Dropped = true; return result; }

            string line = rawLine.TrimEnd('\r', '\n');
            if (String.IsNullOrWhiteSpace(line)) { result.Dropped = true; return result; }
            if (_NoiseLine.IsMatch(line)) { result.Dropped = true; return result; }

            // Structured JSONL event: resolve a readable tool-call summary, trying the runtime's own event
            // shape first (Claude Code / Codex stream-json) and falling back to the shared Mux/OpenCode shape.
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("{", StringComparison.Ordinal)
                && (TryFormatRuntimeSpecificEvent(trimmed, runtime, result) || TryFormatJsonEvent(trimmed, result)))
            {
                ApplyRedactionAndTruncation(result);
                return result;
            }

            result.Text = line;
            ApplyRedactionAndTruncation(result);
            return result;
        }

        #endregion

        #region Private-Methods

        private static bool TryFormatRuntimeSpecificEvent(string json, Armada.Core.Enums.AgentRuntimeEnum runtime, FormattedLogLine result)
        {
            // Each runtime's own typed event shape: Claude Code stream-json, Codex exec --json, OpenCode --format json.
            // Other runtimes use the shared Mux eventType shape (TryFormatMuxEvent).
            switch (runtime)
            {
                case Armada.Core.Enums.AgentRuntimeEnum.ClaudeCode:
                    return TryFormatClaudeEvent(json, result);
                case Armada.Core.Enums.AgentRuntimeEnum.Codex:
                    return TryFormatCodexEvent(json, result) || TryFormatClaudeEvent(json, result);
                case Armada.Core.Enums.AgentRuntimeEnum.OpenCode:
                    return TryFormatOpenCodeEvent(json, result);
                default:
                    return false;
            }
        }

        private static bool TryFormatClaudeEvent(string json, FormattedLogLine result)
        {
            if (!ClaudeStreamLine.TryParse(json, out ClaudeStreamLine? evt) || evt == null) return false;

            // A bare tool_use event.
            if (evt.Type == ClaudeStreamLine.TypeToolUse && !String.IsNullOrEmpty(evt.Name))
            {
                SetToolCall(result, evt.Name, "-> tool " + evt.Name);
                return true;
            }

            // An assistant message carrying content blocks, one of which may be a tool_use.
            if (evt.Message?.Content != null)
            {
                foreach (ClaudeStreamContentBlock block in evt.Message.Content)
                {
                    if (block != null && block.Type == ClaudeStreamContentBlock.TypeToolUse && !String.IsNullOrEmpty(block.Name))
                    {
                        SetToolCall(result, block.Name, "-> tool " + block.Name);
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryFormatCodexEvent(string json, FormattedLogLine result)
        {
            if (!CodexStreamEvent.TryParse(json, out CodexStreamEvent? evt) || evt == null) return false;

            CodexStreamItem? item = evt.Item;
            if (item == null)
            {
                if (evt.Type == CodexStreamEvent.TypeError || evt.Type == CodexStreamEvent.TypeTurnFailed)
                {
                    result.Text = "(error) " + (evt.Message ?? evt.Error ?? String.Empty);
                    return true;
                }

                // thread/turn lifecycle markers carry no display text.
                result.Dropped = true;
                return true;
            }

            switch (item.Type)
            {
                case CodexStreamItem.TypeMcpToolCall:
                    {
                        string name = String.IsNullOrEmpty(item.Server) ? (item.Tool ?? "unknown") : item.Server + "." + (item.Tool ?? "unknown");
                        SetToolCall(result, name, "-> tool " + name + (String.IsNullOrEmpty(item.Status) ? "" : " (" + item.Status + ")"));
                        return true;
                    }
                case CodexStreamItem.TypeCommandExecution:
                    SetToolCall(result, "shell", "-> tool shell " + (item.Command ?? String.Empty) + (item.ExitCode.HasValue ? " (exit " + item.ExitCode.Value + ")" : ""));
                    return true;
                case CodexStreamItem.TypeAgentMessage:
                    if (evt.Type != CodexStreamEvent.TypeItemCompleted || String.IsNullOrEmpty(item.Text)) { result.Dropped = true; return true; }
                    result.Text = item.Text!;
                    return true;
                case CodexStreamItem.TypeReasoning:
                    if (evt.Type != CodexStreamEvent.TypeItemCompleted || String.IsNullOrEmpty(item.Text)) { result.Dropped = true; return true; }
                    result.Text = "(thinking) " + item.Text;
                    return true;
                default:
                    result.Dropped = true;
                    return true;
            }
        }

        private static bool TryFormatOpenCodeEvent(string json, FormattedLogLine result)
        {
            if (!OpenCodeStreamEvent.TryParse(json, out OpenCodeStreamEvent? evt) || evt == null) return false;

            OpenCodePart? part = evt.Part;
            switch (evt.Type)
            {
                case OpenCodeStreamEvent.TypeToolUse:
                    if (part == null || String.IsNullOrEmpty(part.Tool)) return false;
                    {
                        string? status = part.State?.Status;
                        SetToolCall(result, part.Tool, "-> tool " + part.Tool + (String.IsNullOrEmpty(status) ? "" : " (" + status + ")"));
                        return true;
                    }
                case OpenCodeStreamEvent.TypeText:
                case OpenCodeStreamEvent.TypeReasoning:
                    if (part?.Text == null) return false;
                    result.Text = (evt.Type == OpenCodeStreamEvent.TypeReasoning ? "(thinking) " : "") + part.Text;
                    return true;
                case OpenCodeStreamEvent.TypeError:
                    result.Text = "(error) " + (evt.Error ?? String.Empty);
                    return true;
                default:
                    // A step marker with no display text: drop it rather than echo raw JSON.
                    result.Dropped = true;
                    return true;
            }
        }

        private static bool TryFormatJsonEvent(string json, FormattedLogLine result)
        {
            if (!MuxProtocolEvent.TryParse(json, out MuxProtocolEvent? evt) || evt == null) return false;

            switch (evt.EventType)
            {
                case MuxProtocolEvent.ToolCallProposed:
                    if (evt.ToolCall == null) return false;
                    SetToolCall(result, evt.ToolCall.Name, "-> tool " + (evt.ToolCall.Name ?? "unknown"));
                    return true;
                case MuxProtocolEvent.ToolCallCompleted:
                    {
                        bool ok = evt.Result?.Success ?? true;
                        SetToolCall(result, evt.ToolName, "<- tool " + (evt.ToolName ?? "unknown") + (ok ? " ok" : " failed"));
                        return true;
                    }
                case MuxProtocolEvent.AssistantText:
                    if (evt.Text == null) return false;
                    result.Text = evt.Text;
                    return true;
                default:
                    return false;
            }
        }

        private static void SetToolCall(FormattedLogLine result, string? toolName, string text)
        {
            result.IsToolCall = true;
            result.ToolName = toolName;
            result.Text = text;
        }

        private static void ApplyRedactionAndTruncation(FormattedLogLine result)
        {
            // Shared "secret-shaped value" definition, used by request-history capture too.
            string text = SecretRedactor.Redact(result.Text, out bool redacted);
            if (redacted) result.Redacted = true;

            if (text.Length > _MaxLineChars)
            {
                text = text.Substring(0, _MaxLineChars) + " ... [truncated " + (text.Length - _MaxLineChars) + " chars]";
                result.Truncated = true;
            }

            result.Text = text;
        }

        #endregion
    }
}
