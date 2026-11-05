namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;
    /// <summary>Persistence for mission scheduling wait intervals.</summary>
    public interface IMissionWaitIntervalMethods
    {
        /// <summary>Persist a new wait interval.</summary>
        Task<MissionWaitInterval> CreateAsync(MissionWaitInterval interval, CancellationToken token = default);
        /// <summary>Read intervals for a mission, optionally restricting the owning tenant.</summary>
        Task<List<MissionWaitInterval>> EnumerateByMissionAsync(string missionId, string? tenantId = null, CancellationToken token = default);
        /// <summary>Close open intervals once; return the number closed.</summary>
        Task<int> CloseOpenAsync(string missionId, DateTime endedUtc, CancellationToken token = default);
        /// <summary>Close every interval left open by a previous Admiral process; return the number closed.</summary>
        /// <param name="endedUtc">Time the intervals end.</param>
        /// <param name="startedBeforeUtc">Only intervals started before this time are closed, so new waits survive.</param>
        /// <param name="detail">Detail recorded on each closed interval.</param>
        /// <param name="token">Cancellation token.</param>
        Task<int> CloseAllOpenAsync(DateTime endedUtc, DateTime startedBeforeUtc, string detail, CancellationToken token = default);
        /// <summary>Delete closed intervals ending before the retention cutoff; preserve open waits.</summary>
        Task<int> DeleteExpiredAsync(DateTime cutoffUtc, CancellationToken token = default);
    }
}
