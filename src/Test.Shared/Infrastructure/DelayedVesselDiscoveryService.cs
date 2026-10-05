namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Wraps a discovery service and makes each <see cref="DiscoverAsync"/> take a chosen time, so a test can line the
    /// end of a background discovery up with the job heartbeat.
    /// </summary>
    public sealed class DelayedVesselDiscoveryService : IVesselDiscoveryService
    {
        #region Public-Members

        /// <summary>
        /// Delay for the next discovery; read once per call.
        /// </summary>
        public Func<TimeSpan> Delay { get; set; } = () => TimeSpan.Zero;

        #endregion

        #region Private-Members

        private readonly IVesselDiscoveryService _Inner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="inner">The real discovery service.</param>
        public DelayedVesselDiscoveryService(IVesselDiscoveryService inner)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<VesselDiscoveryResult> DiscoverAsync(string tenantId, VesselDiscoveryRequest request, CancellationToken token = default)
        {
            VesselDiscoveryResult result = await _Inner.DiscoverAsync(tenantId, request, token).ConfigureAwait(false);
            TimeSpan delay = Delay();
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
            return result;
        }

        /// <inheritdoc />
        public void ValidateRequest(VesselDiscoveryRequest request)
        {
            _Inner.ValidateRequest(request);
        }

        /// <inheritdoc />
        public Task<VesselBrowseResult> BrowseAsync(string? path, CancellationToken token = default)
        {
            return _Inner.BrowseAsync(path, token);
        }

        /// <inheritdoc />
        public List<string> GetAllowedRoots()
        {
            return _Inner.GetAllowedRoots();
        }

        #endregion
    }
}
