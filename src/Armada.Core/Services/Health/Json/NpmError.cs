namespace Armada.Core.Services.Health.Json
{
    /// <summary>
    /// An npm error (for example ENOLOCK).
    /// </summary>
    public class NpmError
    {
        #region Public-Members

        /// <summary>
        /// Error code.
        /// </summary>
        public string? Code { get; set; } = null;

        /// <summary>
        /// Error summary.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Error detail.
        /// </summary>
        public string? Detail { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public NpmError()
        {
        }

        #endregion
    }
}
