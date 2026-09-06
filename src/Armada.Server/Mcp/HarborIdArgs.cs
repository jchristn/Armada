namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments carrying a single Harbor identifier.
    /// </summary>
    public class HarborIdArgs
    {
        #region Public-Members

        /// <summary>
        /// Harbor identifier (hbr_ prefix).
        /// </summary>
        public string HarborId { get; set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborIdArgs()
        {
        }

        #endregion
    }
}
