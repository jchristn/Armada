namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments carrying a single model endpoint identifier.
    /// </summary>
    public class ModelEndpointIdArgs
    {
        #region Public-Members

        /// <summary>
        /// Model endpoint identifier (mep_ prefix).
        /// </summary>
        public string EndpointId { get; set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelEndpointIdArgs()
        {
        }

        #endregion
    }
}
