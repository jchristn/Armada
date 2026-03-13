namespace Armada.Test.Automated
{
    using System.Net;
    using Armada.Core.Models;

    /// <summary>
    /// Result of a fleet list or enumerate API call, containing the status code and enumeration result.
    /// </summary>
    public class FleetListResult
    {
        #region Public-Members

        /// <summary>
        /// HTTP status code from the response.
        /// </summary>
        public HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// The enumeration result containing fleets.
        /// </summary>
        public EnumerationResult<Fleet> Result { get; set; } = null!;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public FleetListResult()
        {
        }

        /// <summary>
        /// Instantiate with status code and result.
        /// </summary>
        /// <param name="statusCode">HTTP status code from the response.</param>
        /// <param name="result">The enumeration result containing fleets.</param>
        public FleetListResult(HttpStatusCode statusCode, EnumerationResult<Fleet> result)
        {
            StatusCode = statusCode;
            Result = result ?? throw new System.ArgumentNullException(nameof(result));
        }

        #endregion
    }
}
