namespace Armada.Core.Services.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Consulted by the Admiral just before a dispatched mission is created: reports overlaps with active work and brief
    /// lint findings, and may chain the mission after an overlapping one (set DependsOnMissionId) when the vessel asks
    /// for it. Advice never blocks a dispatch; an advisor that fails is ignored.
    /// </summary>
    public interface IMissionDispatchAdvisor
    {
        /// <summary>
        /// Advise on a mission that is about to be created. The advisor may set <see cref="Mission.DependsOnMissionId"/>
        /// (only when it is empty and the vessel serializes overlapping missions); it changes nothing else.
        /// </summary>
        /// <param name="mission">The mission, not yet created.</param>
        /// <param name="vessel">Its vessel.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The advice, or null when there is none.</returns>
        Task<MissionDispatchAdvice?> AdviseAsync(Mission mission, Vessel vessel, CancellationToken token = default);
    }
}
