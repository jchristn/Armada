namespace Armada.Core.Services.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Runs the in-dock Definition-of-Done gate for a mission before acceptance.
    /// </summary>
    public interface IDefinitionOfDoneGate
    {
        /// <summary>
        /// Run the vessel's build and unit-test commands inside a mission's checkout and classify the result.
        /// The call is serialized host-wide so only one gate executes at a time. When the gate is disabled or
        /// no commands are configured, returns a skipped, passing result.
        /// </summary>
        /// <param name="vessel">The vessel whose Definition-of-Done configuration governs the gate.</param>
        /// <param name="worktreePath">Path to the mission's checkout in which to run the commands.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The classified gate result.</returns>
        Task<DefinitionOfDoneResult> EvaluateAsync(Vessel vessel, string worktreePath, CancellationToken token = default);
    }
}
