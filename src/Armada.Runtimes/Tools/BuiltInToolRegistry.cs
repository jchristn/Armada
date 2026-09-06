namespace Armada.Runtimes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools.Tasks;

    /// <summary>
    /// Registry of the built-in coding tools available to an API-endpoint captain's agent loop. Provides tool
    /// definition retrieval and execution routing. Ported from Mux's built-in tool set (file operations,
    /// search, process execution, and task planning), excluding the web tools.
    /// </summary>
    public class BuiltInToolRegistry
    {
        #region Private-Members

        private readonly Dictionary<string, IToolExecutor> _Tools = new Dictionary<string, IToolExecutor>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ToolMutationKind> _MutationKinds = new Dictionary<string, ToolMutationKind>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes the registry with all built-in coding tools. When a task plan is supplied, the
        /// task-planning tools are registered as well.
        /// </summary>
        /// <param name="taskPlan">The per-run task plan the task tools write to, or null to omit the task tools.</param>
        public BuiltInToolRegistry(TaskPlan? taskPlan = null)
        {
            RegisterTool(new ReadFileTool(), ToolMutationKind.ReadOnly);
            RegisterTool(new WriteFileTool(), ToolMutationKind.Mutating);
            RegisterTool(new EditFileTool(), ToolMutationKind.Mutating);
            RegisterTool(new MultiEditTool(), ToolMutationKind.Mutating);
            RegisterTool(new DeleteFileTool(), ToolMutationKind.Mutating);
            RegisterTool(new FileMetadataTool(), ToolMutationKind.ReadOnly);
            RegisterTool(new ListDirectoryTool(), ToolMutationKind.ReadOnly);
            RegisterTool(new ManageDirectoryTool(), ToolMutationKind.Mutating);
            RegisterTool(new GlobTool(), ToolMutationKind.ReadOnly);
            RegisterTool(new GrepTool(), ToolMutationKind.ReadOnly);
            RegisterTool(new RunProcessTool(), ToolMutationKind.Mutating);

            if (taskPlan != null)
            {
                RegisterTool(new PlanTasksTool(taskPlan), ToolMutationKind.ReadOnly);
                RegisterTool(new UpdateTaskTool(taskPlan), ToolMutationKind.ReadOnly);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns all registered tool definitions suitable for sending to the model.
        /// </summary>
        /// <returns>A list of <see cref="ToolDefinition"/> objects describing each tool.</returns>
        public List<ToolDefinition> GetToolDefinitions()
        {
            List<ToolDefinition> definitions = new List<ToolDefinition>();

            foreach (KeyValuePair<string, IToolExecutor> kvp in _Tools)
            {
                ToolDefinition definition = new ToolDefinition
                {
                    Name = kvp.Value.Name,
                    Description = kvp.Value.Description,
                    ParametersSchema = kvp.Value.ParametersSchema
                };

                definitions.Add(definition);
            }

            return definitions;
        }

        /// <summary>
        /// Executes a tool by name, routing the call to the appropriate <see cref="IToolExecutor"/>.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="toolName">The name of the tool to execute.</param>
        /// <param name="arguments">The parsed JSON arguments from the model.</param>
        /// <param name="workingDirectory">The current working directory for the execution context.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> containing the execution output.</returns>
        public async Task<ToolResult> ExecuteAsync(string toolCallId, string toolName, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            if (!_Tools.TryGetValue(toolName, out IToolExecutor? executor))
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "unknown_tool", message = "Tool '" + toolName + "' is not registered." })
                };
            }

            return await executor.ExecuteAsync(toolCallId, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks whether a tool with the specified name is registered.
        /// </summary>
        /// <param name="name">The tool name to look up.</param>
        /// <returns>True if the tool exists; otherwise false.</returns>
        public bool HasTool(string name)
        {
            return _Tools.ContainsKey(name);
        }

        /// <summary>
        /// Gets the mutation classification for a tool. Returns <see cref="ToolMutationKind.Mutating"/> for any
        /// tool that is not a registered read-only built-in.
        /// </summary>
        /// <param name="toolName">The tool name to classify.</param>
        /// <returns>The tool's <see cref="ToolMutationKind"/>.</returns>
        public ToolMutationKind GetMutationKind(string toolName)
        {
            if (toolName != null && _MutationKinds.TryGetValue(toolName, out ToolMutationKind kind))
            {
                return kind;
            }

            return ToolMutationKind.Mutating;
        }

        #endregion

        #region Private-Methods

        private void RegisterTool(IToolExecutor tool, ToolMutationKind mutationKind)
        {
            _Tools[tool.Name] = tool;
            _MutationKinds[tool.Name] = mutationKind;
        }

        #endregion
    }
}
