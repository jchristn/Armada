namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Runs a test body with the process local time zone set through the TZ variable (honored by .NET on
    /// Linux and macOS, not on Windows), so a test can prove that a timestamp with no kind is not moved by
    /// the local offset. Scopes are serialized; disposing restores the previous TZ value.
    /// </summary>
    public sealed class LocalTimeZoneScope : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Default zone: America/New_York, which is never UTC.
        /// </summary>
        public const string DefaultZone = "America/New_York";

        #endregion

        #region Private-Members

        private static readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private readonly string? _PreviousZone;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private LocalTimeZoneScope(string zone)
        {
            _PreviousZone = Environment.GetEnvironmentVariable("TZ");
            Environment.SetEnvironmentVariable("TZ", zone);
            TimeZoneInfo.ClearCachedData();
        }

        /// <summary>
        /// Enter a scope with the local zone set to <paramref name="zone"/>, and verify that the zone has a
        /// non-zero UTC offset on 2020-06-15 15:00, so a test inside it can tell the clocks apart.
        /// </summary>
        /// <param name="zone">IANA zone name. Default: <see cref="DefaultZone"/>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The scope; dispose it to restore the previous zone.</returns>
        /// <exception cref="AssertionException">Thrown when the zone did not move the clock off UTC.</exception>
        public static async Task<LocalTimeZoneScope> EnterAsync(string zone = DefaultZone, CancellationToken token = default)
        {
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            LocalTimeZoneScope scope;
            try
            {
                scope = new LocalTimeZoneScope(zone);
            }
            catch
            {
                _Gate.Release();
                throw;
            }

            DateTime local = new DateTime(2020, 6, 15, 15, 0, 0, DateTimeKind.Local);
            if (local.ToUniversalTime().Ticks == DateTime.SpecifyKind(local, DateTimeKind.Utc).Ticks)
            {
                scope.Dispose();
                throw new AssertionException("TZ=" + zone + " did not move 2020-06-15 15:00 off UTC. Unset DOTNET_SYSTEM_GLOBALIZATION_INVARIANT and install tzdata. A zero offset does not prove this case.");
            }

            return scope;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Restore the previous TZ value and release the scope.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            Environment.SetEnvironmentVariable("TZ", _PreviousZone);
            TimeZoneInfo.ClearCachedData();
            _Gate.Release();
        }

        #endregion
    }
}
