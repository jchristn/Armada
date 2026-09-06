namespace Armada.Runtimes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools;

    /// <summary>
    /// Lists files and directories at a given path with type indicators.
    /// Directories are listed first, then files, each sorted alphabetically.
    /// </summary>
    public class ListDirectoryTool : IToolExecutor
    {
        #region Public-Members

        /// <summary>
        /// The unique name of this tool.
        /// </summary>
        public string Name => "list_directory";

        /// <summary>
        /// A human-readable description of what this tool does.
        /// </summary>
        public string Description => "Lists files and directories at a given path. "
            + "Directories are listed first (marked [DIR]), then files (marked [FILE]), sorted alphabetically within each group.";

        /// <summary>
        /// The JSON Schema object describing the tool's input parameters.
        /// </summary>
        public object ParametersSchema => new
        {
            type = "object",
            properties = new
            {
                path = new
                {
                    type = "string",
                    description = "The absolute path to the directory to list."
                }
            },
            required = new[] { "path" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Executes the list_directory tool.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="arguments">The parsed JSON arguments containing path.</param>
        /// <param name="workingDirectory">The current working directory for resolving relative paths.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> containing the directory listing.</returns>
        public Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                string path = GetRequiredString(arguments, "path");
                string resolvedPath = ResolvePath(path, workingDirectory);

                if (!Directory.Exists(resolvedPath))
                {
                    return Task.FromResult(new ToolResult
                    {
                        ToolCallId = toolCallId,
                        Success = false,
                        Content = JsonSerializer.Serialize(new { error = "directory_not_found", message = $"Directory not found: {resolvedPath}" })
                    });
                }

                List<string> directories = Directory.GetDirectories(resolvedPath)
                    .Select(d => Path.GetFileName(d))
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                List<string> files = Directory.GetFiles(resolvedPath)
                    .Select(f => Path.GetFileName(f))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                StringBuilder sb = new StringBuilder();

                foreach (string dir in directories)
                {
                    sb.AppendLine($"[DIR]  {dir}");
                }

                foreach (string file in files)
                {
                    sb.AppendLine($"[FILE] {file}");
                }

                return Task.FromResult(new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = true,
                    Content = sb.ToString()
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult(new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "permission_denied", message = "Permission denied when listing the directory." })
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "list_error", message = ex.Message })
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
