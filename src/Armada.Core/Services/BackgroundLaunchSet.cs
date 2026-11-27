namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>Background dock provisioning and launches still running, by mission.</summary>
    internal sealed class BackgroundLaunchSet
    {
        private readonly ConcurrentDictionary<string, Task> _Launches = new ConcurrentDictionary<string, Task>(StringComparer.Ordinal);

        /// <summary>Track a launch until it finishes.</summary>
        /// <param name="missionId">Mission ID.</param>
        /// <param name="launch">The running launch.</param>
        internal void Track(string missionId, Task launch)
        {
            _Launches[missionId] = launch;
            _ = launch.ContinueWith(_ => _Launches.TryRemove(new KeyValuePair<string, Task>(missionId, launch)), TaskScheduler.Default);
        }

        /// <summary>Wait for every tracked launch to finish.</summary>
        /// <param name="timeout">Longest time to wait.</param>
        /// <returns>True when none remain.</returns>
        internal async Task<bool> WaitAllAsync(TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (!_Launches.IsEmpty)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) return false;
                Task all = Task.WhenAll(_Launches.Values.ToArray());
                await Task.WhenAny(all, Task.Delay(remaining)).ConfigureAwait(false);
                await Task.Yield();
            }
            return true;
        }
    }
}
