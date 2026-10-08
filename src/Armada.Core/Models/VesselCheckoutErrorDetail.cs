namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// The Data of a REST error for <see cref="VesselCheckoutUnavailableException"/>: the vessel has no checkout Armada can
    /// use, on the Admiral host or on any connected Harbor.
    /// </summary>
    public class VesselCheckoutErrorDetail
    {
        #region Public-Members

        /// <summary>
        /// Stable error code; always <see cref="VesselCheckoutUnavailableException.ErrorCode"/> (VesselCheckoutUnavailable).
        /// </summary>
        public string Code { get; set; } = VesselCheckoutUnavailableException.ErrorCode;

        /// <summary>
        /// Which case it is.
        /// </summary>
        public VesselCheckoutErrorCodeEnum Reason { get; set; } = VesselCheckoutErrorCodeEnum.NoHarborConnected;

        /// <summary>
        /// Vessel identifier.
        /// </summary>
        public string VesselId { get; set; } = String.Empty;

        /// <summary>
        /// Vessel name.
        /// </summary>
        public string VesselName { get; set; } = String.Empty;

        /// <summary>
        /// Why each connected Harbor could not serve the vessel.
        /// </summary>
        public List<string> HarborReasons { get; set; } = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselCheckoutErrorDetail()
        {
        }

        /// <summary>
        /// Build from the exception.
        /// </summary>
        /// <param name="ex">Exception.</param>
        /// <returns>The detail.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="ex"/> is null.</exception>
        public static VesselCheckoutErrorDetail FromException(VesselCheckoutUnavailableException ex)
        {
            if (ex == null) throw new ArgumentNullException(nameof(ex));
            return new VesselCheckoutErrorDetail
            {
                Reason = ex.Code,
                VesselId = ex.VesselId,
                VesselName = ex.VesselName,
                HarborReasons = new List<string>(ex.HarborReasons)
            };
        }

        #endregion
    }
}
