namespace Armada.Runtimes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools;

    /// <summary>
    /// Searches for files matching a glob pattern within a directory tree.
    /// Supports *, **, and ? wildcard patterns.
    /// </summary>
    public class GlobTool : IToolExecutor
    {
        #region Public-Members

        /// <summary>
        /// The unique name of this tool.
        /// </summary>
        public string Name => "glob";

        /// <summary>
        /// A human-readable description of what this tool does.
        /// </summary>
        public string Description => "Searches for files matching a glob pattern. "
            + "Supports * (any characters in filename), ** (any path segments), and ? (single character).";

        /// <summary>
        /// The JSON Schema object describing the tool's input parameters.
        /// </summary>
        public object ParametersSchema => new
        {
            type = "object",
            properties = new
            {
                pattern = new
                {
                    type = "string",
                    description = "The glob pattern to match files against (e.g., '**/*.cs', 'src/**/*.json')."
                },
                path = new
                {
                    type = "string",
                    description = "The directory to search in. Defaults to the working directory."
                }
            },
            required = new[] { "pattern" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Executes the glob tool.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="arguments">The parsed JSON arguments containing pattern and optional path.</param>
        /// <param name="workingDirectory">The current working directory for resolving relative paths.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> containing the matching file paths.</returns>
        public Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                string pattern = GetRequiredString(arguments, "pattern");
                string searchPath = GetOptionalString(arguments, "path", workingDirectory);
                string resolvedPath = ResolvePath(searchPath, workingDirectory);

                if (!Directory.Exists(resolvedPath))
                {
                    return Task.FromResult(new ToolResult
                    {
                        ToolCallId = toolCallId,
                        Success = false,
                        Content = JsonSerializer.Serialize(new { error = "directory_not_found", message = $"Directory not found: {resolvedPath}" })
                    });
                }

                Regex regex = GlobToRegex(pattern);

                List<string> matches = new List<string>();
                IEnumerable<string> entries = Directory.EnumerateFileSystemEntries(resolvedPath, "*", SearchOption.AllDirectories);

                foreach (string entry in entries)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    if (!File.Exists(entry)) continue;

                    string relativePath = Path.GetRelativePath(resolvedPath, entry).Replace('\\', '/');

                    if (regex.IsMatch(relativePath))
                    {
                        matches.Add(relativePath);
                    }
                }

                matches.Sort(StringComparer.OrdinalIgnoreCase);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Found {matches.Count} matching file(s):");

                foreach (string match in matches)
                {
                    sb.AppendLine(match);
                }

                return Task.FromResult(new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = true,
                    Content = sb.ToString()
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "glob_error", message = ex.Message })
                });
            }
        }

        #endregion

        #region Private-Methods

        private Regex GlobToRegex(string pattern)
        {
            string normalized = pattern.Replace('\\', '/');
            StringBuilder regexPattern = new StringBuilder("^");
            int i = 0;

            while (i < normalized.Length)
            {
                char c = normalized[i];

                if (c == '*' && i + 1 < normalized.Length && normalized[i + 1] == '*')
                {
                    // ** matches any path segments
                    if (i + 2 < normalized.Length && normalized[i + 2] == '/')
                    {
                        regexPattern.Append("(.+/)?");
                        i += 3;
                    }
                    else
                    {
                        regexPattern.Append(".*");
                        i += 2;
                    }
                }
                else if (c == '*')
                {
                    // * matches any characters except /
                    regexPattern.Append("[^/]*");
                    i++;
                }
                else if (c == '?')
                {
                    // ? matches single character except /
                    regexPattern.Append("[^/]");
                    i++;
                }
                else if (c == '.')
                {
                    regexPattern.Append("\\.");
                    i++;
                }
                else if (c == '{')
                {
                    regexPattern.Append("(");
                    i++;
                }
                else if (c == '}')
                {
                    regexPattern.Append(")");
                    i++;
                }
                else if (c == ',')
                {
                    regexPattern.Append("|");
                    i++;
                }
                else
                {
                    regexPattern.Append(Regex.Escape(c.ToString()));
                    i++;
                }
            }

            regexPattern.Append("$");
            return new Regex(regexPattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        private string GetRequiredString(JsonElement arguments, string propertyName)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()!;
            }

            throw new ArgumentException($"Required parameter '{propertyName}' is missing or not a string.");
        }

        private string GetOptionalString(JsonElement arguments, string propertyName, string defaultValue)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()!;
            }

            return defaultValue;
        }

        private string ResolvePath(string filePath, string workingDirectory)
        {
            if (Path.IsPathRooted(filePath))
            {
                return Path.GetFullPath(filePath);
            }

            return Path.GetFullPath(Path.Combine(workingDirectory, filePath));
        }

        #endregion
    }
}
