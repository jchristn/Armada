namespace Armada.Core.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Models;

    /// <summary>
    /// What <see cref="CaptainLinkMonitor"/> needs from the Admiral that runs the health check.
    /// </summary>
    internal interface ICaptainLinkHost
    {
        /// <summary>
        /// The Harbor link manager, or null when no Harbor can host captains.
        /// </summary>
        HarborConnectionManager? HarborLinks { get; }

        /// <summary>
        /// The black box that records non-terminal mission failures, or null.
        /// </summary>
        IBlackBoxCapture? BlackBox { get; }

        /// <summary>
        /// The Admiral's current UTC time.
        /// </summary>
        /// <returns>Current UTC time.</returns>
        DateTime UtcNow();

        /// <summary>
        /// Emit a captain link-episode event.
        /// </summary>
        /// <param name="eventType">Event type, for example captain.unreachable.</param>
        /// <param name="message">Event message.</param>
        /// <param name="captain">Captain the episode belongs to.</param>
        /// <param name="mission">Mission the captain works on, or null.</param>
        /// <param name="missionId">Mission id to record when no mission is given, or null.</param>
        /// <param name="payload">Serialized event payload.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task EmitCaptainEventAsync(string eventType, string message, Captain captain, Mission? mission, string? missionId, string payload, CancellationToken token);
    }
}
