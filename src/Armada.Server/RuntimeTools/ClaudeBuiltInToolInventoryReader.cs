namespace Armada.Server.RuntimeTools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Best-effort, display-only inventory of Claude Code's built-in tools, scraped from the type declarations
    /// (<c>sdk-tools.d.ts</c>) shipped in a globally installed <c>@anthropic-ai/claude-code</c> npm package. The package
    /// ships no structured (JSON) tool manifest and the CLI has no command that lists its built-in tools without starting
    /// a model session, so the names are recovered from the <c>XxxInput</c> union members in that file. The result is
    /// shown in the captain tool viewer only; nothing in Armada branches on it. When the file is absent (for example a
    /// native, non-npm install) no inventory is reported.
    /// </summary>
    internal static class ClaudeBuiltInToolInventoryReader
    {
        #region Private-Members

        private const string _SourceName = "Built-In";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the inventory from the default global npm install location.
        /// </summary>
        /// <returns>The inventory, or null when the package file is absent or lists no tools.</returns>
        public static RuntimeBuiltInToolInventory? Read()
        {
            return Read(GetSdkToolsPath());
        }

        /// <summary>
        /// Read the inventory from a specific <c>sdk-tools.d.ts</c> file.
        /// </summary>
        /// <param name="sdkToolsPath">Path to the declarations file.</param>
        /// <returns>The inventory, or null when the file is absent or lists no tools.</returns>
        public static RuntimeBuiltInToolInventory? Read(string sdkToolsPath)
        {
            if (String.IsNullOrWhiteSpace(sdkToolsPath) || !File.Exists(sdkToolsPath))
            {
                return null;
            }

            string contents = File.ReadAllText(sdkToolsPath);
            MatchCollection matches = Regex.Matches(contents, @"^\s*\|\s*([A-Za-z0-9]+Input)\s*$", RegexOptions.Multiline);

            if (matches.Count < 1)
            {
                return null;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<CaptainToolSummary> tools = new List<CaptainToolSummary>();

            foreach (Match match in matches)
            {
                string inputName = match.Groups[1].Value;
                if (!seen.Add(inputName))
                {
                    continue;
                }

                GetDefinition(inputName, out string name, out string description);
                tools.Add(new CaptainToolSummary
                {
                    Name = name,
                    Description = description,
                    RegistrationSource = _SourceName,
                    SourceKind = CaptainToolSourceKindEnum.RuntimeBuiltIn
                });
            }

            return new RuntimeBuiltInToolInventory
            {
                SourceName = _SourceName,
                Target = "Installed CLI schema",
                Note = "Armada enumerated " + tools.Count + " Claude Code built-in tool(s) from the installed CLI schema.",
                Tools = tools
            };
        }

        #endregion

        #region Private-Methods

        private static void GetDefinition(string inputName, out string name, out string description)
        {
            switch (inputName)
            {
                case "AgentInput": name = "Agent"; description = "Spawns or resumes specialized subagents."; return;
                case "AskUserQuestionInput": name = "AskUserQuestion"; description = "Asks the user follow-up questions when more input is needed."; return;
                case "BashInput": name = "Bash"; description = "Runs shell commands in the current workspace."; return;
                case "ConfigInput": name = "Config"; description = "Reads or updates Claude Code runtime settings."; return;
                case "EnterWorktreeInput": name = "EnterWorktree"; description = "Creates or enters an isolated git worktree."; return;
                case "ExitPlanModeInput": name = "ExitPlanMode"; description = "Finalizes a plan and requests approval to implement it."; return;
                case "ExitWorktreeInput": name = "ExitWorktree"; description = "Leaves or removes an isolated git worktree."; return;
                case "FileEditInput": name = "Edit"; description = "Replaces exact text inside an existing file."; return;
                case "FileReadInput": name = "Read"; description = "Reads file contents, including notebooks and PDFs."; return;
                case "FileWriteInput": name = "Write"; description = "Writes file contents from scratch."; return;
                case "GlobInput": name = "Glob"; description = "Finds files by glob pattern."; return;
                case "GrepInput": name = "Grep"; description = "Searches file contents with ripgrep."; return;
                case "ListMcpResourcesInput": name = "ListMcpResources"; description = "Lists MCP resources exposed by configured servers."; return;
                case "McpInput": name = "Mcp"; description = "Invokes a tool through a configured MCP server."; return;
                case "NotebookEditInput": name = "NotebookEdit"; description = "Edits notebook cells in Jupyter notebooks."; return;
                case "ReadMcpResourceInput": name = "ReadMcpResource"; description = "Reads one resource from a configured MCP server."; return;
                case "SubscribeMcpResourceInput": name = "SubscribeMcpResource"; description = "Subscribes to change notifications for an MCP resource."; return;
                case "SubscribePollingInput": name = "SubscribePolling"; description = "Polls an MCP tool or resource on a recurring interval."; return;
                case "TaskOutputInput": name = "TaskOutput"; description = "Reads or waits on output from a background task."; return;
                case "TaskStopInput": name = "TaskStop"; description = "Stops a running background task."; return;
                case "TodoWriteInput": name = "TodoWrite"; description = "Maintains Claude Code's internal task list."; return;
                case "UnsubscribeMcpResourceInput": name = "UnsubscribeMcpResource"; description = "Stops an MCP resource subscription."; return;
                case "UnsubscribePollingInput": name = "UnsubscribePolling"; description = "Stops a recurring MCP polling subscription."; return;
                case "WebFetchInput": name = "WebFetch"; description = "Fetches and processes a specific URL."; return;
                case "WebSearchInput": name = "WebSearch"; description = "Searches the web for up-to-date information."; return;
                default:
                    name = inputName.EndsWith("Input", StringComparison.Ordinal)
                        ? inputName.Substring(0, inputName.Length - "Input".Length)
                        : inputName;
                    description = "Claude Code built-in tool.";
                    return;
            }
        }

        private static string GetSdkToolsPath()
        {
            if (OperatingSystem.IsWindows())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "npm",
                    "node_modules",
                    "@anthropic-ai",
                    "claude-code",
                    "sdk-tools.d.ts");
            }

            return Path.Combine(
                "/usr",
                "local",
                "lib",
                "node_modules",
                "@anthropic-ai",
                "claude-code",
                "sdk-tools.d.ts");
        }

        #endregion
    }
}
