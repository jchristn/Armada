namespace Test.Shared.Infrastructure
{
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Stands in for the Admiral dispatcher loop in service-level tests (RS-4.5). Completion and review only wake the
    /// dispatcher; this counts the wakes and runs the one pass the loop would run.
    /// </summary>
    internal sealed class DispatchWakeProbe
    {
        private readonly AdmiralService _Admiral;
        private int _Wakes = 0;

        internal DispatchWakeProbe(LoggingModule logging, DatabaseDriver db, ArmadaSettings settings, ICaptainService captains, MissionService missions, IDockService docks)
        {
            _Admiral = new AdmiralService(logging, db, settings, captains, missions, new VoyageService(logging, db), docks);
            missions.DispatchRequested = () => Interlocked.Increment(ref _Wakes);
        }

        /// <summary>Wakes raised since the last pass.</summary>
        internal int Wakes => Volatile.Read(ref _Wakes);

        /// <summary>Assert that a wake was raised, then run the dispatch pass it asked for.</summary>
        internal async Task DispatchAsync(string because = "a wake was raised")
        {
            AssertTrue(Interlocked.Exchange(ref _Wakes, 0) > 0, because);
            await _Admiral.DispatchOnceAsync().ConfigureAwait(false);
        }
    }
}
