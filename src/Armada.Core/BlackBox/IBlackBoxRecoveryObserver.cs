namespace Armada.Core.BlackBox
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Told about every confirmed (not dry-run) recovery run, after it finished. Used for recovery outcome tracking. An
    /// observer that throws is logged and ignored.
    /// </summary>
    public interface IBlackBoxRecoveryObserver
    {
        /// <summary>
        /// A recovery path ran.
        /// </summary>
        /// <param name="caller">Who ran it.</param>
        /// <param name="entry">Entry.</param>
        /// <param name="path">The path that ran.</param>
        /// <param name="result">The run result.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task OnRecoveryRunAsync(AuthContext caller, BlackBoxEntry entry, BlackBoxRecoveryPath path, BlackBoxRecoveryRunResult result, CancellationToken token = default);
    }
}
