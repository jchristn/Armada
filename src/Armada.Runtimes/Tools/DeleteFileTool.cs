namespace Armada.Runtimes.Tools
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools;

    /// <summary>
    /// Deletes a file from the filesystem.
    /// </summary>
    public class DeleteFileTool : IToolExecutor
    {
        #region Public-Members

        /// <summary>
        /// The unique name of this tool.
        /// </summary>
        public string Name => "delete_file";

        /// <summary>
        /// A human-readable description of what this tool does.
        /// </summary>
        public string Description => "Deletes a file from the filesystem. Returns an error if the file does not exist.";

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
                    description = "The path to the file to delete."
                }
            },
            required = new[] { "file_path" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Executes the delete_file tool.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="arguments">The parsed JSON arguments containing file_path.</param>
        /// <param name="workingDirectory">The current working directory for resolving relative paths.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> indicating success or failure.</returns>
        public Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                string filePath = GetRequiredString(arguments, "file_path");
                string resolvedPath = ResolvePath(filePath, workingDirectory);

                if (!File.Exists(resolvedPath))
                {
                    return Task.FromResult(new ToolResult
                    {
                        ToolCallId = toolCallId,
                        Success = false,
                        Content = JsonSerializer.Serialize(new { success = false, error = "file_not_found", message = $"File not found: {resolvedPath}" })
                    });
                }

                File.Delete(resolvedPath);

                return Task.FromResult(new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = true,
                    Content = JsonSerializer.Serialize(new { success = true, file_path = resolvedPath })
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult(new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { success = false, error = "permission_denied", message = "Permission denied when deleting the file." })
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { success = false, error = "delete_error", message = ex.Message })
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
