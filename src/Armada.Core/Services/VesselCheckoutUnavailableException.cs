namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Thrown when an operation needs a vessel's checkout and there is none to use: the Admiral has no usable working
    /// directory for the vessel, and no connected Harbor can serve it. The message says what to set; <see cref="Code"/>
    /// says which case it is. It is an <see cref="InvalidOperationException"/>, so REST and MCP surfaces that map
    /// exceptions by type report it as a client error.
    /// </summary>
    public class VesselCheckoutUnavailableException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// Stable error code a REST error body carries in Data.Code and an MCP tool error in Code.
        /// </summary>
        public const string ErrorCode = "VesselCheckoutUnavailable";

        /// <summary>
        /// Which case it is.
        /// </summary>
        public VesselCheckoutErrorCodeEnum Code { get; }

        /// <summary>
        /// Vessel identifier.
        /// </summary>
        public string VesselId { get; }

        /// <summary>
        /// Vessel name.
        /// </summary>
        public string VesselName { get; }

        /// <summary>
        /// Why each connected Harbor could not serve the vessel, one entry per Harbor ("Harbor Mac (hbr_...): ..."). Empty
        /// when no Harbor was asked.
        /// </summary>
        public List<string> HarborReasons { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="code">Which case it is.</param>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="vesselName">Vessel name.</param>
        /// <param name="message">What happened and what to set.</param>
        /// <param name="harborReasons">Why each connected Harbor could not serve the vessel, or null.</param>
        public VesselCheckoutUnavailableException(VesselCheckoutErrorCodeEnum code, string vesselId, string vesselName, string message, List<string>? harborReasons = null)
            : base(message)
        {
            Code = code;
            VesselId = vesselId ?? String.Empty;
            VesselName = vesselName ?? String.Empty;
            HarborReasons = harborReasons != null ? new List<string>(harborReasons) : new List<string>();
        }

        #endregion
    }
}
