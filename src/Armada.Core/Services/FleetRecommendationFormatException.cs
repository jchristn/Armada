namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised when the fleet-recommendations.json a captain wrote is missing, is not valid JSON, does not match the
    /// expected shape, or references none of the imported vessels. The categorization job fails with this message.
    /// </summary>
    public class FleetRecommendationFormatException : Exception
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Error message describing what was wrong with the file.</param>
        public FleetRecommendationFormatException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate with an inner exception.
        /// </summary>
        /// <param name="message">Error message describing what was wrong with the file.</param>
        /// <param name="inner">Underlying parse error.</param>
        public FleetRecommendationFormatException(string message, Exception inner) : base(message, inner)
        {
        }

        #endregion
    }
}
