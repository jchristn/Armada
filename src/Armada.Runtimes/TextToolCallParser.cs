namespace Armada.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using PolyPrompt.Models;

    /// <summary>
    /// Recovers tool calls that an open model wrote into its visible text instead of the native tool_calls field
    /// (RELIABILITY_SIMPLICITY_PLAN.md RS-7.2). Three formats are read: Hermes-style <c>&lt;tool_call&gt;{json}&lt;/tool_call&gt;</c>;
    /// function tags <c>&lt;function=name&gt;...&lt;/function&gt;</c> whose body is a JSON object or
    /// <c>&lt;parameter=x&gt;value&lt;/parameter&gt;</c> pairs, also inside a tool_call block; and fenced JSON blocks holding
    /// a call object or an array of them. Only calls to permitted tools are recovered, and a fenced block counts only when
    /// it names a permitted tool and carries arguments, so a JSON example in prose is left alone. Stateless and thread-safe.
    /// </summary>
    public static class TextToolCallParser
    {
        #region Public-Members

        /// <summary>Format name for &lt;tool_call&gt; blocks.</summary>
        public const string HermesFormat = "hermes";

        /// <summary>Format name for &lt;function=name&gt; tags.</summary>
        public const string FunctionTagFormat = "function_tag";

        /// <summary>Format name for fenced JSON blocks.</summary>
        public const string FencedJsonFormat = "fenced_json";

        /// <summary>Most calls recovered from one response.</summary>
        public const int MaxCalls = 16;

        #endregion

        #region Private-Members

        private static readonly Regex _ToolCallBlock = new Regex(@"<tool_call>\s*(?<body>.*?)\s*</tool_call>", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _FunctionTag = new Regex(@"<function=(?<name>[^>\s]+)\s*>(?<body>.*?)</function>", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _Parameter = new Regex(@"<parameter=(?<name>[^>\s]+)\s*>(?<value>.*?)</parameter>", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _Fence = new Regex(@"```(?:json|tool_call|tool)?[ \t]*\r?\n(?<body>.*?)\r?\n[ \t]*```", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _Integer = new Regex(@"^-?\d{1,15}$", RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Recover the tool calls written in text.
        /// </summary>
        /// <param name="text">The model's visible text (reasoning already removed), or null.</param>
        /// <param name="permittedTools">Tool names the captain may call; calls to other names are not recovered.</param>
        /// <returns>The recovered calls and the remaining text; no calls when nothing matched.</returns>
        /// <exception cref="ArgumentNullException">Thrown when permittedTools is null.</exception>
        public static TextToolCallParseResult Parse(string? text, ISet<string> permittedTools)
        {
            if (permittedTools == null) throw new ArgumentNullException(nameof(permittedTools));
            TextToolCallParseResult result = new TextToolCallParseResult { RemainingText = (text ?? String.Empty).Trim() };
            if (String.IsNullOrWhiteSpace(text)) return result;

            // Matched spans, start index to length.
            SortedDictionary<int, int> spans = new SortedDictionary<int, int>();
            List<ToolCall> calls = new List<ToolCall>();

            foreach (Match block in _ToolCallBlock.Matches(text))
            {
                string body = block.Groups["body"].Value;
                List<ToolCall> found = new List<ToolCall>();
                if (body.StartsWith("<function=", StringComparison.Ordinal))
                {
                    foreach (Match tag in _FunctionTag.Matches(body))
                    {
                        ToolCall? call = FromFunctionTag(tag);
                        if (call != null && permittedTools.Contains(call.Name)) found.Add(call);
                    }
                }
                else found.AddRange(FromJson(body, permittedTools, false));
                if (found.Count == 0) continue;
                calls.AddRange(found);
                spans[block.Index] = block.Length;
                result.Format ??= HermesFormat;
            }

            foreach (Match tag in _FunctionTag.Matches(text))
            {
                if (Inside(spans, tag.Index)) continue;
                ToolCall? call = FromFunctionTag(tag);
                if (call == null || !permittedTools.Contains(call.Name)) continue;
                calls.Add(call);
                spans[tag.Index] = tag.Length;
                result.Format ??= FunctionTagFormat;
            }

            if (calls.Count == 0)
            {
                foreach (Match fence in _Fence.Matches(text))
                {
                    List<ToolCall> found = FromJson(fence.Groups["body"].Value, permittedTools, true);
                    if (found.Count == 0) continue;
                    calls.AddRange(found);
                    spans[fence.Index] = fence.Length;
                    result.Format ??= FencedJsonFormat;
                }
            }

            if (calls.Count == 0) return result;
            if (calls.Count > MaxCalls) calls = calls.GetRange(0, MaxCalls);
            foreach (ToolCall call in calls) call.Id = "text_call_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            result.Calls = calls;
            result.RemainingText = Remove(text, spans).Trim();
            return result;
        }

        #endregion

        #region Private-Methods

        private static ToolCall? FromFunctionTag(Match tag)
        {
            string name = tag.Groups["name"].Value.Trim();
            if (name.Length == 0) return null;
            string body = tag.Groups["body"].Value;
            string trimmed = body.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                string? args = ObjectJson(trimmed);
                return args == null ? null : new ToolCall { Name = name, ArgumentsJson = args };
            }

            Dictionary<string, object?> values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (Match parameter in _Parameter.Matches(body)) values[parameter.Groups["name"].Value] = ParameterValue(parameter.Groups["value"].Value);
            if (values.Count == 0 && trimmed.Length > 0) return null;
            return new ToolCall { Name = name, ArgumentsJson = JsonSerializer.Serialize(values) };
        }

        private static object? ParameterValue(string raw)
        {
            // The value sits on its own lines between the tags; drop only that one leading and trailing line break so
            // file content keeps its own whitespace.
            string value = raw;
            if (value.StartsWith("\r\n", StringComparison.Ordinal)) value = value.Substring(2);
            else if (value.StartsWith("\n", StringComparison.Ordinal)) value = value.Substring(1);
            if (value.EndsWith("\r\n", StringComparison.Ordinal)) value = value.Substring(0, value.Length - 2);
            else if (value.EndsWith("\n", StringComparison.Ordinal)) value = value.Substring(0, value.Length - 1);

            string probe = value.Trim();
            if (probe == "true") return true;
            if (probe == "false") return false;
            if (_Integer.IsMatch(probe)) return Int64.Parse(probe, CultureInfo.InvariantCulture);
            if ((probe.StartsWith("{", StringComparison.Ordinal) && probe.EndsWith("}", StringComparison.Ordinal))
                || (probe.StartsWith("[", StringComparison.Ordinal) && probe.EndsWith("]", StringComparison.Ordinal)))
            {
                try { return JsonSerializer.Deserialize<object>(probe); }
                catch (JsonException) { }
            }
            return value;
        }

        private static List<ToolCall> FromJson(string body, ISet<string> permittedTools, bool requireArguments)
        {
            List<ToolCall> calls = new List<ToolCall>();
            string trimmed = body.Trim();
            List<TextToolCallPayload?>? payloads = null;
            try
            {
                if (trimmed.StartsWith("[", StringComparison.Ordinal)) payloads = JsonSerializer.Deserialize<List<TextToolCallPayload?>>(trimmed);
                else if (trimmed.StartsWith("{", StringComparison.Ordinal))
                {
                    TextToolCallPayload? single = JsonSerializer.Deserialize<TextToolCallPayload>(trimmed);
                    if (single != null) payloads = new List<TextToolCallPayload?> { single };
                }
            }
            catch (JsonException)
            {
                return calls;
            }
            if (payloads == null) return calls;

            foreach (TextToolCallPayload? outer in payloads)
            {
                if (outer == null) continue;
                TextToolCallPayload payload = outer.Function ?? outer;
                string? name = payload.Name?.Trim();
                if (String.IsNullOrEmpty(name) || !permittedTools.Contains(name)) continue;
                object? arguments = payload.Arguments ?? payload.Parameters;
                if (arguments == null && requireArguments) continue;
                string? args = arguments == null ? "{}" : ArgumentsJson(arguments);
                if (args == null) continue;
                calls.Add(new ToolCall { Name = name, ArgumentsJson = args });
            }
            return calls;
        }

        private static string? ArgumentsJson(object arguments)
        {
            string raw = JsonSerializer.Serialize(arguments);
            if (raw.StartsWith("{", StringComparison.Ordinal)) return raw;
            if (!raw.StartsWith("\"", StringComparison.Ordinal)) return null;
            string? inner = JsonSerializer.Deserialize<string>(raw);
            return inner == null ? null : ObjectJson(inner.Trim());
        }

        private static string? ObjectJson(string candidate)
        {
            if (!candidate.StartsWith("{", StringComparison.Ordinal)) return null;
            try
            {
                Dictionary<string, object?>? parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(candidate);
                return parsed == null ? null : JsonSerializer.Serialize(parsed);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool Inside(SortedDictionary<int, int> spans, int index)
        {
            foreach (KeyValuePair<int, int> span in spans) if (index >= span.Key && index < span.Key + span.Value) return true;
            return false;
        }

        private static string Remove(string text, SortedDictionary<int, int> spans)
        {
            StringBuilder sb = new StringBuilder();
            int cursor = 0;
            foreach (KeyValuePair<int, int> span in spans)
            {
                if (span.Key < cursor) continue;
                sb.Append(text, cursor, span.Key - cursor);
                cursor = span.Key + span.Value;
            }
            if (cursor < text.Length) sb.Append(text, cursor, text.Length - cursor);
            return sb.ToString();
        }

        #endregion
    }
}
