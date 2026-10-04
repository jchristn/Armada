namespace Armada.Core.Services.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Runs one headless captain turn for a server-side caller. Implemented by the server's captain chat service and
    /// stubbed in tests.
    /// </summary>
    public interface IAskCaptainTurnRunner
    {
        /// <summary>
        /// Run a captain turn to completion (or timeout/cancellation).
        /// </summary>
        /// <param name="options">Turn inputs and callbacks.</param>
        /// <param name="token">Cancellation token; cancelling stops the captain's process.</param>
        /// <returns>The reply (Success false with Error on failure) and the observed tool calls.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        Task<CaptainChatTurnResult> RunTurnAsync(CaptainChatTurnOptions options, CancellationToken token = default);
    }
}
