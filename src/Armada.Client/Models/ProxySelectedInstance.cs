namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// The Armada instance selected in an Armada.Proxy session.
    /// </summary>
    public class ProxySelectedInstance
    {
        #region Public-Members

        /// <summary>
        /// Instance id.
        /// </summary>
        public string? InstanceId { get; set; } = null;

        /// <summary>
        /// Instance state.
        /// </summary>
        public string? State { get; set; } = null;

        /// <summary>
        /// Armada version, or null.
        /// </summary>
        public string? ArmadaVersion { get; set; } = null;

        /// <summary>
        /// Last heartbeat, or null.
        /// </summary>
        public DateTime? LastSeenUtc { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ProxySelectedInstance()
        {
        }

        #endregion
    }
}
