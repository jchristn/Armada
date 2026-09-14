namespace Armada.Publisher.Manifest
{
    /// <summary>
    /// Product-level identity shared by every artifact and channel. Never contains a version;
    /// the version is supplied per release on the command line.
    /// </summary>
    public class ProductInfo
    {
        #region Public-Members

        /// <summary>
        /// Product name (for example "Armada").
        /// </summary>
        public string Name { get; set; } = "Armada";

        /// <summary>
        /// Publisher or vendor name shown in installer metadata.
        /// </summary>
        public string Publisher { get; set; } = string.Empty;

        /// <summary>
        /// Canonical product homepage.
        /// </summary>
        public string Homepage { get; set; } = string.Empty;

        /// <summary>
        /// License description or reference.
        /// </summary>
        public string License { get; set; } = string.Empty;

        #endregion
    }
}
