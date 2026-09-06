namespace Armada.Runtimes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools.Tasks;
    using Armada.Runtimes.Tools;
    using Armada.Runtimes.Tools.Tasks;
    using Armada.Runtimes.Tools;

    /// <summary>
    /// Advances a single task in the current job's plan: sets its status and optionally a note. The model
    /// calls this the moment a task starts and again the moment it finishes. Read-only with respect to the
    /// workspace, so it never acquires the write lease.
    /// </summary>
    public class UpdateTaskTool : IToolExecutor
    {
        #region Private-Members

        private readonly TaskPlan? _TaskPlan;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UpdateTaskTool"/> class.
        /// </summary>
        /// <param name="taskPlan">The per-job task plan to update, or null when task planning is not active
        /// for the run (the tool then returns an error result).</param>
        public UpdateTaskTool(TaskPlan? taskPlan)
        {
            _TaskPlan = taskPlan;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The unique name of this tool.
        /// </summary>
        public string Name => "update_task";

        /// <summary>
        /// A human-readable description of what this tool does.
        /// </summary>
        public string Description => "Updates the status of one task in the current plan. "
            + "Set status to in_progress when you start a task and completed when you finish it; keep exactly one task "
            + "in_progress at a time. Use blocked (with a note) when a task cannot proceed, skipped when it is no longer needed, "
            + "and failed (a note is required) when an attempt failed.";

        /// <summary>
        /// The JSON Schema object describing the tool's input parameters.
        /// </summary>
        public object ParametersSchema => new
        {
            type = "object",
            properties = new
            {
                id = new
                {
                    type = "string",
                    description = "The id of the task to update."
                },
                status = new
                {
                    type = "string",
                    description = "The new status.",
                    @enum = new[] { "pending", "in_progress", "completed", "failed", "skipped", "blocked" }
                },
                note = new
                {
                    type = "string",
                    description = "An optional note. Required when status is \"failed\"."
                }
            },
            required = new[] { "id", "status" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Executes the update_task tool.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="arguments">The parsed JSON arguments containing id, status, and optional note.</param>
        /// <param name="workingDirectory">The current working directory (unused).</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> confirming the update or describing the problem.</returns>
        public Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            if (_TaskPlan == null)
            {
                return Task.FromResult(Error(toolCallId, "task_planning_disabled", "Task planning is not active for this run."));
            }

            string id = GetString(arguments, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                return Task.FromResult(Error(toolCallId, "invalid_argument", "The 'id' parameter is required."));
            }

            string statusText = GetString(arguments, "status");
            if (!TryParseStatus(statusText, out AgentTaskStatusEnum status))
            {
                return Task.FromResult(Error(toolCallId, "invalid_status", $"Unknown status '{statusText}'. Valid values: pending, in_progress, completed, failed, skipped, blocked."));
            }

            string? note = arguments.TryGetProperty("note", out JsonElement noteElement) && noteElement.ValueKind == JsonValueKind.String
                ? noteElement.GetString()
                : null;

            if (status == AgentTaskStatusEnum.Failed && string.IsNullOrWhiteSpace(note))
            {
                return Task.FromResult(Error(toolCallId, "note_required", "A note is required when marking a task failed."));
            }

            bool found = _TaskPlan.TryUpdateTask(id, status, note, out _);
            if (!found)
            {
                List<string> ids = new List<string>();
                foreach (AgentTask task in _TaskPlan.Snapshot())
                {
                    ids.Add(task.Id);
                }

                return Task.FromResult(new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "unknown_task", message = $"No task with id '{id}'.", validIds = ids })
                });
            }

            return Task.FromResult(new ToolResult
            {
                ToolCallId = toolCallId,
                Success = true,
                Content = JsonSerializer.Serialize(new { ok = true, id, status = statusText })
            });
        }

        #endregion

        #region Private-Methods

        private static bool TryParseStatus(string value, out AgentTaskStatusEnum status)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "pending": status = AgentTaskStatusEnum.Pending; return true;
                case "in_progress": status = AgentTaskStatusEnum.InProgress; return true;
                case "completed": status = AgentTaskStatusEnum.Completed; return true;
                case "failed": status = AgentTaskStatusEnum.Failed; return true;
                case "skipped": status = AgentTaskStatusEnum.Skipped; return true;
                case "blocked": status = AgentTaskStatusEnum.Blocked; return true;
                default: status = AgentTaskStatusEnum.Pending; return false;
            }
        }

        private static string GetString(JsonElement arguments, string propertyName)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static ToolResult Error(string toolCallId, string code, string message)
        {
            return new ToolResult
            {
                ToolCallId = toolCallId,
                Success = false,
                Content = JsonSerializer.Serialize(new { error = code, message })
            };
        }

        #endregion
    }
}
