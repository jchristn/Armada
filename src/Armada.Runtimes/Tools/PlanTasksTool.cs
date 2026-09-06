namespace Armada.Runtimes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools.Tasks;

    /// <summary>
    /// Establishes or replaces the current job's task plan. The model calls this at the start of a large
    /// request to lay out the work as named tasks with optional dependencies, and again when it needs to
    /// reorganize. Read-only with respect to the workspace: it mutates in-memory plan state only, so it
    /// never acquires the write lease.
    /// </summary>
    public class PlanTasksTool : IToolExecutor
    {
        #region Private-Members

        private readonly TaskPlan? _TaskPlan;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="PlanTasksTool"/> class.
        /// </summary>
        /// <param name="taskPlan">The per-job task plan to write, or null when task planning is not active
        /// for the run (the tool then returns an error result).</param>
        public PlanTasksTool(TaskPlan? taskPlan)
        {
            _TaskPlan = taskPlan;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The unique name of this tool.
        /// </summary>
        public string Name => "plan_tasks";

        /// <summary>
        /// A human-readable description of what this tool does.
        /// </summary>
        public string Description => "Establishes or replaces the plan of tasks for the current request. "
            + "Call this at the start of any request that will take more than a couple of steps or spans several files, "
            + "then keep the plan current with update_task. Each task has a stable id, a short title, and optional dependsOn "
            + "ids of tasks that must complete first. Re-calling replaces the whole plan.";

        /// <summary>
        /// The JSON Schema object describing the tool's input parameters.
        /// </summary>
        public object ParametersSchema => new
        {
            type = "object",
            properties = new
            {
                tasks = new
                {
                    type = "array",
                    description = "The ordered list of tasks that make up the plan.",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = new
                            {
                                type = "string",
                                description = "A short stable id unique within the plan, for example \"t1\"."
                            },
                            title = new
                            {
                                type = "string",
                                description = "A short imperative label for the task."
                            },
                            dependsOn = new
                            {
                                type = "array",
                                description = "Ids of tasks that must complete before this one can start. Omit or leave empty when the task has no prerequisites.",
                                items = new { type = "string" }
                            }
                        },
                        required = new[] { "id", "title" }
                    }
                }
            },
            required = new[] { "tasks" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Executes the plan_tasks tool.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="arguments">The parsed JSON arguments containing the tasks array.</param>
        /// <param name="workingDirectory">The current working directory (unused).</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> confirming the plan or describing the validation problems.</returns>
        public Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            if (_TaskPlan == null)
            {
                return Task.FromResult(Error(toolCallId, "task_planning_disabled", "Task planning is not active for this run."));
            }

            List<AgentTask> tasks = new List<AgentTask>();
            if (arguments.TryGetProperty("tasks", out JsonElement tasksElement) && tasksElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement taskElement in tasksElement.EnumerateArray())
                {
                    AgentTask task = new AgentTask
                    {
                        Id = GetString(taskElement, "id"),
                        Title = GetString(taskElement, "title")
                    };

                    if (taskElement.TryGetProperty("dependsOn", out JsonElement dependsElement) && dependsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement dependency in dependsElement.EnumerateArray())
                        {
                            if (dependency.ValueKind == JsonValueKind.String)
                            {
                                task.DependsOn.Add(dependency.GetString() ?? string.Empty);
                            }
                        }
                    }

                    tasks.Add(task);
                }
            }

            TaskPlanValidationResult validation = TaskPlanValidator.Validate(tasks);
            if (!validation.IsValid)
            {
                return Task.FromResult(new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "invalid_plan", problems = validation.Problems })
                });
            }

            _TaskPlan.SetPlan(tasks);

            return Task.FromResult(new ToolResult
            {
                ToolCallId = toolCallId,
                Success = true,
                Content = JsonSerializer.Serialize(new { ok = true, taskCount = tasks.Count })
            });
        }

        #endregion

        #region Private-Methods

        private static string GetString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
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
