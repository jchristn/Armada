namespace Armada.Helm.Infrastructure
{
    /// <summary>
    /// The targeting options given on the command line (each null when absent).
    /// </summary>
    public class AdmiralTargetRequest
    {
        #region Public-Members

        /// <summary>
        /// <c>--server</c> value, or null.
        /// </summary>
        public string? Server { get; set; } = null;

        /// <summary>
        /// <c>--token</c> value, or null.
        /// </summary>
        public string? Token { get; set; } = null;

        /// <summary>
        /// <c>--profile</c> value, or null.
        /// </summary>
        public string? Profile { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AdmiralTargetRequest()
        {
        }

        #endregion
    }
}
