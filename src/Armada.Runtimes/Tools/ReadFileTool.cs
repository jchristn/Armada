namespace Armada.Runtimes.Tools
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools.Arguments;

    /// <summary>
    /// Reads a file from the filesystem and returns its contents with line numbers.
    /// </summary>
    public class ReadFileTool : IToolExecutor
    {
        private const int _DefaultPageLines = 200;
        private const int _DefaultPageCharacters = 24_000;
        private const int _PaginationFooterReserve = 512;

        #region Public-Members

        /// <summary>
        /// The unique name of this tool.
        /// </summary>
        public string Name => "read_file";

        /// <summary>
        /// A human-readable description of what this tool does.
        /// </summary>
        public string Description => "Reads a file from the filesystem and returns its contents with line numbers (like cat -n); the numbers are for display, so leave them out of edits. "
            + "Defaults to pages of at most 200 lines and 24,000 characters with continuation instructions. CLAUDE.md and AGENTS.md are read completely by default. Explicit limit requests retain full/range behavior.";

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
                    description = "The absolute path to the file to read."
                },
                offset = new
                {
                    type = "integer",
                    description = "The line number to start reading from (1-based). Defaults to 1."
                },
                limit = new
                {
                    type = "integer",
                    description = "The maximum number of lines to read. Omit for a bounded 200-line page (24,000 characters), except CLAUDE.md/AGENTS.md, which default to complete instructions. Explicit zero or negative reads the remaining file within the file-size limit."
                }
            },
            required = new[] { "file_path" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Executes the read_file tool.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="arguments">The parsed JSON arguments containing file_path, offset, and limit.</param>
        /// <param name="workingDirectory">The current working directory for resolving relative paths.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> containing the file contents with line numbers.</returns>
        public async Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            if (!ToolArgumentParser.TryParse(arguments, out ReadFileToolArguments? args, out string? argumentError))
            {
                return ToolArgumentParser.InvalidParameter(toolCallId, argumentError);
            }

            try
            {
                string filePath = args.FilePath!;
                string resolvedPath = ResolvePath(filePath, workingDirectory);

                int offset = args.Offset ?? 1;
                bool boundedDefault = !args.Limit.HasValue && !IsInstructionFile(resolvedPath);
                int limit = args.Limit ?? (boundedDefault ? _DefaultPageLines : -1);

                if (!File.Exists(resolvedPath))
                {
                    return new ToolResult
                    {
                        ToolCallId = toolCallId,
                        Success = false,
                        Content = JsonSerializer.Serialize(new { error = "file_not_found", message = $"File not found: {resolvedPath}" })
                    };
                }

                FileInfo fileInfo = new FileInfo(resolvedPath);
                if (fileInfo.Length > ToolSafetyLimits.MaxReadFileBytes)
                {
                    return new ToolResult
                    {
                        ToolCallId = toolCallId,
                        Success = false,
                        Content = JsonSerializer.Serialize(new
                        {
                            error = "file_too_large",
                            message = $"File size ({fileInfo.Length} bytes) exceeds maximum allowed ({ToolSafetyLimits.MaxReadFileBytes} bytes): {resolvedPath}"
                        })
                    };
                }

                string[] lines = await File.ReadAllLinesAsync(resolvedPath, cancellationToken).ConfigureAwait(false);

                int startIndex = offset <= 1 ? 0 : offset - 1;
                int endIndex = limit > 0 ? (int)Math.Min(lines.Length, (long)startIndex + limit) : lines.Length;
                StringBuilder sb = new StringBuilder();
                int nextIndex = startIndex;
                bool oversizedLine = false;

                for (int i = startIndex; i < endIndex; i++)
                {
                    string lineContent = lines[i].Replace("\r", string.Empty);
                    string numberedLine = $"{i + 1,6}\t{lineContent}\n";
                    if (boundedDefault && sb.Length + numberedLine.Length > _DefaultPageCharacters - _PaginationFooterReserve)
                    {
                        oversizedLine = numberedLine.Length > _DefaultPageCharacters - _PaginationFooterReserve;
                        break;
                    }
                    sb.Append(numberedLine);
                    nextIndex = i + 1;
                }

                if (boundedDefault && nextIndex < lines.Length)
                {
                    sb.Append($"[read_file: output paged; {lines.Length} total lines. Continue with offset={nextIndex + 1}. ");
                    sb.Append(oversizedLine
                        ? "The next line exceeds the default character budget; request an explicit limit=1 to read it, or use grep for a smaller result."
                        : "Omitted limit defaults to at most 200 lines and 24,000 characters; an explicit limit selects a range.");
                    sb.Append("]\n");
                }

                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = true,
                    Content = sb.ToString()
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "permission_denied", message = "Permission denied when reading the file." })
                };
            }
            catch (Exception ex)
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "read_error", message = ex.Message })
                };
            }
        }

        #endregion

        #region Private-Methods

        private static bool IsInstructionFile(string filePath)
        {
            string name = Path.GetFileName(filePath);
            return String.Equals(name, "CLAUDE.md", StringComparison.OrdinalIgnoreCase)
                || String.Equals(name, "AGENTS.md", StringComparison.OrdinalIgnoreCase);
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
