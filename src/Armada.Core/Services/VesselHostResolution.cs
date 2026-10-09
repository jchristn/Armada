namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// What <see cref="VesselHostResolver.TryResolveAsync"/> found: the host to work on, or why there is none.
    /// </summary>
    public class VesselHostResolution
    {
        #region Public-Members

        /// <summary>
        /// The host, or null when there is none.
        /// </summary>
        public VesselHost? Host { get; private set; } = null;

        /// <summary>
        /// Why there is no host, or null when there is one.
        /// </summary>
        public VesselCheckoutErrorCodeEnum? ErrorCode { get; private set; } = null;

        /// <summary>
        /// What happened and what to set, or null when there is a host.
        /// </summary>
        public string? Message { get; private set; } = null;

        /// <summary>
        /// Why each connected Harbor could not serve the vessel. Never null.
        /// </summary>
        public List<string> HarborReasons { get; private set; } = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate (no host, no reason).
        /// </summary>
        public VesselHostResolution()
        {
        }

        /// <summary>
        /// A host was found.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <returns>The resolution.</returns>
        public static VesselHostResolution Found(VesselHost host)
        {
            return new VesselHostResolution { Host = host ?? throw new ArgumentNullException(nameof(host)) };
        }

        /// <summary>
        /// No host was found.
        /// </summary>
        /// <param name="code">Why.</param>
        /// <param name="message">What happened and what to set.</param>
        /// <param name="harborReasons">Why each connected Harbor could not serve the vessel, or null.</param>
        /// <returns>The resolution.</returns>
        public static VesselHostResolution Unavailable(VesselCheckoutErrorCodeEnum code, string message, List<string>? harborReasons)
        {
            return new VesselHostResolution
            {
                ErrorCode = code,
                Message = message ?? String.Empty,
                HarborReasons = harborReasons != null ? new List<string>(harborReasons) : new List<string>()
            };
        }

        #endregion
    }
}
