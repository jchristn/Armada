namespace Armada.Core.BlackBox
{
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Runs one recorded black box action (an existing MCP tool and its arguments) as the caller, with the same
    /// validation, authorization and tenant scoping as if the caller had called that tool directly. The Admiral wires an
    /// implementation that invokes the registered MCP tool handler; recovery runs fail with BlackBoxActionsUnavailableException
    /// where none is wired. Implementations must be thread-safe and must never run anything but the given action.
    /// </summary>
    public interface IBlackBoxActionExecutor
    {
        /// <summary>
        /// Run an action.
        /// </summary>
        /// <param name="caller">Caller the tool runs as.</param>
        /// <param name="action">Action (tool and arguments) recorded in the entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The step result (Tool, Succeeded, Message); Order is set by the caller.</returns>
        Task<BlackBoxRecoveryStepResult> ExecuteAsync(AuthContext caller, BlackBoxAction action, CancellationToken token = default);
    }
}
