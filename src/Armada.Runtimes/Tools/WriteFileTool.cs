namespace Armada.Runtimes.Tools
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools;

    /// <summary>
    /// Writes content to a file, creating parent directories as needed.
    /// Preserves existing line ending style for existing files, uses platform default for new files.
    /// </summary>
    public class WriteFileTool : IToolExecutor
    {
        #region Public-Members

        /// <summary>
        /// The unique name of this tool.
        /// </summary>
        public string Name => "write_file";

        /// <summary>
        /// A human-readable description of what this tool does.
        /// </summary>
        public string Description => "Writes content to a file. Creates parent directories if they do not exist. "
            + "Preserves original line ending style for existing files; uses platform default for new files.";

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
                    description = "The absolute path to the file to write."
                },
                content = new
                {
                    type = "string",
                    description = "The content to write to the file."
                }
            },
            required = new[] { "file_path", "content" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Executes the write_file tool.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="arguments">The parsed JSON arguments containing file_path and content.</param>
        /// <param name="workingDirectory">The current working directory for resolving relative paths.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> containing the write operation result.</returns>
        public async Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                string filePath = GetRequiredString(arguments, "file_path");
                string content = GetRequiredString(arguments, "content");
                string resolvedPath = ResolvePath(filePath, workingDirectory);

                string lineEnding = Environment.NewLine;

                if (File.Exists(resolvedPath))
                {
                    lineEnding = DetectLineEnding(resolvedPath);
                }

                string? parentDir = Path.GetDirectoryName(resolvedPath);
                if (parentDir != null && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                string normalizedContent = content.Replace("\r\n", "\n").Replace("\r", "\n");
                string outputContent = normalizedContent.Replace("\n", lineEnding);

                await File.WriteAllTextAsync(resolvedPath, outputContent, cancellationToken).ConfigureAwait(false);

                int lineCount = normalizedContent.Split('\n').Length;

                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = true,
                    Content = JsonSerializer.Serialize(new
                    {
                        success = true,
                        file_path = resolvedPath,
                        line_count = lineCount
                    })
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "permission_denied", message = "Permission denied when writing the file." })
                };
            }
            catch (Exception ex)
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "write_error", message = ex.Message })
                };
            }
        }

        #endregion

        #region Private-Methods

        private string DetectLineEnding(string filePath)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                int current;
                while ((current = fs.ReadByte()) != -1)
                {
                    if (current == '\r')
                    {
                        int next = fs.ReadByte();
                        if (next == '\n')
                        {
                            return "\r\n";
                        }

                        return "\r";
                    }

                    if (current == '\n')
                    {
                        return "\n";
                    }
                }
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
