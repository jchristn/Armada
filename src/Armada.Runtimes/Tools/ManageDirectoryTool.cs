namespace Armada.Runtimes.Tools
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools;

    /// <summary>
    /// Creates, deletes, or renames directories.
    /// </summary>
    public class ManageDirectoryTool : IToolExecutor
    {
        #region Public-Members

        /// <summary>
        /// The unique name of this tool.
        /// </summary>
        public string Name => "manage_directory";

        /// <summary>
        /// A human-readable description of what this tool does.
        /// </summary>
        public string Description => "Creates, deletes, or renames a directory. "
            + "Use action 'create' to create a directory (including parents), "
            + "'delete' to remove an empty or non-empty directory, "
            + "or 'rename' to move/rename a directory.";

        /// <summary>
        /// The JSON Schema object describing the tool's input parameters.
        /// </summary>
        public object ParametersSchema => new
        {
            type = "object",
            properties = new
            {
                action = new
                {
                    type = "string",
                    description = "The action to perform: 'create', 'delete', or 'rename'.",
                    @enum = new[] { "create", "delete", "rename" }
                },
                path = new
                {
                    type = "string",
                    description = "The path to the directory to create, delete, or rename from."
                },
                new_path = new
                {
                    type = "string",
                    description = "The new path for the directory (required for 'rename' action)."
                }
            },
            required = new[] { "action", "path" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Executes the manage_directory tool.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="arguments">The parsed JSON arguments containing action, path, and optionally new_path.</param>
        /// <param name="workingDirectory">The current working directory for resolving relative paths.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> indicating success or failure.</returns>
        public Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                string action = GetRequiredString(arguments, "action").ToLowerInvariant();
                string path = GetRequiredString(arguments, "path");
                string resolvedPath = ResolvePath(path, workingDirectory);

                switch (action)
                {
                    case "create":
                        return Task.FromResult(CreateDirectory(toolCallId, resolvedPath));

                    case "delete":
                        return Task.FromResult(DeleteDirectory(toolCallId, resolvedPath));

                    case "rename":
                        string newPath = GetOptionalString(arguments, "new_path");
                        if (string.IsNullOrWhiteSpace(newPath))
                        {
                            return Task.FromResult(new ToolResult
                            {
                                ToolCallId = toolCallId,
                                Success = false,
                                Content = JsonSerializer.Serialize(new { success = false, error = "missing_parameter", message = "The 'new_path' parameter is required for the 'rename' action." })
                            });
                        }
                        string resolvedNewPath = ResolvePath(newPath, workingDirectory);
                        return Task.FromResult(RenameDirectory(toolCallId, resolvedPath, resolvedNewPath));

                    default:
                        return Task.FromResult(new ToolResult
                        {
                            ToolCallId = toolCallId,
                            Success = false,
                            Content = JsonSerializer.Serialize(new { success = false, error = "invalid_action", message = $"Unknown action '{action}'. Use 'create', 'delete', or 'rename'." })
                        });
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { success = false, error = "directory_error", message = ex.Message })
                });
            }
        }

        #endregion

        #region Private-Methods

        private ToolResult CreateDirectory(string toolCallId, string path)
        {
            if (Directory.Exists(path))
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = true,
                    Content = JsonSerializer.Serialize(new { success = true, path = path, message = "Directory already exists." })
                };
            }

            try
            {
                Directory.CreateDirectory(path);
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = true,
                    Content = JsonSerializer.Serialize(new { success = true, path = path })
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { success = false, error = "permission_denied", message = $"Permission denied creating directory: {path}" })
                };
            }
        }

        private ToolResult DeleteDirectory(string toolCallId, string path)
        {
            if (!Directory.Exists(path))
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { success = false, error = "directory_not_found", message = $"Directory not found: {path}" })
                };
            }

            try
            {
                Directory.Delete(path, recursive: true);
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = true,
                    Content = JsonSerializer.Serialize(new { success = true, path = path })
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { success = false, error = "permission_denied", message = $"Permission denied deleting directory: {path}" })
                };
            }
        }

        private ToolResult RenameDirectory(string toolCallId, string fromPath, string toPath)
        {
            if (!Directory.Exists(fromPath))
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { success = false, error = "directory_not_found", message = $"Source directory not found: {fromPath}" })
                };
            }

            if (Directory.Exists(toPath))
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { success = false, error = "target_exists", message = $"Target directory already exists: {toPath}" })
                };
            }

            try
            {
                Directory.Move(fromPath, toPath);
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = true,
                    Content = JsonSerializer.Serialize(new { success = true, from_path = fromPath, to_path = toPath })
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { success = false, error = "permission_denied", message = $"Permission denied renaming directory." })
                };
            }
        }

        private string GetRequiredString(JsonElement arguments, string propertyName)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()!;
            }

            throw new ArgumentException($"Required parameter '{propertyName}' is missing or not a string.");
        }

        private string GetOptionalString(JsonElement arguments, string propertyName)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }

            return string.Empty;
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
