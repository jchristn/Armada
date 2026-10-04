namespace Armada.Core.Services.Health.Json
{
    /// <summary>
    /// Error envelope npm writes as JSON when a command fails.
    /// </summary>
    public class NpmErrorEnvelope
    {
        #region Public-Members

        /// <summary>
        /// The error, or null when the output is not an error.
        /// </summary>
        public NpmError? Error { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public NpmErrorEnvelope()
        {
        }

        #endregion
    }
}
