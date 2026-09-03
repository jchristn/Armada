namespace Armada.Core.Services
{
    using System;
    using System.Text.Json;
    using System.Text.RegularExpressions;

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
            // Claude Code / Codex stream a "type"-tagged event with tool_use content blocks; other runtimes
            // use the shared eventType shape handled by TryFormatJsonEvent.
            if (runtime != Armada.Core.Enums.AgentRuntimeEnum.ClaudeCode && runtime != Armada.Core.Enums.AgentRuntimeEnum.Codex)
                return false;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;

                string type = root.TryGetProperty("type", out JsonElement ty) && ty.ValueKind == JsonValueKind.String ? ty.GetString() ?? "" : "";

                // A bare tool_use event.
                if (type == "tool_use" && root.TryGetProperty("name", out JsonElement directName) && directName.ValueKind == JsonValueKind.String)
                {
                    result.IsToolCall = true;
                    result.ToolName = directName.GetString();
                    result.Text = "-> tool " + (result.ToolName ?? "unknown");
                    return true;
                }

                // An assistant message carrying content blocks, one of which may be a tool_use.
                JsonElement contentHolder = root;
                if (root.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.Object)
                    contentHolder = message;

                if (contentHolder.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement block in content.EnumerateArray())
                    {
                        if (block.ValueKind != JsonValueKind.Object) continue;
                        string blockType = block.TryGetProperty("type", out JsonElement bt) && bt.ValueKind == JsonValueKind.String ? bt.GetString() ?? "" : "";
                        if (blockType == "tool_use" && block.TryGetProperty("name", out JsonElement bn) && bn.ValueKind == JsonValueKind.String)
                        {
                            result.IsToolCall = true;
                            result.ToolName = bn.GetString();
                            result.Text = "-> tool " + (result.ToolName ?? "unknown");
                            return true;
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }

            return false;
        }

        private static bool TryFormatJsonEvent(string json, FormattedLogLine result)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;

                string eventType = root.TryGetProperty("eventType", out JsonElement et) && et.ValueKind == JsonValueKind.String
                    ? et.GetString() ?? "" : "";

                if (eventType == "tool_call_proposed" && root.TryGetProperty("toolCall", out JsonElement tc))
                {
                    string? name = tc.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    result.IsToolCall = true;
                    result.ToolName = name;
                    result.Text = "-> tool " + (name ?? "unknown");
                    return true;
                }

                if (eventType == "tool_call_completed")
                {
                    string? name = root.TryGetProperty("toolName", out JsonElement tn) && tn.ValueKind == JsonValueKind.String ? tn.GetString() : null;
                    bool ok = true;
                    if (root.TryGetProperty("result", out JsonElement res) && res.TryGetProperty("success", out JsonElement suc)
                        && (suc.ValueKind == JsonValueKind.True || suc.ValueKind == JsonValueKind.False))
                        ok = suc.GetBoolean();
                    result.IsToolCall = true;
                    result.ToolName = name;
                    result.Text = "<- tool " + (name ?? "unknown") + (ok ? " ok" : " failed");
                    return true;
                }

                if (eventType == "assistant_text" && root.TryGetProperty("text", out JsonElement txt) && txt.ValueKind == JsonValueKind.String)
                {
                    result.Text = txt.GetString() ?? "";
                    return true;
                }
            }
            catch (JsonException)
            {
            }

            return false;
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
