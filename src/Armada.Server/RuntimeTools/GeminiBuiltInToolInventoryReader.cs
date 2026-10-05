namespace Armada.Server.RuntimeTools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Best-effort, display-only inventory of Gemini CLI's built-in tools, scraped from files of a globally installed
    /// <c>@google/gemini-cli</c> npm package: tool-name constants in the compiled JavaScript of
    /// <c>@google/gemini-cli-core</c> and descriptions from its bundled <c>docs/reference/tools.md</c> table. The package
    /// ships no structured (JSON) tool manifest and the CLI only lists tools from an interactive session, so this is the
    /// only offline source. The result is shown in the captain tool viewer only; nothing in Armada branches on it. When
    /// the package is absent no inventory is reported.
    /// </summary>
    internal static class GeminiBuiltInToolInventoryReader
    {
        #region Private-Members

        private const string _SourceName = "Gemini CLI Built-In Tools";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the inventory from the default global npm install location.
        /// </summary>
        /// <returns>The inventory, or null when the package is absent.</returns>
        public static RuntimeBuiltInToolInventory? Read()
        {
            return Read(GetCorePackagePath());
        }

        /// <summary>
        /// Read the inventory from a specific <c>@google/gemini-cli-core</c> package directory.
        /// </summary>
        /// <param name="corePackagePath">Package directory.</param>
        /// <returns>The inventory, or null when the expected package files are absent.</returns>
        public static RuntimeBuiltInToolInventory? Read(string corePackagePath)
        {
            if (String.IsNullOrWhiteSpace(corePackagePath))
            {
                return null;
            }

            string baseDeclarationsPath = Path.Combine(corePackagePath, "dist", "src", "tools", "definitions", "base-declarations.js");
            string toolNamesPath = Path.Combine(corePackagePath, "dist", "src", "tools", "tool-names.js");
            string docsPath = Path.Combine(corePackagePath, "dist", "docs", "reference", "tools.md");

            if (!File.Exists(baseDeclarationsPath) || !File.Exists(toolNamesPath))
            {
                return null;
            }

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(
                File.ReadAllText(baseDeclarationsPath),
                @"export const [A-Z0-9_]+_TOOL_NAME = '([^']+)';"))
            {
                names.Add(match.Groups[1].Value);
            }

            foreach (Match match in Regex.Matches(
                File.ReadAllText(toolNamesPath),
                @"export const TRACKER_[A-Z0-9_]+_TOOL_NAME = '([^']+)';"))
            {
                names.Add(match.Groups[1].Value);
            }

            Dictionary<string, string> descriptions = File.Exists(docsPath)
                ? ParseToolDescriptions(docsPath)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            List<CaptainToolSummary> tools = new List<CaptainToolSummary>();

            foreach (string name in names.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                string description = descriptions.TryGetValue(name, out string? documented)
                    ? documented
                    : GetFallbackDescription(name);
                tools.Add(CreateTool(name, description));
            }

            if (descriptions.TryGetValue("complete_task", out string? completeTaskDescription))
            {
                tools.Add(CreateTool("complete_task", completeTaskDescription));
            }

            return new RuntimeBuiltInToolInventory
            {
                SourceName = _SourceName,
                Target = "Installed CLI package",
                Note = "Armada enumerated " + tools.Count + " Gemini CLI built-in tool(s) from the installed CLI package.",
                Tools = tools
            };
        }

        #endregion

        #region Private-Methods

        private static CaptainToolSummary CreateTool(string name, string description)
        {
            return new CaptainToolSummary
            {
                Name = name,
                Description = description,
                RegistrationSource = _SourceName,
                SourceKind = CaptainToolSourceKindEnum.RuntimeBuiltIn
            };
        }

        private static Dictionary<string, string> ParseToolDescriptions(string docsPath)
        {
            Dictionary<string, string> descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawLine in File.ReadLines(docsPath))
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("|", StringComparison.Ordinal) || line.StartsWith("| :", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] cells = line.Split('|');
                if (cells.Length < 5)
                {
                    continue;
                }

                string? toolName = ExtractToolName(cells[2].Trim());
                if (String.IsNullOrWhiteSpace(toolName))
                {
                    continue;
                }

                string description = cells[4].Trim().Replace("<br><br>", " ", StringComparison.Ordinal);
                int parameterIndex = description.IndexOf("**Parameters:**", StringComparison.Ordinal);
                if (parameterIndex >= 0)
                {
                    description = description.Substring(0, parameterIndex).Trim();
                }

                if (!String.IsNullOrWhiteSpace(description))
                {
                    descriptions[toolName] = description;
                }
            }

            descriptions["tracker_create_task"] = "Creates a new tracker task in Gemini's built-in task graph.";
            descriptions["tracker_update_task"] = "Updates an existing tracker task in Gemini's built-in task graph.";
            descriptions["tracker_get_task"] = "Reads one tracker task from Gemini's built-in task graph.";
            descriptions["tracker_list_tasks"] = "Lists tracker tasks from Gemini's built-in task graph.";
            descriptions["tracker_add_dependency"] = "Adds a dependency edge between Gemini tracker tasks.";
            descriptions["tracker_visualize"] = "Renders Gemini's built-in task graph and dependencies.";

            return descriptions;
        }

        private static string? ExtractToolName(string toolCell)
        {
            if (String.IsNullOrWhiteSpace(toolCell))
            {
                return null;
            }

            if (toolCell.StartsWith("[`", StringComparison.Ordinal))
            {
                int end = toolCell.IndexOf("`]", StringComparison.Ordinal);
                if (end > 2)
                {
                    return toolCell.Substring(2, end - 2).Trim();
                }
            }

            if (toolCell.StartsWith("`", StringComparison.Ordinal) && toolCell.EndsWith("`", StringComparison.Ordinal) && toolCell.Length > 2)
            {
                return toolCell.Substring(1, toolCell.Length - 2).Trim();
            }

            return null;
        }

        private static string GetFallbackDescription(string name)
        {
            return name switch
            {
                "tracker_create_task" => "Creates a new tracker task in Gemini's built-in task graph.",
                "tracker_update_task" => "Updates an existing tracker task in Gemini's built-in task graph.",
                "tracker_get_task" => "Reads one tracker task from Gemini's built-in task graph.",
                "tracker_list_tasks" => "Lists tracker tasks from Gemini's built-in task graph.",
                "tracker_add_dependency" => "Adds a dependency edge between Gemini tracker tasks.",
                "tracker_visualize" => "Renders Gemini's built-in task graph and dependencies.",
                _ => "Gemini CLI built-in tool."
            };
        }

        private static string GetCorePackagePath()
        {
            if (OperatingSystem.IsWindows())
            {
                string candidate = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "npm",
                    "node_modules",
                    "@google",
                    "gemini-cli",
                    "node_modules",
                    "@google",
                    "gemini-cli-core");

                return Directory.Exists(candidate) ? candidate : String.Empty;
            }

            string unixCandidate = Path.Combine(
                "/usr",
                "local",
                "lib",
                "node_modules",
                "@google",
                "gemini-cli",
                "node_modules",
                "@google",
                "gemini-cli-core");

            return Directory.Exists(unixCandidate) ? unixCandidate : String.Empty;
        }

        #endregion
    }
}
