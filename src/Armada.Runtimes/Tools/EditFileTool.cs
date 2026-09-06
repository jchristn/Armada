namespace Armada.Runtimes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools;

    /// <summary>
    /// Performs a single exact string replacement in a file.
    /// Normalizes content to LF for matching, then preserves original line endings on write.
    /// </summary>
    public class EditFileTool : IToolExecutor
    {
        #region Public-Members

        /// <summary>
        /// The unique name of this tool.
        /// </summary>
        public string Name => "edit_file";

        /// <summary>
        /// A human-readable description of what this tool does.
        /// </summary>
        public string Description => "Performs an exact string replacement in a file. Finds old_string and replaces it with new_string. "
            + "Returns an error if the old_string is not found or matches multiple locations (ambiguous).";

        /// <summary>
        /// The JSON Schema object describing the tool's input parameters.
        /// </summary>
        public object ParametersSchema => new
        {
            type = "object",
            properties = new
            {
                file_path = new
                {
                    type = "string",
                    description = "The absolute path to the file to edit."
                },
                old_string = new
                {
                    type = "string",
                    description = "The exact string to find and replace."
                },
                new_string = new
                {
                    type = "string",
                    description = "The replacement string."
                }
            },
            required = new[] { "file_path", "old_string", "new_string" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Executes the edit_file tool.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="arguments">The parsed JSON arguments containing file_path, old_string, and new_string.</param>
        /// <param name="workingDirectory">The current working directory for resolving relative paths.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> containing the edit result or error details.</returns>
        public async Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                string filePath = GetRequiredString(arguments, "file_path");
                string oldString = GetRequiredString(arguments, "old_string");
                string newString = GetRequiredString(arguments, "new_string");
                string resolvedPath = ResolvePath(filePath, workingDirectory);

                if (!File.Exists(resolvedPath))
                {
                    return new ToolResult
                    {
                        ToolCallId = toolCallId,
                        Success = false,
                        Content = JsonSerializer.Serialize(new { success = false, error = "file_not_found", message = $"File not found: {resolvedPath}" })
                    };
                }

                string originalContent = await File.ReadAllTextAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
                string lineEnding = DetectLineEnding(originalContent);
                string lfContent = originalContent.Replace("\r\n", "\n").Replace("\r", "\n");
                string lfOldString = oldString.Replace("\r\n", "\n").Replace("\r", "\n");
                string lfNewString = newString.Replace("\r\n", "\n").Replace("\r", "\n");

                List<int> matchPositions = FindAllOccurrences(lfContent, lfOldString);

                if (matchPositions.Count == 0)
                {
                    return new ToolResult
                    {
                        ToolCallId = toolCallId,
                        Success = false,
                        Content = JsonSerializer.Serialize(new
                        {
                            success = false,
                            error = "old_string_not_found",
                            message = "The specified old_string was not found in the file.",
                            file_path = resolvedPath
                        })
                    };
                }

                if (matchPositions.Count > 1)
                {
                    List<int> lineNumbers = new List<int>();
                    foreach (int pos in matchPositions)
                    {
                        int lineNumber = CountLines(lfContent, pos);
                        lineNumbers.Add(lineNumber);
                    }

                    return new ToolResult
                    {
                        ToolCallId = toolCallId,
                        Success = false,
                        Content = JsonSerializer.Serialize(new
                        {
                            success = false,
                            error = "ambiguous_match",
                            details = new
                            {
                                match_count = matchPositions.Count,
                                candidate_line_numbers = lineNumbers,
                                suggestion = "Provide more surrounding context in old_string to uniquely identify the target location."
                            }
                        })
                    };
                }

                string lfResult = lfContent.Substring(0, matchPositions[0])
                    + lfNewString
                    + lfContent.Substring(matchPositions[0] + lfOldString.Length);

                string outputContent = lfResult.Replace("\n", lineEnding);
                await File.WriteAllTextAsync(resolvedPath, outputContent, cancellationToken).ConfigureAwait(false);

                int newLineCount = lfResult.Split('\n').Length;

                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = true,
                    Content = JsonSerializer.Serialize(new
                    {
                        success = true,
                        file_path = resolvedPath,
                        edits_applied = 1,
                        new_line_count = newLineCount
                    })
                };
            }
            catch (Exception ex)
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { success = false, error = "edit_error", message = ex.Message })
                };
            }
        }

        #endregion

        #region Private-Methods

        private List<int> FindAllOccurrences(string content, string search)
        {
            List<int> positions = new List<int>();
            int index = 0;

            while (index < content.Length)
            {
                int found = content.IndexOf(search, index, StringComparison.Ordinal);
                if (found < 0) break;
                positions.Add(found);
                index = found + 1;
            }

            return positions;
        }

        private int CountLines(string content, int upToPosition)
        {
            int lineNumber = 1;
            for (int i = 0; i < upToPosition && i < content.Length; i++)
            {
                if (content[i] == '\n')
                {
                    lineNumber++;
                }
            }

            return lineNumber;
        }

        private string DetectLineEnding(string content)
        {
            int crlfIndex = content.IndexOf("\r\n", StringComparison.Ordinal);
            int lfIndex = content.IndexOf("\n", StringComparison.Ordinal);
            int crIndex = content.IndexOf("\r", StringComparison.Ordinal);

            if (crlfIndex >= 0 && (crlfIndex <= lfIndex || lfIndex < 0))
            {
                return "\r\n";
            }

            if (lfIndex >= 0)
            {
                return "\n";
            }

            if (crIndex >= 0)
            {
                return "\r";
            }

            return Environment.NewLine;
        }

        private string GetRequiredString(JsonElement arguments, string propertyName)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()!;
            }

            throw new ArgumentException($"Required parameter '{propertyName}' is missing or not a string.");
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
