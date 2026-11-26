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
        private readonly ToolLimits _Limits;

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes the tool with output limits.
        /// </summary>
        /// <param name="limits">The output limits for this run, or null for the limits of an unknown context window.</param>
        public ReadFileTool(ToolLimits? limits = null)
        {
            _Limits = limits ?? new ToolLimits();
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The unique name of this tool.
        /// </summary>
        public string Name => "read_file";

        /// <summary>
        /// A human-readable description of what this tool does.
        /// </summary>
        public string Description => "Reads a file from the filesystem and returns its contents with line numbers (like cat -n); the numbers are for display, so leave them out of edits. "
            + "Defaults to pages of at most 200 lines and 24,000 characters with continuation instructions. CLAUDE.md and AGENTS.md are read completely by default. Explicit limit requests retain full/range behavior up to a size limit set by the context window, then page with continuation instructions.";

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
                    description = "The maximum number of lines to read. Omit for a bounded 200-line page (24,000 characters), except CLAUDE.md/AGENTS.md, which default to complete instructions. Explicit zero or negative reads the remaining file, paged at the context-window size limit."
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
                if (fileInfo.Length > _Limits.MaxReadFileBytes)
                {
                    return new ToolResult
                    {
                        ToolCallId = toolCallId,
                        Success = false,
                        Content = JsonSerializer.Serialize(new
                        {
                            error = "file_too_large",
                            message = $"File size ({fileInfo.Length} bytes) exceeds maximum allowed ({_Limits.MaxReadFileBytes} bytes): {resolvedPath}. Use grep, or run_process with head or tail, for a smaller result."
                        })
                    };
                }

                int budget = (boundedDefault ? _DefaultPageCharacters : _Limits.MaxReadCharacters) - _PaginationFooterReserve;
                int startIndex = offset <= 1 ? 0 : offset - 1;
                long endIndex = limit > 0 ? (long)startIndex + limit : Int64.MaxValue;
                StringBuilder sb = new StringBuilder();
                int nextIndex = startIndex;
                int totalLines = 0;
                bool stopped = false;
                bool oversizedLine = false;
                bool truncatedLine = false;

                using (StreamReader reader = new StreamReader(resolvedPath, Encoding.UTF8, true))
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
                    {
                        int i = totalLines;
                        totalLines++;
                        if (stopped || i < startIndex || i >= endIndex) continue;

                        string numberedLine = $"{i + 1,6}\t{line.Replace("\r", string.Empty)}\n";
                        if (sb.Length + numberedLine.Length > budget)
                        {
                            stopped = true;
                            oversizedLine = numberedLine.Length > budget;
                            if (!boundedDefault && sb.Length == 0)
                            {
                                sb.Append(numberedLine, 0, budget).Append('\n');
                                truncatedLine = true;
                                nextIndex = i + 1;
                            }

                            continue;
                        }

                        sb.Append(numberedLine);
                        nextIndex = i + 1;
                    }
                }

                if (boundedDefault && nextIndex < totalLines)
                {
                    sb.Append($"[read_file: output paged; {totalLines} total lines. Continue with offset={nextIndex + 1}. ");
                    sb.Append(oversizedLine
                        ? "The next line exceeds the default character budget; request an explicit limit=1 to read it, or use grep for a smaller result."
                        : "Omitted limit defaults to at most 200 lines and 24,000 characters; an explicit limit selects a range.");
                    sb.Append("]\n");
                }
                else if (!boundedDefault && stopped)
                {
                    if (truncatedLine)
                    {
                        sb.Append($"[read_file: line {nextIndex} is longer than the {_Limits.MaxReadCharacters}-character read limit for this context window and was cut. Use grep for a smaller result.");
                    }
                    else
                    {
                        sb.Append($"[read_file: output paged at the {_Limits.MaxReadCharacters}-character read limit for this context window");
                    }

                    sb.Append(nextIndex < totalLines && nextIndex < endIndex
                        ? $"; {totalLines} total lines. Continue with offset={nextIndex + 1}.]\n"
                        : "]\n");
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
