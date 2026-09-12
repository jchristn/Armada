namespace Armada.Runtimes.Interfaces
{
    using Armada.Core.Enums;

    /// <summary>
    /// Produces the agent-process runtime used to launch a captain. In Local (standalone) mode this returns
    /// the in-process runtime that spawns the CLI on the Admiral's own host. In Split mode a remote
    /// implementation returns a runtime proxy that carries out the launch on a Harbor over its link. Callers
    /// (the agent lifecycle handler) work against the returned <see cref="IAgentRuntime"/> and its events
    /// identically regardless of where the process physically runs.
    /// </summary>
    public interface IHostProcessExecutor
    {
        /// <summary>
        /// Create the agent runtime for the given runtime type.
        /// </summary>
        /// <param name="runtimeType">Agent runtime type.</param>
        /// <returns>An agent runtime.</returns>
        IAgentRuntime CreateRuntime(AgentRuntimeEnum runtimeType);

        /// <summary>
        /// Create the agent runtime for the given runtime name.
        /// </summary>
        /// <param name="name">Agent runtime name.</param>
        /// <returns>An agent runtime.</returns>
        IAgentRuntime CreateRuntime(string name);
    }
}
