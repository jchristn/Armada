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
    /// Searches files recursively for lines matching a regular expression pattern.
    /// Returns matching lines with file path, line number, and content.
    /// </summary>
    public class GrepTool : IToolExecutor
    {
        #region Private-Members

        private const int _MaxMatches = 100;

        #endregion

        #region Public-Members

        /// <summary>
        /// The unique name of this tool.
        /// </summary>
        public string Name => "grep";

        /// <summary>
        /// A human-readable description of what this tool does.
        /// </summary>
        public string Description => "Searches files recursively for lines matching a regular expression. "
            + "Returns matching lines with file path and line number. Limited to the first 100 matches.";

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
                    description = "The regular expression pattern to search for."
                },
                path = new
                {
                    type = "string",
                    description = "The directory to search in. Defaults to the working directory."
                },
                include = new
                {
                    type = "string",
                    description = "A glob pattern to filter which files to search (e.g., '*.cs', '*.json')."
                }
            },
            required = new[] { "pattern" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Executes the grep tool.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="arguments">The parsed JSON arguments containing pattern, optional path, and optional include.</param>
        /// <param name="workingDirectory">The current working directory for resolving relative paths.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> containing the matching lines.</returns>
        public Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                string pattern = GetRequiredString(arguments, "pattern");
                string searchPath = GetOptionalString(arguments, "path", workingDirectory) ?? workingDirectory;
                string? include = GetOptionalString(arguments, "include", null);
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

                Regex regex;
                try
                {
                    regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(5));
                }
                catch (ArgumentException ex)
                {
                    return Task.FromResult(new ToolResult
                    {
                        ToolCallId = toolCallId,
                        Success = false,
                        Content = JsonSerializer.Serialize(new { error = "invalid_regex", message = $"Invalid regular expression: {ex.Message}" })
                    });
                }

                string fileFilter = include ?? "*";
                IEnumerable<string> files = Directory.EnumerateFiles(resolvedPath, fileFilter, SearchOption.AllDirectories);

                StringBuilder sb = new StringBuilder();
                int matchCount = 0;

                foreach (string filePath in files)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (matchCount >= _MaxMatches) break;

                    try
                    {
                        string[] lines = File.ReadAllLines(filePath);
                        string relativePath = Path.GetRelativePath(resolvedPath, filePath).Replace('\\', '/');

                        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                        {
                            if (matchCount >= _MaxMatches) break;

                            if (regex.IsMatch(lines[lineIndex]))
                            {
                                int lineNumber = lineIndex + 1;
                                sb.AppendLine($"{relativePath}:{lineNumber}: {lines[lineIndex]}");
                                matchCount++;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Skip files that cannot be read (binary, locked, etc.)
                    }
                }

                if (matchCount == 0)
                {
                    sb.AppendLine("No matches found.");
                }
                else if (matchCount >= _MaxMatches)
                {
                    sb.AppendLine($"(output truncated at {_MaxMatches} matches)");
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
                    Content = JsonSerializer.Serialize(new { error = "grep_error", message = ex.Message })
                });
            }
        }

        #endregion

        #region Private-Methods

        private string GetRequiredString(JsonElement arguments, string propertyName)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()!;
            }

            throw new ArgumentException($"Required parameter '{propertyName}' is missing or not a string.");
        }

        private string? GetOptionalString(JsonElement arguments, string propertyName, string? defaultValue)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
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
