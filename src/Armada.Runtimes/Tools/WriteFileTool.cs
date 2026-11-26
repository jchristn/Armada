namespace Armada.Runtimes.Tools
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools;
    using Armada.Runtimes.Tools.Arguments;

    /// <summary>
    /// Writes content to a file, creating parent directories as needed.
    /// Keeps an existing file's line endings, encoding, and byte order mark; a new file is UTF-8 without a byte order mark and uses the platform line ending.
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
            + "Preserves an existing file's line endings and byte order mark; uses platform default line endings for new files.";

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
            if (!ToolArgumentParser.TryParse(arguments, out WriteFileToolArguments? args, out string? argumentError))
            {
                return ToolArgumentParser.InvalidParameter(toolCallId, argumentError);
            }

            try
            {
                string filePath = args.FilePath!;
                string content = args.Content!;
                string resolvedPath = ResolvePath(filePath, workingDirectory);

                TextFileSnapshot format = File.Exists(resolvedPath)
                    ? await TextFileCodec.ReadAsync(resolvedPath, cancellationToken).ConfigureAwait(false)
                    : new TextFileSnapshot();

                string? parentDir = Path.GetDirectoryName(resolvedPath);
                if (parentDir != null && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                string normalizedContent = TextFileCodec.NormalizeToLf(content);
                await TextFileCodec.WriteAsync(resolvedPath, normalizedContent, format, cancellationToken).ConfigureAwait(false);

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
