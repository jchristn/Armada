namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Protocol;

    /// <summary>
    /// Reads a captain's current activity from its runtime's typed stream events (the same <see cref="ClaudeStreamLine"/>
    /// and <see cref="CodexStreamEvent"/> models Ask turns parse): a tool call it started, with the tool's name and short
    /// input (the command for a shell call, the path for a file edit), text it wrote, or reasoning. Pure; never reads free
    /// text for meaning.
    /// </summary>
    public static class RuntimeActivityParser
    {
        #region Public-Members

        /// <summary>
        /// Longest <see cref="RuntimeActivity.Detail"/> kept, in characters.
        /// </summary>
        public const int MaxDetailChars = 200;

        #endregion

        #region Private-Members

        private const string _McpToolPrefix = "mcp__";
        private const string _McpSeparator = "__";
        private const string _Ellipsis = "...";

        #endregion

        #region Public-Methods

        /// <summary>
        /// The activities of one Claude Code stream-json event, in order: one per text, thinking, and tool_use block of an
        /// assistant message, or the tool of a bare tool_use event. Empty for every other event.
        /// </summary>
        /// <param name="line">Parsed event.</param>
        /// <param name="nowUtc">When it was observed, UTC.</param>
        /// <returns>Activities, possibly empty.</returns>
        public static List<RuntimeActivity> FromClaude(ClaudeStreamLine line, DateTime nowUtc)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));
            List<RuntimeActivity> activities = new List<RuntimeActivity>();
            if (String.Equals(line.Type, ClaudeStreamLine.TypeAssistant, StringComparison.Ordinal) && line.Message?.Content != null)
            {
                foreach (ClaudeStreamContentBlock block in line.Message.Content)
                {
                    RuntimeActivity? activity = FromClaudeBlock(block, nowUtc);
                    if (activity != null) activities.Add(activity);
                }
            }
            else if (String.Equals(line.Type, ClaudeStreamLine.TypeToolUse, StringComparison.Ordinal) && !String.IsNullOrWhiteSpace(line.Name))
            {
                activities.Add(ClaudeTool(line.Name!, null, nowUtc));
            }

            return activities;
        }

        /// <summary>
        /// The activity of one content block of a Claude Code assistant message, or null for a block that is not text,
        /// thinking, or a tool call (or is empty).
        /// </summary>
        /// <param name="block">Content block.</param>
        /// <param name="nowUtc">When it was observed, UTC.</param>
        /// <returns>The activity, or null.</returns>
        public static RuntimeActivity? FromClaudeBlock(ClaudeStreamContentBlock block, DateTime nowUtc)
        {
            if (block == null) return null;
            if (String.Equals(block.Type, ClaudeStreamContentBlock.TypeToolUse, StringComparison.Ordinal))
            {
                if (String.IsNullOrWhiteSpace(block.Name)) return null;
                return ClaudeTool(block.Name!, block.Input, nowUtc);
            }

            if (String.Equals(block.Type, ClaudeStreamContentBlock.TypeText, StringComparison.Ordinal))
                return TextActivity(block.Text, nowUtc);

            if (String.Equals(block.Type, ClaudeStreamContentBlock.TypeThinking, StringComparison.Ordinal))
                return ThinkingActivity(block.Thinking, nowUtc);

            return null;
        }

        /// <summary>
        /// The activity of one Codex exec --json event: a command or MCP tool call when its item starts, a file change,
        /// reasoning, or an agent message when its item completes. Null for every other event.
        /// </summary>
        /// <param name="evt">Parsed event.</param>
        /// <param name="nowUtc">When it was observed, UTC.</param>
        /// <returns>The activity, or null.</returns>
        public static RuntimeActivity? FromCodex(CodexStreamEvent evt, DateTime nowUtc)
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));
            CodexStreamItem? item = evt.Item;
            if (item == null) return null;

            bool started = String.Equals(evt.Type, CodexStreamEvent.TypeItemStarted, StringComparison.Ordinal);
            bool completed = String.Equals(evt.Type, CodexStreamEvent.TypeItemCompleted, StringComparison.Ordinal);

            if (started && String.Equals(item.Type, CodexStreamItem.TypeCommandExecution, StringComparison.Ordinal))
            {
                string? command = Shorten(item.Command);
                return Tool("shell", null, command, command != null ? "Running " + command : "Running a command", nowUtc);
            }

            if (started && String.Equals(item.Type, CodexStreamItem.TypeMcpToolCall, StringComparison.Ordinal))
            {
                string tool = String.IsNullOrWhiteSpace(item.Tool) ? "an MCP tool" : item.Tool!.Trim();
                return Tool(String.IsNullOrWhiteSpace(item.Tool) ? null : item.Tool!.Trim(), null, Shorten(item.Server), "Calling tool " + tool, nowUtc);
            }

            if (completed && String.Equals(item.Type, CodexStreamItem.TypeFileChange, StringComparison.Ordinal))
                return Tool("file_change", null, null, "Editing files", nowUtc);

            if (completed && String.Equals(item.Type, CodexStreamItem.TypeReasoning, StringComparison.Ordinal))
                return ThinkingActivity(item.Text, nowUtc);

            if (completed && String.Equals(item.Type, CodexStreamItem.TypeAgentMessage, StringComparison.Ordinal))
                return TextActivity(item.Text, nowUtc);

            return null;
        }

        /// <summary>
        /// The first non-blank line of a text, trimmed and cut to <paramref name="maxChars"/> characters; an ellipsis
        /// marks a cut or a dropped remainder. Null when the text is blank.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="maxChars">Maximum length, at least 4.</param>
        /// <returns>The line, or null.</returns>
        public static string? FirstLine(string? text, int maxChars)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            if (maxChars < 4) maxChars = 4;
            string[] lines = text!.Replace("\r\n", "\n").Split('\n');
            string? first = null;
            bool more = false;
            foreach (string raw in lines)
            {
                string trimmed = raw.Trim();
                if (trimmed.Length == 0) continue;
                if (first == null) first = trimmed;
                else
                {
                    more = true;
                    break;
                }
            }

            if (first == null) return null;
            if (first.Length > maxChars) return first.Substring(0, maxChars - _Ellipsis.Length).TrimEnd() + _Ellipsis;
            return more ? first + " " + _Ellipsis : first;
        }

        #endregion

        #region Private-Methods

        private static RuntimeActivity ClaudeTool(string name, string? inputJson, DateTime nowUtc)
        {
            ClaudeToolInput input = ReadClaudeInput(inputJson);
            string trimmedName = name.Trim();
            string? command = Shorten(input.Command);
            string? description = Shorten(input.Description);
            string? filePath = Shorten(input.FilePath);

            switch (trimmedName)
            {
                case "Bash":
                    if (description != null && command != null) return Tool(trimmedName, description, command, description + ": " + command, nowUtc);
                    if (command != null) return Tool(trimmedName, description, command, "Running " + command, nowUtc);
                    return Tool(trimmedName, description, null, description ?? "Running a command", nowUtc);
                case "Read":
                    return Tool(trimmedName, null, filePath, filePath != null ? "Reading " + filePath : "Reading a file", nowUtc);
                case "Edit":
                case "MultiEdit":
                    return Tool(trimmedName, null, filePath, filePath != null ? "Editing " + filePath : "Editing a file", nowUtc);
                case "Write":
                    return Tool(trimmedName, null, filePath, filePath != null ? "Writing " + filePath : "Writing a file", nowUtc);
                case "NotebookEdit":
                    string? notebook = Shorten(input.NotebookPath);
                    return Tool(trimmedName, null, notebook, notebook != null ? "Editing " + notebook : "Editing a notebook", nowUtc);
                case "Grep":
                    string? grepPattern = Shorten(input.Pattern);
                    string? grepPath = Shorten(input.Path);
                    if (grepPattern == null) return Tool(trimmedName, null, null, "Searching the code", nowUtc);
                    return Tool(trimmedName, null, grepPattern, "Searching for " + grepPattern + (grepPath != null ? " in " + grepPath : String.Empty), nowUtc);
                case "Glob":
                    string? globPattern = Shorten(input.Pattern);
                    return Tool(trimmedName, null, globPattern, globPattern != null ? "Finding files " + globPattern : "Finding files", nowUtc);
                case "LS":
                    string? listed = Shorten(input.Path);
                    return Tool(trimmedName, null, listed, listed != null ? "Listing " + listed : "Listing a directory", nowUtc);
                case "WebFetch":
                    string? url = Shorten(input.Url);
                    return Tool(trimmedName, null, url, url != null ? "Fetching " + url : "Fetching a web page", nowUtc);
                case "WebSearch":
                    string? query = Shorten(input.Query);
                    return Tool(trimmedName, null, query, query != null ? "Searching the web for " + query : "Searching the web", nowUtc);
                case "Task":
                case "Agent":
                    return Tool(trimmedName, description, description, description != null ? "Delegating: " + description : "Delegating to a subagent", nowUtc);
                case "TodoWrite":
                    return Tool(trimmedName, null, null, "Updating its task list", nowUtc);
            }

            return Tool(trimmedName, description, command ?? filePath, "Calling tool " + DisplayToolName(trimmedName), nowUtc);
        }

        /// <summary>
        /// The tool part of an MCP tool name (mcp__server__tool), or the name itself for any other tool.
        /// </summary>
        private static string DisplayToolName(string name)
        {
            if (!name.StartsWith(_McpToolPrefix, StringComparison.Ordinal)) return name;
            string rest = name.Substring(_McpToolPrefix.Length);
            int separator = rest.IndexOf(_McpSeparator, StringComparison.Ordinal);
            if (separator < 0 || separator + _McpSeparator.Length >= rest.Length) return name;
            return rest.Substring(separator + _McpSeparator.Length);
        }

        private static ClaudeToolInput ReadClaudeInput(string? inputJson)
        {
            if (String.IsNullOrWhiteSpace(inputJson)) return new ClaudeToolInput();
            try
            {
                return JsonSerializer.Deserialize<ClaudeToolInput>(inputJson!) ?? new ClaudeToolInput();
            }
            catch (JsonException)
            {
                return new ClaudeToolInput();
            }
        }

        private static RuntimeActivity? TextActivity(string? text, DateTime nowUtc)
        {
            string? line = FirstLine(text, MaxDetailChars);
            if (line == null) return null;
            return new RuntimeActivity { Kind = RuntimeActivityKindEnum.Text, Detail = line, Summary = line, TimestampUtc = nowUtc };
        }

        private static RuntimeActivity ThinkingActivity(string? text, DateTime nowUtc)
        {
            string? line = FirstLine(text, MaxDetailChars);
            return new RuntimeActivity
            {
                Kind = RuntimeActivityKindEnum.Thinking,
                Detail = line,
                Summary = line != null ? "Thinking: " + line : "Thinking",
                TimestampUtc = nowUtc
            };
        }

        private static RuntimeActivity Tool(string? name, string? description, string? detail, string summary, DateTime nowUtc)
        {
            return new RuntimeActivity
            {
                Kind = RuntimeActivityKindEnum.ToolCall,
                ToolName = name,
                Description = description,
                Detail = detail,
                Summary = FirstLine(summary, MaxDetailChars * 2) ?? summary,
                TimestampUtc = nowUtc
            };
        }

        private static string? Shorten(string? value)
        {
            return FirstLine(value, MaxDetailChars);
        }

        #endregion
    }
}
