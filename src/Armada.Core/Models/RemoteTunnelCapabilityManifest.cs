namespace Armada.Core.Models
{
    /// <summary>
    /// Capability manifest advertised by an Armada instance to the remote proxy.
    /// </summary>
    public class RemoteTunnelCapabilityManifest
    {
        #region Public-Members

        /// <summary>
        /// Tunnel protocol version understood by this Armada instance.
        /// </summary>
        public string ProtocolVersion { get; set; } = Constants.RemoteTunnelProtocolVersion;

        /// <summary>
        /// Armada product version.
        /// </summary>
        public string ArmadaVersion { get; set; } = Constants.ProductVersion;

        /// <summary>
        /// Supported feature identifiers.
        /// </summary>
        public List<string> Features
        {
            get => _Features;
            set => _Features = value ?? new List<string>();
        }

        #endregion

        #region Private-Members

        private List<string> _Features = new List<string>();

        #endregion
    }
}
