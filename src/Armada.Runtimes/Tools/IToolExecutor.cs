namespace Armada.Runtimes.Tools
{
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools;

    /// <summary>
    /// Defines the contract for a tool that can be executed by the agent loop.
    /// </summary>
    public interface IToolExecutor
    {
        /// <summary>
        /// The unique name of the tool, used to route tool calls from the LLM.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// A human-readable description of what the tool does.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// The JSON Schema object describing the tool's input parameters.
        /// </summary>
        object ParametersSchema { get; }

        /// <summary>
        /// Executes the tool with the given arguments and returns the result.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="arguments">The parsed JSON arguments from the LLM.</param>
        /// <param name="workingDirectory">The current working directory for the execution context.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> containing the execution output.</returns>
        Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken);
    }
}
